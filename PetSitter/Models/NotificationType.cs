namespace PetSitter.Models
{
    public static class NotificationType
    {
        public const string BOOKING_NEW = "BOOKING_NEW";
        public const string BOOKING_ACCEPTED = "BOOKING_ACCEPTED";
        public const string BOOKING_REJECTED = "BOOKING_REJECTED";
        public const string BOOKING_CANCELLED = "BOOKING_CANCELLED";
        public const string BOOKING_COMPLETION_PENDING = "BOOKING_COMPLETION_PENDING";
        public const string BOOKING_COMPLETED = "BOOKING_COMPLETED";
        public const string BOOKING_DISPUTED = "BOOKING_DISPUTED";
        public const string BOOKING_RESOLVED = "BOOKING_RESOLVED";
        public const string BOOKING_EXPIRED = "BOOKING_EXPIRED";

        // Reminders
        public const string REMINDER_PENDING_EXPIRES = "REMINDER_PENDING_EXPIRES";
        public const string REMINDER_COMPLETION_EXPIRES = "REMINDER_COMPLETION_EXPIRES";
        public const string REMINDER_OVERDUE_ACCEPTED = "REMINDER_OVERDUE_ACCEPTED";
        public const string REMINDER_PAYMENT_EXPIRES = "REMINDER_PAYMENT_EXPIRES";

        // Payments & Refunds
        public const string PAYMENT_SUCCESS = "PAYMENT_SUCCESS";
        public const string EARNINGS_CREDITED = "EARNINGS_CREDITED";
        public const string REFUND_PENDING = "REFUND_PENDING";
        public const string REFUND_COMPLETED = "REFUND_COMPLETED";
        public const string PAYMENT_ABNORMAL = "PAYMENT_ABNORMAL";

        // Payouts & Banking
        public const string WITHDRAWAL_APPROVED = "WITHDRAWAL_APPROVED";
        public const string WITHDRAWAL_REJECTED = "WITHDRAWAL_REJECTED";
        public const string WITHDRAWAL_PAID = "WITHDRAWAL_PAID";
        public const string BANK_ACCOUNT_UPDATED = "BANK_ACCOUNT_UPDATED";
        public const string RECONCILIATION_ALERT = "RECONCILIATION_ALERT";
    }
}
