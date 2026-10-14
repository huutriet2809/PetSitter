using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class NotificationService : INotificationService
    {
        private readonly PetSitterContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationService> _logger;

        // Ghi chú kiến trúc: _lastScanTimes được lưu trữ trong bộ nhớ tiến trình (in-memory ConcurrentDictionary).
        // Giới hạn hệ thống:
        // 1. Khi tiến trình restart, thông tin throttle trong bộ đệm sẽ bị xóa và được thiết lập lại từ đầu.
        // 2. Trong mô hình nhiều instance (multi-instance/load balancing), bộ đệm này độc lập giữa các tiến trình.
        //    Tuy nhiên, việc quét đồng thời hoặc nhiều lần vẫn an toàn tuyệt đối do cơ chế DedupeKey + unique index tại MongoDB
        //    sẽ ngăn chặn việc tạo trùng bản ghi thông báo.
        // 3. Trong tương lai khi mở rộng quy mô hệ thống, có thể chuyển bộ đệm này sang Redis dạng Distributed Cache (Key-Value với TTL).
        private static readonly ConcurrentDictionary<string, DateTime> _lastScanTimes = new();
        private static DateTime _lastCleanupTime = DateTime.UtcNow;
        private static readonly object _cleanupLock = new();

        public NotificationService(PetSitterContext context, IConfiguration configuration, ILogger<NotificationService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        private static void CleanupStaleScanTimes()
        {
            var now = DateTime.UtcNow;
            if (now - _lastCleanupTime > TimeSpan.FromMinutes(30) || _lastScanTimes.Count > 1000)
            {
                lock (_cleanupLock)
                {
                    if (now - _lastCleanupTime > TimeSpan.FromMinutes(30) || _lastScanTimes.Count > 1000)
                    {
                        var threshold = now.AddHours(-1);
                        foreach (var kvp in _lastScanTimes)
                        {
                            if (kvp.Value < threshold)
                            {
                                _lastScanTimes.TryRemove(kvp.Key, out _);
                            }
                        }
                        _lastCleanupTime = now;
                    }
                }
            }
        }

        public async Task<bool> CreateNotificationAsync(string userId, string type, string? bookingId, string title, string message, string dedupeKey, string? targetRole = null)
        {
            try
            {
                var notif = new Notification
                {
                    UserId = userId,
                    Type = type,
                    BookingId = bookingId,
                    Title = title,
                    Message = message,
                    CreatedAt = DateTime.UtcNow,
                    DedupeKey = dedupeKey,
                    TargetRole = targetRole
                };

                await _context.Notifications.InsertOneAsync(notif);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogDebug("Thông báo với DedupeKey {DedupeKey} đã tồn tại. Bỏ qua êm ái.", dedupeKey);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi chèn thông báo (DedupeKey: {DedupeKey}).", dedupeKey);
                return false;
            }
        }

        public async Task CreateNotificationsAsync(IEnumerable<Notification> notifications)
        {
            foreach (var n in notifications)
            {
                await CreateNotificationAsync(n.UserId, n.Type, n.BookingId, n.Title, n.Message, n.DedupeKey, n.TargetRole);
            }
        }

        public async Task<List<NotificationDto>> GetNotificationsAsync(string currentUserId, int limit = 20, DateTime? before = null)
        {
            // Quét nhắc hạn lười cho chính user (được throttle)
            await ScanAndGenerateRemindersAsync(currentUserId);

            var clampedLimit = Math.Clamp(limit, 1, 50);
            var filterBuilder = Builders<Notification>.Filter;
            var filter = filterBuilder.Eq(n => n.UserId, currentUserId);

            if (before.HasValue)
            {
                filter &= filterBuilder.Lt(n => n.CreatedAt, before.Value);
            }

            var notifications = await _context.Notifications
                .Find(filter)
                .SortByDescending(n => n.CreatedAt)
                .Limit(clampedLimit)
                .ToListAsync();

            return notifications.Select(n => new NotificationDto
            {
                NotificationId = n.NotificationId,
                Type = n.Type,
                BookingId = n.BookingId,
                Title = n.Title,
                Message = n.Message,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt,
                TargetRole = n.TargetRole
            }).ToList();
        }

        public async Task<UnreadCountDto> GetUnreadCountAsync(string currentUserId)
        {
            // Quét nhắc hạn lười cho chính user (được throttle)
            await ScanAndGenerateRemindersAsync(currentUserId);

            var count = await _context.Notifications.CountDocumentsAsync(
                n => n.UserId == currentUserId && n.ReadAt == null
            );

            return new UnreadCountDto { UnreadCount = (int)count };
        }

        public async Task MarkAsReadAsync(string currentUserId, string notificationId)
        {
            var nowUtc = DateTime.UtcNow;
            var filter = Builders<Notification>.Filter.Eq(n => n.NotificationId, notificationId)
                       & Builders<Notification>.Filter.Eq(n => n.UserId, currentUserId)
                       & Builders<Notification>.Filter.Eq(n => n.ReadAt, null);

            var update = Builders<Notification>.Update.Set(n => n.ReadAt, nowUtc);
            var result = await _context.Notifications.UpdateOneAsync(filter, update);

            if (result.MatchedCount == 0)
            {
                // Kiểm tra xem có tồn tại thông báo nhưng thuộc user khác hoặc không tồn tại
                var existing = await _context.Notifications.Find(n => n.NotificationId == notificationId).FirstOrDefaultAsync();
                if (existing == null || existing.UserId != currentUserId)
                {
                    throw new NotFoundException("Không tìm thấy thông tin thông báo.");
                }
                // Nếu existing != null && existing.UserId == currentUserId && existing.ReadAt != null: idempotent, không ném lỗi
            }
        }

        public async Task MarkAllAsReadAsync(string currentUserId)
        {
            var nowUtc = DateTime.UtcNow;
            var filter = Builders<Notification>.Filter.Eq(n => n.UserId, currentUserId)
                       & Builders<Notification>.Filter.Eq(n => n.ReadAt, null);

            var update = Builders<Notification>.Update.Set(n => n.ReadAt, nowUtc);
            await _context.Notifications.UpdateManyAsync(filter, update);
        }

        public async Task ScanAndGenerateRemindersAsync(string currentUserId, bool force = false)
        {
            try
            {
                CleanupStaleScanTimes();

                var minScanMinutes = _configuration.GetValue<int>("BookingReminderSettings:MinScanIntervalMinutes", 5);
                if (!force && _lastScanTimes.TryGetValue(currentUserId, out var lastScan) && DateTime.UtcNow - lastScan < TimeSpan.FromMinutes(minScanMinutes))
                {
                    return; // Đã quét trong vòng 5 phút gần đây, bỏ qua để tiết kiệm tài nguyên
                }

                _lastScanTimes[currentUserId] = DateTime.UtcNow;

                var nowUtc = DateTime.UtcNow;
                var vnToday = BookingService.GetVnToday();
                var pendingExpiringHours = _configuration.GetValue<int>("BookingReminderSettings:PendingExpiringHours", 12);
                var completionExpiringHours = _configuration.GetValue<int>("BookingReminderSettings:CompletionExpiringHours", 24);
                var maxOverdueAccepted = _configuration.GetValue<int>("BookingReminderSettings:MaxOverdueAcceptedReminders", 7);

                // 1. Quét đơn cho Chủ nuôi (Owner)
                var ownerExpiringBookings = await _context.Bookings.Find(b =>
                    b.OwnerId == currentUserId &&
                    b.TrangThai == BookingStatus.COMPLETION_PENDING &&
                    b.CompletionDeadline != null &&
                    b.CompletionDeadline > nowUtc &&
                    b.CompletionDeadline <= nowUtc.AddHours(completionExpiringHours)
                ).ToListAsync();

                foreach (var b in ownerExpiringBookings)
                {
                    var dedupeKey = $"{b.BookingId}:{NotificationType.REMINDER_COMPLETION_EXPIRES}:{currentUserId}";
                    await CreateNotificationAsync(
                        currentUserId,
                        NotificationType.REMINDER_COMPLETION_EXPIRES,
                        b.BookingId,
                        "Sắp hết hạn xác nhận hoàn tất",
                        $"Đơn #{b.MaDatLich} còn dưới {completionExpiringHours} giờ để xác nhận hoặc báo vấn đề trước khi hệ thống tự động hoàn tất.",
                        dedupeKey,
                        targetRole: "OWNER"
                    );
                }

                // 2. Quét đơn cho Người chăm sóc (Sitter)
                var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == currentUserId).FirstOrDefaultAsync();
                if (sitterProfile != null)
                {
                    // a) Đơn PENDING sắp hết hạn (<= 12 giờ)
                    var pendingExpiringBookings = await _context.Bookings.Find(b =>
                        b.SitterId == sitterProfile.SitterId &&
                        b.TrangThai == BookingStatus.PENDING &&
                        b.ExpiresAt != null &&
                        b.ExpiresAt > nowUtc &&
                        b.ExpiresAt <= nowUtc.AddHours(pendingExpiringHours)
                    ).ToListAsync();

                    foreach (var b in pendingExpiringBookings)
                    {
                        var dedupeKey = $"{b.BookingId}:{NotificationType.REMINDER_PENDING_EXPIRES}:{currentUserId}";
                        await CreateNotificationAsync(
                            currentUserId,
                            NotificationType.REMINDER_PENDING_EXPIRES,
                            b.BookingId,
                            "Đơn đặt lịch sắp hết hạn xác nhận",
                            $"Đơn #{b.MaDatLich} chỉ còn dưới {pendingExpiringHours} giờ để phản hồi chấp nhận hoặc từ chối.",
                            dedupeKey,
                            targetRole: "SITTER"
                        );
                    }

                    // b) Đơn ACCEPTED quá ngày kết thúc mà chưa bấm hoàn tất (nhắc tối đa 1 lần/ngày, tối đa N lần)
                    var overdueAcceptedBookings = await _context.Bookings.Find(b =>
                        b.SitterId == sitterProfile.SitterId &&
                        b.TrangThai == BookingStatus.ACCEPTED &&
                        b.NgayKetThuc < vnToday
                    ).ToListAsync();

                    foreach (var b in overdueAcceptedBookings)
                    {
                        var prefix = $"{b.BookingId}:{NotificationType.REMINDER_OVERDUE_ACCEPTED}:";
                        var countFilter = Builders<Notification>.Filter.Eq(n => n.UserId, currentUserId)
                                        & Builders<Notification>.Filter.Regex(n => n.DedupeKey, new MongoDB.Bson.BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(prefix)));

                        var existingCount = await _context.Notifications.CountDocumentsAsync(countFilter);

                        if (existingCount < maxOverdueAccepted)
                        {
                            var vnDateStr = vnToday.ToString("yyyyMMdd");
                            var dedupeKey = $"{prefix}{vnDateStr}:{currentUserId}";
                            await CreateNotificationAsync(
                                currentUserId,
                                NotificationType.REMINDER_OVERDUE_ACCEPTED,
                                b.BookingId,
                                "Nhắc nhở hoàn tất dịch vụ",
                                $"Đơn #{b.MaDatLich} đã qua ngày kết thúc ({b.NgayKetThuc:dd/MM/yyyy}), vui lòng kiểm tra và xác nhận hoàn tất dịch vụ.",
                                dedupeKey,
                                targetRole: "SITTER"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi trong quá trình quét nhắc hạn cho user {UserId}.", currentUserId);
            }
        }
    }
}
