namespace Lab.Shared.Contracts;

public static class BusinessOperationContracts
{
    public static OperationContractDescriptor StorefrontHostInfo { get; } = OperationContractDescriptor.Create(
        operationName: "storefront-host-info",
        inputs:
        [
            "route"
        ],
        preconditions:
        [
            "Storefront.Api is reachable."
        ],
        postconditions:
        [
            "The response reports that the Storefront host is running.",
            "The response includes enough identity information to understand which host answered."
        ],
        invariants:
        [
            "Storefront.Api is the observation boundary for this host-level response."
        ],
        observationStart: "The host-info request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the host-info request.");

    public static OperationContractDescriptor CatalogHostInfo { get; } = OperationContractDescriptor.Create(
        operationName: "catalog-host-info",
        inputs:
        [
            "route"
        ],
        preconditions:
        [
            "Catalog.Api is reachable."
        ],
        postconditions:
        [
            "The response reports that the Catalog host is running.",
            "The response includes enough identity information to understand which Catalog host answered."
        ],
        invariants:
        [
            "Catalog.Api is the observation boundary for this host-level response."
        ],
        observationStart: "The host-info request arrives at Catalog.Api.",
        observationEnd: "Catalog.Api sends the HTTP response for the host-info request.");

    public static OperationContractDescriptor CartHostInfo { get; } = OperationContractDescriptor.Create(
        operationName: "cart-host-info",
        inputs:
        [
            "route"
        ],
        preconditions:
        [
            "Cart.Api is reachable."
        ],
        postconditions:
        [
            "The response reports that the Cart host is running.",
            "The response includes enough identity information to understand which Cart host answered."
        ],
        invariants:
        [
            "Cart.Api is the observation boundary for this host-level response."
        ],
        observationStart: "The host-info request arrives at Cart.Api.",
        observationEnd: "Cart.Api sends the HTTP response for the host-info request.");

    public static OperationContractDescriptor PaymentSimulatorHostInfo { get; } = OperationContractDescriptor.Create(
        operationName: "payment-simulator-host-info",
        inputs:
        [
            "route"
        ],
        preconditions:
        [
            "PaymentSimulator.Api is reachable."
        ],
        postconditions:
        [
            "The response reports that the PaymentSimulator host is running.",
            "The response includes enough identity information to understand which simulator host answered."
        ],
        invariants:
        [
            "PaymentSimulator.Api is the observation boundary for this host-level response."
        ],
        observationStart: "The host-info request arrives at PaymentSimulator.Api.",
        observationEnd: "PaymentSimulator.Api sends the HTTP response for the host-info request.");

    public static OperationContractDescriptor OrderHostInfo { get; } = OperationContractDescriptor.Create(
        operationName: "order-host-info",
        inputs:
        [
            "route"
        ],
        preconditions:
        [
            "Order.Api is reachable."
        ],
        postconditions:
        [
            "The response reports that the Order host is running.",
            "The response includes enough identity information to understand which order host answered."
        ],
        invariants:
        [
            "Order.Api is the observation boundary for this host-level response."
        ],
        observationStart: "The host-info request arrives at Order.Api.",
        observationEnd: "Order.Api sends the HTTP response for the host-info request.");

    public static OperationContractDescriptor HealthCheck { get; } = OperationContractDescriptor.Create(
        operationName: "health-check",
        inputs:
        [
            "route"
        ],
        preconditions:
        [
            "Storefront.Api is reachable enough to accept the request."
        ],
        postconditions:
        [
            "A 200 response means the Storefront process is alive at the observation boundary."
        ],
        invariants:
        [
            "Health is measured at Storefront.Api, not inferred from another process."
        ],
        observationStart: "The health request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the health request.");

    public static OperationContractDescriptor CpuBoundLab { get; } = OperationContractDescriptor.Create(
        operationName: "cpu-bound-lab",
        inputs:
        [
            "workFactor",
            "iterations"
        ],
        preconditions:
        [
            "The selected work parameters are within the supported CPU lab range."
        ],
        postconditions:
        [
            "The response returns the selected parameters and a deterministic checksum.",
            "The response reflects CPU work completed inside the synchronous Storefront observation boundary."
        ],
        invariants:
        [
            "The endpoint must do real CPU work rather than simulated waiting.",
            "The same input pair must yield the same checksum."
        ],
        observationStart: "The CPU lab request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the CPU lab request.");

    public static OperationContractDescriptor IoBoundLab { get; } = OperationContractDescriptor.Create(
        operationName: "io-bound-lab",
        inputs:
        [
            "delayMs",
            "jitterMs"
        ],
        preconditions:
        [
            "The selected wait parameters are within the supported I/O lab range."
        ],
        postconditions:
        [
            "The response returns the selected delay parameters and the applied downstream wait.",
            "The response reflects waiting completed inside the synchronous Storefront observation boundary."
        ],
        invariants:
        [
            "The endpoint must simulate downstream wait rather than consume material CPU time.",
            "A request trace must explain the requested wait and the applied wait."
        ],
        observationStart: "The I/O lab request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the I/O lab request.");

    public static OperationContractDescriptor ProductPage { get; } = OperationContractDescriptor.Create(
        operationName: "product-page",
        inputs:
        [
            "productId",
            "requested region",
            "caller identity"
        ],
        preconditions:
        [
            "The request names the product to read.",
            "Storefront can reach the chosen product read source or fail explicitly."
        ],
        postconditions:
        [
            "The response returns a renderable product view or an explicit not-found result.",
            "The response reflects the product view that was available inside the observation boundary."
        ],
        invariants:
        [
            "Storefront.Api is the user-visible observation boundary.",
            "Post-response background work does not count toward this contract."
        ],
        observationStart: "The product page request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the final HTTP response for the product page request.");

    public static OperationContractDescriptor CatalogProductDetail { get; } = OperationContractDescriptor.Create(
        operationName: "catalog-product-detail",
        inputs:
        [
            "productId",
            "debug telemetry flag"
        ],
        preconditions:
        [
            "The request names the product to read.",
            "Catalog.Api can reach the primary product read store or fail explicitly."
        ],
        postconditions:
        [
            "The response returns product detail, price, stock state, and version, or an explicit not-found result.",
            "When debug telemetry is requested, the response exposes internal stage metadata for the catalog read path."
        ],
        invariants:
        [
            "Catalog.Api is the observation boundary for this product-detail response.",
            "The response reflects the product and inventory state visible to Catalog.Api during the request."
        ],
        observationStart: "The product detail request arrives at Catalog.Api.",
        observationEnd: "Catalog.Api sends the HTTP response for the product detail request.");

    public static OperationContractDescriptor AddItemToCart { get; } = OperationContractDescriptor.Create(
        operationName: "add-item-to-cart",
        inputs:
        [
            "userId",
            "productId",
            "quantity"
        ],
        preconditions:
        [
            "The request identifies the user and the requested item.",
            "The quantity is valid for cart mutation."
        ],
        postconditions:
        [
            "The resulting cart state includes the requested mutation or an explicit validation failure.",
            "The response reflects the cart state that Storefront committed to returning."
        ],
        invariants:
        [
            "Cart truth lives in durable state, not in server-local memory.",
            "A successful response must describe the resulting cart seen at the boundary."
        ],
        observationStart: "The add-to-cart request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the add-to-cart operation.");

    public static OperationContractDescriptor CartAddItem { get; } = OperationContractDescriptor.Create(
        operationName: "cart-add-item",
        inputs:
        [
            "userId",
            "productId",
            "quantity"
        ],
        preconditions:
        [
            "The request identifies the user and the requested item.",
            "The quantity is valid for cart mutation."
        ],
        postconditions:
        [
            "The resulting cart state includes the requested mutation or an explicit validation failure.",
            "The response reflects the cart state that Cart.Api committed to returning."
        ],
        invariants:
        [
            "Cart truth lives in durable state, not in server-local memory.",
            "A successful response must describe the resulting cart seen at the Cart.Api boundary."
        ],
        observationStart: "The add-item request arrives at Cart.Api.",
        observationEnd: "Cart.Api sends the HTTP response for the add-item request.");

    public static OperationContractDescriptor CartRemoveItem { get; } = OperationContractDescriptor.Create(
        operationName: "cart-remove-item",
        inputs:
        [
            "userId",
            "productId",
            "quantity"
        ],
        preconditions:
        [
            "The request identifies the user and the requested item.",
            "The quantity is valid for cart mutation."
        ],
        postconditions:
        [
            "The resulting cart state reflects the requested removal or an explicit validation failure.",
            "The response reports the cart state visible at the Cart.Api boundary."
        ],
        invariants:
        [
            "Cart truth lives in durable state, not in server-local memory.",
            "Removing an absent item must be explicit and deterministic."
        ],
        observationStart: "The remove-item request arrives at Cart.Api.",
        observationEnd: "Cart.Api sends the HTTP response for the remove-item request.");

    public static OperationContractDescriptor CartGet { get; } = OperationContractDescriptor.Create(
        operationName: "cart-get",
        inputs:
        [
            "userId"
        ],
        preconditions:
        [
            "The request identifies the user whose cart is being read."
        ],
        postconditions:
        [
            "The response returns the cart state visible at Cart.Api, including the explicit empty-cart case."
        ],
        invariants:
        [
            "Cart.Api is the observation boundary for the cart read response.",
            "Cart truth lives in durable state, not in server-local memory."
        ],
        observationStart: "The get-cart request arrives at Cart.Api.",
        observationEnd: "Cart.Api sends the HTTP response for the get-cart request.");

    public static OperationContractDescriptor OrderCheckout { get; } = OperationContractDescriptor.Create(
        operationName: "order-checkout",
        inputs:
        [
            "userId",
            "payment mode",
            "checkout mode",
            "idempotency key"
        ],
        preconditions:
        [
            "The request identifies the cart to validate for checkout.",
            "Order.Api can either complete the selected checkout mode or fail explicitly."
        ],
        postconditions:
        [
            "The response reports the checkout state that Order.Api committed to returning at its current observation boundary."
        ],
        invariants:
        [
            "Order.Api is the current observation boundary for direct checkout calls.",
            "The concrete checkout contract depends on whether the request selected sync or async mode."
        ],
        observationStart: "The checkout request arrives at Order.Api.",
        observationEnd: "Order.Api sends the HTTP response for the checkout request.");

    public static OperationContractDescriptor CheckoutSync { get; } = OperationContractDescriptor.Create(
        operationName: "checkout-sync",
        inputs:
        [
            "userId",
            "cart contents",
            "payment details",
            "idempotency key"
        ],
        preconditions:
        [
            "The request identifies the cart to validate and charge.",
            "The synchronous checkout mode is the active contract."
        ],
        postconditions:
        [
            "The response reports whether the synchronous checkout contract was met.",
            "A success response means the required synchronous work completed before the boundary closed."
        ],
        invariants:
        [
            "The synchronous contract is stricter than eventual background completion.",
            "The current implementation closes this synchronous checkout contract at Order.Api.",
            "The final architecture may still wrap this path inside a higher Storefront.Api observation boundary."
        ],
        observationStart: "The checkout request arrives at Order.Api.",
        observationEnd: "Order.Api sends the HTTP response for the synchronous checkout request.");

    public static OperationContractDescriptor CheckoutAsync { get; } = OperationContractDescriptor.Create(
        operationName: "checkout-async",
        inputs:
        [
            "userId",
            "cart contents",
            "payment details",
            "idempotency key"
        ],
        preconditions:
        [
            "The request identifies the cart to validate and accept for background payment confirmation.",
            "The asynchronous checkout mode is the active contract."
        ],
        postconditions:
        [
            "A success response means Order.Api accepted the checkout and persisted the pending order before the boundary closed.",
            "Background payment confirmation may continue after the response."
        ],
        invariants:
        [
            "The asynchronous contract is weaker than synchronous payment confirmation.",
            "Order.Api must persist both the pending order state and the background job before reporting acceptance."
        ],
        observationStart: "The checkout request arrives at Order.Api.",
        observationEnd: "Order.Api sends the HTTP response for the asynchronous checkout request.");

    public static OperationContractDescriptor StorefrontCheckout { get; } = OperationContractDescriptor.Create(
        operationName: "storefront-checkout",
        inputs:
        [
            "userId",
            "payment mode",
            "checkout mode",
            "idempotency key"
        ],
        preconditions:
        [
            "Storefront.Api can validate the request and reach Order.Api or fail explicitly."
        ],
        postconditions:
        [
            "The response reports the checkout state that Storefront committed to returning at the user-visible boundary."
        ],
        invariants:
        [
            "Storefront.Api is the top-level observation boundary for checkout once this route is used."
        ],
        observationStart: "The checkout request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the checkout request.");

    public static OperationContractDescriptor StorefrontCheckoutSync { get; } = OperationContractDescriptor.Create(
        operationName: "storefront-checkout-sync",
        inputs:
        [
            "userId",
            "payment mode",
            "idempotency key"
        ],
        preconditions:
        [
            "Storefront.Api can validate the request and reach Order.Api in synchronous mode."
        ],
        postconditions:
        [
            "The response reports whether the stricter synchronous checkout contract was met at the top-level Storefront boundary."
        ],
        invariants:
        [
            "Storefront.Api is the user-visible boundary.",
            "The synchronous contract includes downstream payment confirmation before the response closes."
        ],
        observationStart: "The synchronous checkout request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the synchronous checkout request.");

    public static OperationContractDescriptor StorefrontCheckoutAsync { get; } = OperationContractDescriptor.Create(
        operationName: "storefront-checkout-async",
        inputs:
        [
            "userId",
            "payment mode",
            "idempotency key"
        ],
        preconditions:
        [
            "Storefront.Api can validate the request and reach Order.Api in asynchronous mode."
        ],
        postconditions:
        [
            "A success response means Storefront accepted a pending checkout contract at the user-visible boundary.",
            "Background payment confirmation may continue after the response."
        ],
        invariants:
        [
            "Storefront.Api is the user-visible boundary.",
            "The asynchronous contract ends before background payment confirmation completes."
        ],
        observationStart: "The asynchronous checkout request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the asynchronous checkout request.");

    public static OperationContractDescriptor PaymentAuthorize { get; } = OperationContractDescriptor.Create(
        operationName: "payment-authorize",
        inputs:
        [
            "paymentId",
            "orderId",
            "amountCents",
            "mode"
        ],
        preconditions:
        [
            "The request identifies the payment attempt to simulate.",
            "The requested simulation mode is supported."
        ],
        postconditions:
        [
            "The response reports the configured provider behavior for this authorization attempt.",
            "Provider references and callback scheduling details are explicit when the mode requires them."
        ],
        invariants:
        [
            "PaymentSimulator.Api is external to Order.Api and reached over HTTP.",
            "The simulator must behave deterministically for a given request and attempt number."
        ],
        observationStart: "The payment authorization request arrives at PaymentSimulator.Api.",
        observationEnd: "PaymentSimulator.Api sends the HTTP response for the authorization attempt.");

    public static OperationContractDescriptor PaymentAuthorizationStatus { get; } = OperationContractDescriptor.Create(
        operationName: "payment-authorization-status",
        inputs:
        [
            "paymentId"
        ],
        preconditions:
        [
            "The request identifies the simulated payment whose state is being inspected."
        ],
        postconditions:
        [
            "The response returns the simulator-side state known for that payment or an explicit not-found result."
        ],
        invariants:
        [
            "The status view reflects simulator-local state only.",
            "Callback scheduling and delivery outcomes are explicit rather than implied."
        ],
        observationStart: "The payment-status request arrives at PaymentSimulator.Api.",
        observationEnd: "PaymentSimulator.Api sends the HTTP response for the payment-status request.");

    public static OperationContractDescriptor OrderHistory { get; } = OperationContractDescriptor.Create(
        operationName: "order-history",
        inputs:
        [
            "userId",
            "requested read region"
        ],
        preconditions:
        [
            "The request identifies the user whose history is being read.",
            "Storefront can reach the chosen order-history read source or fail explicitly."
        ],
        postconditions:
        [
            "The response returns the order history view visible within the observation boundary.",
            "The response distinguishes an empty history from an unavailable read source."
        ],
        invariants:
        [
            "Projection lag after the response is outside the current observation boundary.",
            "Storefront.Api is still the user-visible measurement point."
        ],
        observationStart: "The order-history request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the final HTTP response for the order-history request.");

    public static IReadOnlyList<OperationContractDescriptor> All { get; } = Array.AsReadOnly(
    [
        ProductPage,
        AddItemToCart,
        CheckoutSync,
        OrderHistory
    ]);
}
