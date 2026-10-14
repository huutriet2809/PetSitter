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
    }
}
