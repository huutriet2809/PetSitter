namespace PetSitter.Models
{
    public static class PaymentStatus
    {
        public const string NONE = "NONE";
        public const string PENDING_PAYMENT = "PENDING_PAYMENT";
        public const string HELD = "HELD";
        public const string RELEASED = "RELEASED";
        public const string REFUNDED = "REFUNDED";
        public const string PARTIALLY_REFUNDED = "PARTIALLY_REFUNDED";
        public const string REFUND_PENDING = "REFUND_PENDING";
        public const string FAILED = "FAILED";
        public const string EXPIRED = "EXPIRED";
        public const string CANCELLED = "CANCELLED";
    }
}
