using System;

namespace PetSitter.DTOs
{
    public class NotificationDto
    {
        public string NotificationId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? BookingId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public bool IsRead => ReadAt != null;
    }

    public class UnreadCountDto
    {
        public int UnreadCount { get; set; }
    }
}
