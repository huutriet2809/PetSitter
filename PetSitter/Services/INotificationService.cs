using PetSitter.DTOs;
using PetSitter.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public interface INotificationService
    {
        Task<bool> CreateNotificationAsync(string userId, string type, string? bookingId, string title, string message, string dedupeKey, string? targetRole = null);
        Task CreateNotificationsAsync(IEnumerable<Notification> notifications);
        Task<List<NotificationDto>> GetNotificationsAsync(string currentUserId, int limit = 20, DateTime? before = null);
        Task<UnreadCountDto> GetUnreadCountAsync(string currentUserId);
        Task MarkAsReadAsync(string currentUserId, string notificationId);
        Task MarkAllAsReadAsync(string currentUserId);
        Task ScanAndGenerateRemindersAsync(string currentUserId, bool force = false);
    }
}
