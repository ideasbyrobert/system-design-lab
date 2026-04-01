using Lab.Persistence;
using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using CartEntity = Lab.Persistence.Entities.Cart;
using CartItemEntity = Lab.Persistence.Entities.CartItem;
using ProductEntity = Lab.Persistence.Entities.Product;
using UserEntity = Lab.Persistence.Entities.User;

namespace Cart.Api.CartState;

internal sealed class CartStateService(PrimaryDbContext dbContext, TimeProvider timeProvider)
{
    private const string ActiveStatus = "active";

    public async Task<CartLoadResult> LoadCartForReadAsync(string userId, CancellationToken cancellationToken)
    {
        UserEntity? user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return CartLoadResult.CreateFailure(
                "user_not_found",
                StatusCodes.Status404NotFound,
                $"No user '{userId}' exists in the primary store.");
        }

        CartEntity? cart = await LoadActiveCartAsync(userId, cancellationToken);
        return CartLoadResult.Success(new CartLoadContext(user, cart, Product: null, CartCreated: false), cart is null ? "missing" : "loaded");
    }

    public async Task<CartLoadResult> LoadCartForAddAsync(string userId, string productId, CancellationToken cancellationToken)
    {
        UserEntity? user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return CartLoadResult.CreateFailure(
                "user_not_found",
                StatusCodes.Status404NotFound,
                $"No user '{userId}' exists in the primary store.");
        }

        ProductEntity? product = await FindProductAsync(productId, cancellationToken);

        if (product is null)
        {
            return CartLoadResult.CreateFailure(
                "product_not_found",
                StatusCodes.Status404NotFound,
                $"No product '{productId}' exists in the primary store.");
        }

        CartEntity? cart = await LoadActiveCartAsync(userId, cancellationToken);
        bool cartCreated = false;

        if (cart is null)
        {
            DateTimeOffset nowUtc = timeProvider.GetUtcNow();
            cart = new CartEntity
            {
                CartId = Guid.NewGuid().ToString("N"),
                UserId = user.UserId,
                Region = user.Region,
                Status = ActiveStatus,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };

            dbContext.Carts.Add(cart);
            cartCreated = true;
        }

        return CartLoadResult.Success(new CartLoadContext(user, cart, product, CartCreated: cartCreated), cartCreated ? "created" : "loaded");
    }

    public async Task<CartLoadResult> LoadCartForRemoveAsync(string userId, CancellationToken cancellationToken)
    {
        UserEntity? user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return CartLoadResult.CreateFailure(
                "user_not_found",
                StatusCodes.Status404NotFound,
                $"No user '{userId}' exists in the primary store.");
        }

        CartEntity? cart = await LoadActiveCartAsync(userId, cancellationToken);
        return CartLoadResult.Success(new CartLoadContext(user, cart, Product: null, CartCreated: false), cart is null ? "missing" : "loaded");
    }

    public string ApplyAdd(CartLoadContext context, int quantity)
    {
        CartEntity cart = context.Cart ?? throw new InvalidOperationException("A cart must be available before add-item mutation.");
        ProductEntity product = context.Product ?? throw new InvalidOperationException("A product must be available before add-item mutation.");
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        CartItemEntity? item = cart.Items.SingleOrDefault(existingItem => string.Equals(existingItem.ProductId, product.ProductId, StringComparison.Ordinal));

        if (item is null)
        {
            item = new CartItemEntity
            {
                CartItemId = Guid.NewGuid().ToString("N"),
                CartId = cart.CartId,
                ProductId = product.ProductId,
                Quantity = quantity,
                UnitPriceCents = product.PriceCents,
                AddedUtc = nowUtc
            };

            cart.Items.Add(item);
            cart.UpdatedUtc = nowUtc;
            return "added";
        }

        item.Quantity += quantity;
        item.UnitPriceCents = product.PriceCents;
        item.AddedUtc = nowUtc;
        cart.UpdatedUtc = nowUtc;
        return "accumulated";
    }

    public string ApplyRemove(CartLoadContext context, string productId, int quantity)
    {
        CartEntity? cart = context.Cart;

        if (cart is null)
        {
            return "no_op";
        }

        CartItemEntity? item = cart.Items.SingleOrDefault(existingItem => string.Equals(existingItem.ProductId, productId, StringComparison.Ordinal));

        if (item is null)
        {
            return "no_op";
        }

        if (quantity >= item.Quantity)
        {
            cart.Items.Remove(item);
            dbContext.CartItems.Remove(item);
            cart.UpdatedUtc = timeProvider.GetUtcNow();
            return "removed";
        }

        item.Quantity -= quantity;
        cart.UpdatedUtc = timeProvider.GetUtcNow();
        return "decremented";
    }

    public bool RequiresPersistence(CartLoadContext context, string mutationOutcome) =>
        context.CartCreated ||
        mutationOutcome is "added" or "accumulated" or "decremented" or "removed";

    public Task PersistAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public CartSnapshot CreateSnapshot(CartLoadContext context) =>
        context.Cart is null
            ? CreateMissingCartSnapshot(context.User)
            : CreateSnapshot(context.Cart);

    private Task<UserEntity?> FindUserAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    private Task<ProductEntity?> FindProductAsync(string productId, CancellationToken cancellationToken) =>
        dbContext.Products.SingleOrDefaultAsync(product => product.ProductId == productId, cancellationToken);

    private Task<CartEntity?> LoadActiveCartAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Carts
            .Include(cart => cart.Items)
            .SingleOrDefaultAsync(cart => cart.UserId == userId && cart.Status == ActiveStatus, cancellationToken);

    private static CartSnapshot CreateMissingCartSnapshot(UserEntity user) =>
        new(
            CartId: null,
            UserId: user.UserId,
            Region: user.Region,
            Exists: false,
            Status: "missing",
            CreatedUtc: null,
            UpdatedUtc: null,
            Items: []);

    private static CartSnapshot CreateSnapshot(CartEntity cart) =>
        new(
            CartId: cart.CartId,
            UserId: cart.UserId,
            Region: cart.Region,
            Exists: true,
            Status: cart.Status,
            CreatedUtc: cart.CreatedUtc,
            UpdatedUtc: cart.UpdatedUtc,
            Items: cart.Items
                .OrderBy(item => item.ProductId, StringComparer.Ordinal)
                .Select(item => new CartItemSnapshot(
                    item.ProductId,
                    item.Quantity,
                    item.UnitPriceCents,
                    item.Quantity * item.UnitPriceCents,
                    item.AddedUtc))
                .ToArray());
}

internal sealed record CartLoadContext(
    UserEntity User,
    CartEntity? Cart,
    ProductEntity? Product,
    bool CartCreated);

internal sealed record CartLoadResult(
    CartLoadContext? Context,
    string LoadOutcome,
    CartOperationFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static CartLoadResult Success(CartLoadContext context, string loadOutcome) =>
        new(context, loadOutcome, null);

    public static CartLoadResult CreateFailure(string code, int statusCode, string detail) =>
        new(null, "failed", new CartOperationFailure(code, statusCode, detail));
}

internal sealed record CartSnapshot(
    string? CartId,
    string UserId,
    string Region,
    bool Exists,
    string Status,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc,
    IReadOnlyList<CartItemSnapshot> Items)
{
    public int DistinctItemCount => Items.Count;

    public int TotalQuantity => Items.Sum(item => item.Quantity);

    public int TotalPriceCents => Items.Sum(item => item.LineSubtotalCents);
}

internal sealed record CartItemSnapshot(
    string ProductId,
    int Quantity,
    int UnitPriceSnapshotCents,
    int LineSubtotalCents,
    DateTimeOffset AddedUtc);

internal sealed record CartOperationFailure(
    string Code,
    int StatusCode,
    string Detail);
