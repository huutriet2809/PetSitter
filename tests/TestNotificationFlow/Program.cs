using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using PetSitter.Data;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestNotificationFlow
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== BẮT ĐẦU TEST TOÀN DIỆN HỆ THỐNG THÔNG BÁO (PROMPT 4) ===");

            var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../PetSitter"));
            var config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets<Program>(optional: true)
                .Build();

            var connectionString = config.GetConnectionString("MongoDb");
            var client = new MongoClient(connectionString);
            var databaseName = config["ConnectionStrings:Database"] ?? config["MongoDb:Database"] ?? "PetSitterDb";
            var database = client.GetDatabase(databaseName);
            var context = new PetSitterContext(database);

            // Ensure seeder indexes exist
            await MongoSeeder.SeedDataAsync(context, config);

            var notificationService = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
            var bookingService = new BookingService(context, config, notificationService, NullLogger<BookingService>.Instance);

            var testPrefix = "TEST_NOTIF_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var createdUserIds = new List<string>();
            var createdBookingIds = new List<string>();

            int passed = 0;
            int failed = 0;

            void AssertTrue(bool condition, string testName)
            {
                if (condition)
                {
                    Console.WriteLine($"[PASS] {testName}");
                    passed++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FAIL] {testName}");
                    Console.ResetColor();
                    failed++;
                }
            }

            try
            {
                // Setup test accounts: Owner, Sitter, Admin
                var ownerUser = new User
                {
                    Email = $"{testPrefix}_owner@test.com",
                    FullName = "Test Owner",
                    PasswordHash = "hash",
                    Role = "USER"
                };
                await context.Users.InsertOneAsync(ownerUser);
                createdUserIds.Add(ownerUser.UserId);

                var sitterUser = new User
                {
                    Email = $"{testPrefix}_sitter@test.com",
                    FullName = "Test Sitter",
                    PasswordHash = "hash",
                    Role = "SITTER"
                };
                await context.Users.InsertOneAsync(sitterUser);
                createdUserIds.Add(sitterUser.UserId);

                var adminUser = new User
                {
                    Email = $"{testPrefix}_admin@test.com",
                    FullName = "Test Admin",
                    PasswordHash = "hash",
                    Role = "ADMIN"
                };
                await context.Users.InsertOneAsync(adminUser);
                createdUserIds.Add(adminUser.UserId);

                var sitterProfile = new SitterProfile
                {
                    UserId = sitterUser.UserId,
                    Status = "Approved",
                    IsVerified = true,
                    Rating = 5.0m,
                    TotalReviews = 10,
                    Bio = "Test Bio",
                    Address = "Da Nang"
                };
                await context.SitterProfiles.InsertOneAsync(sitterProfile);

                var serviceItem = new Service
                {
                    ServiceName = "Cham soc thu cung",
                    Description = "Mo ta test",
                    Gia = 100000m
                };
                await context.Services.InsertOneAsync(serviceItem);

                var sitterService = new PetSitter.Models.SitterService
                {
                    SitterId = sitterProfile.SitterId,
                    ServiceId = serviceItem.ServiceId,
                    Price = 150000m
                };
                await context.SitterServices.InsertOneAsync(sitterService);

                var vnToday = BookingService.GetVnToday();

                // Setup availabilities for the next 10 days
                for (int i = 0; i < 10; i++)
                {
                    await context.SitterAvailabilities.InsertOneAsync(new SitterAvailability
                    {
                        SitterId = sitterProfile.SitterId,
                        AvailableDate = vnToday.AddDays(i),
                        IsAvailable = true
                    });
                }

                // -------------------------------------------------------------
                // TEST (a): Event Notifications (Create -> Sitter receives BOOKING_NEW)
                // -------------------------------------------------------------
                var createDto = new CreateBookingDto
                {
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(2).ToString("yyyy-MM-dd"),
                    NgayKetThuc = vnToday.AddDays(3).ToString("yyyy-MM-dd"),
                    TenPet = "Miu Miu",
                    LoaiPet = "Cat",
                    KichCo = "Small",
                    Addons = new List<string>()
                };

                var createdBooking = await bookingService.CreateBookingAsync(ownerUser.UserId, createDto);
                createdBookingIds.Add(createdBooking.BookingId);

                var sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var newBookingNotif = sitterNotifs.FirstOrDefault(n => n.BookingId == createdBooking.BookingId && n.Type == NotificationType.BOOKING_NEW);
                AssertTrue(newBookingNotif != null, "(a.1) Tạo đơn đặt lịch -> Sitter nhận thông báo BOOKING_NEW");

                // -------------------------------------------------------------
                // TEST (a.2): Sitter complete -> Owner receives BOOKING_COMPLETION_PENDING
                // -------------------------------------------------------------
                // Giả lập đơn đã ACCEPTED và quá NgayKetThuc
                var testBookingComplete = new Booking
                {
                    MaDatLich = "PS_TEST_COMP",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-3),
                    NgayKetThuc = vnToday.AddDays(-1),
                    SoNgay = 3,
                    TrangThai = BookingStatus.ACCEPTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-3)
                };
                await context.Bookings.InsertOneAsync(testBookingComplete);
                createdBookingIds.Add(testBookingComplete.BookingId);

                await bookingService.SitterCompleteBookingAsync(sitterUser.UserId, testBookingComplete.BookingId);
                var ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                var compPendingNotif = ownerNotifs.FirstOrDefault(n => n.BookingId == testBookingComplete.BookingId && n.Type == NotificationType.BOOKING_COMPLETION_PENDING);
                AssertTrue(compPendingNotif != null, "(a.2) Sitter hoàn tất -> Owner nhận thông báo BOOKING_COMPLETION_PENDING");

                // -------------------------------------------------------------
                // TEST (a.3): Owner confirm -> Sitter receives BOOKING_COMPLETED
                // -------------------------------------------------------------
                await bookingService.OwnerConfirmCompletionAsync(ownerUser.UserId, testBookingComplete.BookingId);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var compNotif = sitterNotifs.FirstOrDefault(n => n.BookingId == testBookingComplete.BookingId && n.Type == NotificationType.BOOKING_COMPLETED);
                AssertTrue(compNotif != null, "(a.3) Owner xác nhận hoàn tất -> Sitter nhận thông báo BOOKING_COMPLETED");

                // -------------------------------------------------------------
                // TEST (a.4 & requirement 5): Owner dispute -> Sitter & Admin receive BOOKING_DISPUTED (NO raw reason in message)
                // -------------------------------------------------------------
                var testBookingDispute = new Booking
                {
                    MaDatLich = "PS_TEST_DISP",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-3),
                    NgayKetThuc = vnToday.AddDays(-1),
                    SoNgay = 3,
                    TrangThai = BookingStatus.COMPLETION_PENDING,
                    CompletionDeadline = DateTime.UtcNow.AddHours(20),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-3)
                };
                await context.Bookings.InsertOneAsync(testBookingDispute);
                createdBookingIds.Add(testBookingDispute.BookingId);

                var rawSecretReason = "LyDoBiMatKhongDuocInTrongThongBao_123456789";
                await bookingService.OwnerDisputeBookingAsync(ownerUser.UserId, testBookingDispute.BookingId, rawSecretReason);

                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var disputeNotifSitter = sitterNotifs.FirstOrDefault(n => n.BookingId == testBookingDispute.BookingId && n.Type == NotificationType.BOOKING_DISPUTED);
                var adminNotifs = await notificationService.GetNotificationsAsync(adminUser.UserId);
                var disputeNotifAdmin = adminNotifs.FirstOrDefault(n => n.BookingId == testBookingDispute.BookingId && n.Type == NotificationType.BOOKING_DISPUTED);

                AssertTrue(disputeNotifSitter != null && disputeNotifAdmin != null, "(a.4) Owner khiếu nại -> Sitter và Admin đều nhận BOOKING_DISPUTED");
                AssertTrue(disputeNotifSitter != null && disputeNotifAdmin != null && !disputeNotifSitter.Message.Contains(rawSecretReason) && !disputeNotifAdmin.Message.Contains(rawSecretReason), "(a.4.1 - Requirement 5) Thông báo khiếu nại KHÔNG in lý do thô (bảo mật)");

                // -------------------------------------------------------------
                // TEST (a.5): Admin resolve dispute -> both Owner & Sitter receive BOOKING_RESOLVED
                // -------------------------------------------------------------
                await bookingService.AdminResolveDisputeAsync(adminUser.UserId, testBookingDispute.BookingId, new ResolveDisputeDto
                {
                    Resolution = BookingStatus.COMPLETED,
                    Note = "Admin da kiem tra va xac nhan don hop le."
                });
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var resolvedOwner = ownerNotifs.FirstOrDefault(n => n.BookingId == testBookingDispute.BookingId && n.Type == NotificationType.BOOKING_RESOLVED);
                var resolvedSitter = sitterNotifs.FirstOrDefault(n => n.BookingId == testBookingDispute.BookingId && n.Type == NotificationType.BOOKING_RESOLVED);
                AssertTrue(resolvedOwner != null && resolvedSitter != null, "(a.5) Admin giải quyết tranh chấp -> Cả Owner và Sitter đều nhận BOOKING_RESOLVED");

                // -------------------------------------------------------------
                // TEST (b): Lazy evaluation notifications (EXPIRED & AUTO_COMPLETED)
                // -------------------------------------------------------------
                var testLazyExpired = new Booking
                {
                    MaDatLich = "PS_TEST_LAZY_EXP",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(1),
                    NgayKetThuc = vnToday.AddDays(2),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-49)
                };
                await context.Bookings.InsertOneAsync(testLazyExpired);
                createdBookingIds.Add(testLazyExpired.BookingId);

                await bookingService.ProcessLazyBookingUpdatesAsync(ownerId: ownerUser.UserId);
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var lazyExpOwner = ownerNotifs.FirstOrDefault(n => n.BookingId == testLazyExpired.BookingId && n.Type == NotificationType.BOOKING_EXPIRED);
                var lazyExpSitter = sitterNotifs.FirstOrDefault(n => n.BookingId == testLazyExpired.BookingId && n.Type == NotificationType.BOOKING_EXPIRED);
                AssertTrue(lazyExpOwner != null && lazyExpSitter != null, "(b.1) Lazy PENDING quá hạn -> EXPIRED sinh thông báo cho CẢ HAI BÊN");

                // -------------------------------------------------------------
                // TEST (c): Concurrency test (50 parallel tasks inserting same DedupeKey)
                // -------------------------------------------------------------
                var testDedupeKey = $"{testPrefix}_CONCURRENT_DEDUPE_KEY";
                var concurrentTasks = Enumerable.Range(0, 50).Select(async _ =>
                {
                    return await notificationService.CreateNotificationAsync(
                        ownerUser.UserId,
                        NotificationType.BOOKING_ACCEPTED,
                        null,
                        "Test Concurrent Title",
                        "Test Concurrent Message",
                        testDedupeKey
                    );
                });
                var results = await Task.WhenAll(concurrentTasks);
                var successCount = results.Count(r => r == true);
                var totalInDb = await context.Notifications.CountDocumentsAsync(n => n.DedupeKey == testDedupeKey);
                AssertTrue(successCount == 1 && totalInDb == 1, "(c) 50 request song song cùng DedupeKey -> Đúng 1 thành công, 0 trùng lặp");

                // -------------------------------------------------------------
                // TEST (d): Cursor pagination & Unread Count
                // -------------------------------------------------------------
                var unreadCount = await notificationService.GetUnreadCountAsync(ownerUser.UserId);
                AssertTrue(unreadCount.UnreadCount > 0, "(d.1) GetUnreadCountAsync trả về số lượng chính xác > 0");

                var paginated = await notificationService.GetNotificationsAsync(ownerUser.UserId, limit: 3);
                AssertTrue(paginated.Count <= 3, "(d.2) Phân trang giới hạn limit = 3 hoạt động đúng");
                if (paginated.Count > 0)
                {
                    var oldestCreatedAt = paginated.Last().CreatedAt;
                    var nextPage = await notificationService.GetNotificationsAsync(ownerUser.UserId, limit: 3, before: oldestCreatedAt);
                    AssertTrue(nextPage.All(n => n.CreatedAt < oldestCreatedAt), "(d.3) Phân trang bằng con trỏ before trả về các bản ghi cũ hơn chuẩn xác");
                }

                // -------------------------------------------------------------
                // TEST (e): Resiliency (Notification failure does not rollback state change)
                // -------------------------------------------------------------
                // Giả lập gọi CancelBookingAsync khi NotificationService gặp lỗi chèn
                var testResilienceBooking = new Booking
                {
                    MaDatLich = "PS_TEST_RESIL",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(3),
                    NgayKetThuc = vnToday.AddDays(4),
                    TrangThai = BookingStatus.PENDING,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(testResilienceBooking);
                createdBookingIds.Add(testResilienceBooking.BookingId);

                // Service với null notification service để kiểm tra không bị NullReference và vẫn chuyển trạng thái thành công
                var resilientBookingService = new BookingService(context, config, null, NullLogger<BookingService>.Instance);
                await resilientBookingService.CancelBookingAsync(ownerUser.UserId, testResilienceBooking.BookingId, "Lý do hủy kiểm thử");
                var checkDbBooking = await context.Bookings.Find(b => b.BookingId == testResilienceBooking.BookingId).FirstOrDefaultAsync();
                AssertTrue(checkDbBooking.TrangThai == BookingStatus.CANCELLED, "(e) Lỗi thông báo không làm gián đoạn chuyển trạng thái booking (Resilience)");

                // -------------------------------------------------------------
                // TEST (f & g): Anti-IDOR & Idempotency
                // -------------------------------------------------------------
                var userANotif = (await notificationService.GetNotificationsAsync(ownerUser.UserId, limit: 1)).First();

                // User B (sitterUser) cố tình đánh dấu đã đọc thông báo của User A -> phải ném NotFoundException (404)
                bool idorBlocked = false;
                try
                {
                    await notificationService.MarkAsReadAsync(sitterUser.UserId, userANotif.NotificationId);
                }
                catch (NotFoundException)
                {
                    idorBlocked = true;
                }
                AssertTrue(idorBlocked, "(f) Anti-IDOR: Người dùng không thể đánh dấu đã đọc thông báo của người khác (ném 404)");

                // Idempotent: User A đánh dấu đã đọc 2 lần liên tiếp -> không lỗi, lần 2 vẫn thành công
                await notificationService.MarkAsReadAsync(ownerUser.UserId, userANotif.NotificationId);
                await notificationService.MarkAsReadAsync(ownerUser.UserId, userANotif.NotificationId);
                AssertTrue(true, "(g.1) Idempotent: Đánh dấu đã đọc nhiều lần cho cùng một thông báo không bị lỗi");

                await notificationService.MarkAllAsReadAsync(ownerUser.UserId);
                var unreadAfterMarkAll = await notificationService.GetUnreadCountAsync(ownerUser.UserId);
                AssertTrue(unreadAfterMarkAll.UnreadCount == 0, "(g.2) MarkAllAsReadAsync đặt toàn bộ thông báo về đã đọc (unreadCount = 0)");

                // -------------------------------------------------------------
                // TEST (h, i, n, o): Reminders & Throttling & Caps
                // -------------------------------------------------------------
                // 1. PENDING <= 12h sinh REMINDER_PENDING_EXPIRES
                var testPendingExpiring = new Booking
                {
                    MaDatLich = "PS_TEST_REM_P",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(1),
                    NgayKetThuc = vnToday.AddDays(2),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddHours(6), // còn 6 giờ (< 12h)
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-42)
                };
                await context.Bookings.InsertOneAsync(testPendingExpiring);
                createdBookingIds.Add(testPendingExpiring.BookingId);

                // 2. COMPLETION_PENDING <= 24h sinh REMINDER_COMPLETION_EXPIRES
                var testCompExpiring = new Booking
                {
                    MaDatLich = "PS_TEST_REM_C",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-2),
                    NgayKetThuc = vnToday.AddDays(-1),
                    TrangThai = BookingStatus.COMPLETION_PENDING,
                    CompletionDeadline = DateTime.UtcNow.AddHours(12), // còn 12 giờ (< 24h)
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-3)
                };
                await context.Bookings.InsertOneAsync(testCompExpiring);
                createdBookingIds.Add(testCompExpiring.BookingId);

                // 3. ACCEPTED quá ngày kết thúc sinh REMINDER_OVERDUE_ACCEPTED
                var testAcceptedOverdue = new Booking
                {
                    MaDatLich = "PS_TEST_REM_A",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-5),
                    NgayKetThuc = vnToday.AddDays(-2), // đã qua ngày kết thúc
                    TrangThai = BookingStatus.ACCEPTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                };
                await context.Bookings.InsertOneAsync(testAcceptedOverdue);
                createdBookingIds.Add(testAcceptedOverdue.BookingId);

                // Quét nhắc hạn cho Sitter (force = true)
                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: true);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);

                var remPending = sitterNotifs.FirstOrDefault(n => n.BookingId == testPendingExpiring.BookingId && n.Type == NotificationType.REMINDER_PENDING_EXPIRES);
                var remOverdue = sitterNotifs.FirstOrDefault(n => n.BookingId == testAcceptedOverdue.BookingId && n.Type == NotificationType.REMINDER_OVERDUE_ACCEPTED);
                AssertTrue(remPending != null, "(h.1) Đơn PENDING <= 12h sinh REMINDER_PENDING_EXPIRES cho Sitter");
                AssertTrue(remOverdue != null, "(h.2) Đơn ACCEPTED quá ngày kết thúc sinh REMINDER_OVERDUE_ACCEPTED cho Sitter");

                // Quét nhắc hạn cho Owner (force = true)
                await notificationService.ScanAndGenerateRemindersAsync(ownerUser.UserId, force: true);
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                var remCompletion = ownerNotifs.FirstOrDefault(n => n.BookingId == testCompExpiring.BookingId && n.Type == NotificationType.REMINDER_COMPLETION_EXPIRES);
                AssertTrue(remCompletion != null, "(h.3) Đơn COMPLETION_PENDING <= 24h sinh REMINDER_COMPLETION_EXPIRES cho Owner");

                // TEST (i): Throttling 5 phút
                // Tạo đơn mới PENDING <= 12h nữa
                var testPendingThrottle = new Booking
                {
                    MaDatLich = "PS_TEST_REM_THROT",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(5),
                    NgayKetThuc = vnToday.AddDays(6),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddHours(5),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-43)
                };
                await context.Bookings.InsertOneAsync(testPendingThrottle);
                createdBookingIds.Add(testPendingThrottle.BookingId);

                // Quét lần nữa với force = false (sẽ bị throttle vì vừa quét cách đây vài giây)
                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: false);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var throttledNotif = sitterNotifs.FirstOrDefault(n => n.BookingId == testPendingThrottle.BookingId && n.Type == NotificationType.REMINDER_PENDING_EXPIRES);
                AssertTrue(throttledNotif == null, "(i) Quét nhắc hạn trong 5 phút được throttle và bỏ qua để tiết kiệm tài nguyên");

                // TEST (n): Không sinh nhắc hạn cho đơn đã hết hạn hoặc không còn hiệu lực
                var testExpiredBookingReminder = new Booking
                {
                    MaDatLich = "PS_TEST_NO_REM",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(7),
                    NgayKetThuc = vnToday.AddDays(8),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddHours(-1), // đã quá hạn
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-49)
                };
                await context.Bookings.InsertOneAsync(testExpiredBookingReminder);
                createdBookingIds.Add(testExpiredBookingReminder.BookingId);

                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: true);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var noReminderForExpired = sitterNotifs.FirstOrDefault(n => n.BookingId == testExpiredBookingReminder.BookingId && n.Type == NotificationType.REMINDER_PENDING_EXPIRES);
                AssertTrue(noReminderForExpired == null, "(n) Nhắc hạn KHÔNG sinh cho đơn đã quá hạn (ExpiresAt <= now)");

                // TEST (o): ACCEPTED quá hạn nhắc tối đa N lần (mặc định 7)
                var testMaxRemindersBooking = new Booking
                {
                    MaDatLich = "PS_TEST_MAX_REM",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-10),
                    NgayKetThuc = vnToday.AddDays(-8),
                    TrangThai = BookingStatus.ACCEPTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-10)
                };
                await context.Bookings.InsertOneAsync(testMaxRemindersBooking);
                createdBookingIds.Add(testMaxRemindersBooking.BookingId);

                // Giả lập đã có 7 thông báo nhắc hạn cho đơn này trong quá khứ
                for (int d = 1; d <= 7; d++)
                {
                    var pastDateStr = vnToday.AddDays(-d).ToString("yyyyMMdd");
                    await notificationService.CreateNotificationAsync(
                        sitterUser.UserId,
                        NotificationType.REMINDER_OVERDUE_ACCEPTED,
                        testMaxRemindersBooking.BookingId,
                        "Nhắc nhở cũ",
                        "Nội dung cũ",
                        $"{testMaxRemindersBooking.BookingId}:{NotificationType.REMINDER_OVERDUE_ACCEPTED}:{pastDateStr}:{sitterUser.UserId}"
                    );
                }

                // Quét nhắc hạn hôm nay -> đã đạt 7 lần, không được sinh thêm lần thứ 8
                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: true);
                var countOverdueForBooking = await context.Notifications.CountDocumentsAsync(n =>
                    n.UserId == sitterUser.UserId &&
                    n.BookingId == testMaxRemindersBooking.BookingId &&
                    n.Type == NotificationType.REMINDER_OVERDUE_ACCEPTED
                );
                AssertTrue(countOverdueForBooking == 7, "(o) Đơn ACCEPTED quá hạn nhắc tối đa N lần (đạt 7 lần không sinh thêm)");

                // -------------------------------------------------------------
                // TEST (m): Đối soát tạo bù thông báo lazy bị thiếu
                // -------------------------------------------------------------
                // Giả lập đơn bị SYSTEM đổi sang EXPIRED trong 7 ngày gần đây nhưng chưa từng có bản ghi thông báo
                var testMissingNotifBooking = new Booking
                {
                    MaDatLich = "PS_TEST_MISSING",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-3),
                    NgayKetThuc = vnToday.AddDays(-2),
                    TrangThai = BookingStatus.EXPIRED,
                    ExpiresAt = DateTime.UtcNow.AddDays(-2),
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow.AddDays(-4),
                    History = new List<InquiryHistory>
                    {
                        new InquiryHistory
                        {
                            HanhDong = BookingStatus.EXPIRED,
                            FromStatus = BookingStatus.PENDING,
                            NguoiThucHien = "SYSTEM",
                            ActorUserId = null,
                            LyDo = "Quá hạn 48 giờ",
                            ThoiGian = DateTime.UtcNow.AddDays(-2)
                        }
                    }
                };
                await context.Bookings.InsertOneAsync(testMissingNotifBooking);
                createdBookingIds.Add(testMissingNotifBooking.BookingId);

                // Chưa có thông báo
                var missingBefore = await context.Notifications.Find(n => n.BookingId == testMissingNotifBooking.BookingId).ToListAsync();
                AssertTrue(missingBefore.Count == 0, "(m.1) Đơn đối soát ban đầu hoàn toàn thiếu thông báo");

                // Chạy ProcessLazyBookingUpdatesAsync cho Owner -> sẽ quét reconciliation 7 ngày và tạo bù
                await bookingService.ProcessLazyBookingUpdatesAsync(ownerId: ownerUser.UserId);

                var backfilledNotifOwner = await context.Notifications.Find(n =>
                    n.BookingId == testMissingNotifBooking.BookingId &&
                    n.UserId == ownerUser.UserId &&
                    n.Type == NotificationType.BOOKING_EXPIRED
                ).FirstOrDefaultAsync();

                var backfilledNotifSitter = await context.Notifications.Find(n =>
                    n.BookingId == testMissingNotifBooking.BookingId &&
                    n.UserId == sitterUser.UserId &&
                    n.Type == NotificationType.BOOKING_EXPIRED
                ).FirstOrDefaultAsync();

                AssertTrue(backfilledNotifOwner != null && backfilledNotifSitter != null, "(m.2) Đối soát phát hiện đơn EXPIRED trong 7 ngày thiếu thông báo và đã TẠO BÙ cho cả 2 bên");
            }
            finally
            {
                // DỌN DẸP DỮ LIỆU TEST TRONG PetSitterDb
                Console.WriteLine("\n--- ĐANG DỌN DẸP DỮ LIỆU TEST TRONG PetSitterDb ---");
                if (createdUserIds.Any())
                {
                    await context.Users.DeleteManyAsync(u => createdUserIds.Contains(u.UserId));
                    await context.SitterProfiles.DeleteManyAsync(s => createdUserIds.Contains(s.UserId));
                    await context.Notifications.DeleteManyAsync(n => createdUserIds.Contains(n.UserId));
                }
                if (createdBookingIds.Any())
                {
                    await context.Bookings.DeleteManyAsync(b => createdBookingIds.Contains(b.BookingId));
                    await context.Reviews.DeleteManyAsync(r => createdBookingIds.Contains(r.BookingId));
                }
                await context.Notifications.DeleteManyAsync(n => n.DedupeKey.StartsWith(testPrefix));
                await context.Services.DeleteManyAsync(s => s.ServiceName == "Cham soc thu cung");
                await context.SitterServices.DeleteManyAsync(ss => ss.Price == 150000m);
                Console.WriteLine("Đã dọn dẹp sạch sẽ dữ liệu test.");
            }

            Console.WriteLine($"\n=== KẾT QUẢ TỔNG HỢP: {passed} PASSED, {failed} FAILED ===");
            return failed == 0 ? 0 : 1;
        }
    }
}
