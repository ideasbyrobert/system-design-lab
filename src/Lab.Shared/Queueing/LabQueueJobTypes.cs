namespace Lab.Shared.Queueing;

public static class LabQueueJobTypes
{
    public const string PaymentConfirmationRetry = "payment-confirmation-retry";

    public const string OrderHistoryProjectionUpdate = "order-history-projection-update";

    public const string ProductPageProjectionRebuild = "product-page-projection-rebuild";
}
