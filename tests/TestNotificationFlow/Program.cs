using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using PetSitter.Controllers;
using PetSitter.Data;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
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
                    Price = 150000m,
                    IsPrimary = true
                };
                await context.SitterServices.InsertOneAsync(sitterService);

                var vnToday = BookingService.GetVnToday();

                // Setup availabilities
                for (int i = -15; i < 20; i++)
                {
                    await context.SitterAvailabilities.InsertOneAsync(new SitterAvailability
                    {
                        SitterId = sitterProfile.SitterId,
                        AvailableDate = vnToday.AddDays(i),
                        IsAvailable = true
                    });
                }

                // -------------------------------------------------------------
                // PHẦN A: BẢNG SỰ KIỆN -> NGƯỜI NHẬN -> TARGETROLE
                // -------------------------------------------------------------
                // A.1: Tạo đơn -> Sitter nhận BOOKING_NEW (TargetRole=SITTER)
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
                AssertTrue(newBookingNotif != null && newBookingNotif.TargetRole == "SITTER", "(a.1) Tạo đơn đặt lịch -> Sitter nhận BOOKING_NEW (TargetRole=SITTER)");

                // A.2: Sitter Accept -> Owner nhận BOOKING_ACCEPTED (TargetRole=OWNER)
                var sitterBookingCtrl = new SitterBookingController(context, bookingService, notificationService, NullLogger<SitterBookingController>.Instance);
                sitterBookingCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, sitterUser.UserId),
                            new Claim(ClaimTypes.Role, "SITTER")
                        }, "TestAuth"))
                    }
                };
                await sitterBookingCtrl.AcceptBooking(createdBooking.BookingId);
                var ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                var acceptNotif = ownerNotifs.FirstOrDefault(n => n.BookingId == createdBooking.BookingId && n.Type == NotificationType.BOOKING_ACCEPTED);
                AssertTrue(acceptNotif != null && acceptNotif.TargetRole == "OWNER", "(a.2) Sitter chấp nhận đơn -> Owner nhận BOOKING_ACCEPTED (TargetRole=OWNER)");

                // A.3: Sitter Reject -> Owner nhận BOOKING_REJECTED (TargetRole=OWNER)
                var createDto2 = new CreateBookingDto
                {
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(5).ToString("yyyy-MM-dd"),
                    NgayKetThuc = vnToday.AddDays(6).ToString("yyyy-MM-dd"),
                    TenPet = "Gau Gau",
                    LoaiPet = "Dog",
                    KichCo = "Medium",
                    Addons = new List<string>()
                };
                var createdBooking2 = await bookingService.CreateBookingAsync(ownerUser.UserId, createDto2);
                createdBookingIds.Add(createdBooking2.BookingId);

                await sitterBookingCtrl.RejectBooking(createdBooking2.BookingId, new RejectBookingDto { LyDo = "Ban lich dot xuat" });
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                var rejectNotif = ownerNotifs.FirstOrDefault(n => n.BookingId == createdBooking2.BookingId && n.Type == NotificationType.BOOKING_REJECTED);
                AssertTrue(rejectNotif != null && rejectNotif.TargetRole == "OWNER", "(a.3) Sitter từ chối đơn -> Owner nhận BOOKING_REJECTED (TargetRole=OWNER)");

                // A.4: Sitter Cancel -> Owner nhận BOOKING_CANCELLED (TargetRole=OWNER)
                var bookingCancelBySitter = new Booking
                {
                    MaDatLich = "PS_CANCEL_BY_SITTER",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(7),
                    NgayKetThuc = vnToday.AddDays(8),
                    SoNgay = 1,
                    TrangThai = BookingStatus.ACCEPTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(bookingCancelBySitter);
                createdBookingIds.Add(bookingCancelBySitter.BookingId);

                await bookingService.CancelBookingAsync(sitterUser.UserId, bookingCancelBySitter.BookingId, "Sitter ban dot xuat");
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                var cancelBySitterNotif = ownerNotifs.FirstOrDefault(n => n.BookingId == bookingCancelBySitter.BookingId && n.Type == NotificationType.BOOKING_CANCELLED);
                AssertTrue(cancelBySitterNotif != null && cancelBySitterNotif.TargetRole == "OWNER", "(a.4) Sitter hủy đơn -> Owner nhận BOOKING_CANCELLED (TargetRole=OWNER)");

                // A.5: Owner Cancel -> Sitter nhận BOOKING_CANCELLED (TargetRole=SITTER)
                var bookingCancelByOwner = new Booking
                {
                    MaDatLich = "PS_CANCEL_BY_OWNER",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(9),
                    NgayKetThuc = vnToday.AddDays(10),
                    SoNgay = 1,
                    TrangThai = BookingStatus.ACCEPTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(bookingCancelByOwner);
                createdBookingIds.Add(bookingCancelByOwner.BookingId);

                await bookingService.CancelBookingAsync(ownerUser.UserId, bookingCancelByOwner.BookingId, "Owner thay doi lich");
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var cancelByOwnerNotif = sitterNotifs.FirstOrDefault(n => n.BookingId == bookingCancelByOwner.BookingId && n.Type == NotificationType.BOOKING_CANCELLED);
                AssertTrue(cancelByOwnerNotif != null && cancelByOwnerNotif.TargetRole == "SITTER", "(a.5) Owner hủy đơn -> Sitter nhận BOOKING_CANCELLED (TargetRole=SITTER)");

                // A.6: Sitter Complete -> Owner nhận BOOKING_COMPLETION_PENDING (TargetRole=OWNER)
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
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                var compPendingNotif = ownerNotifs.FirstOrDefault(n => n.BookingId == testBookingComplete.BookingId && n.Type == NotificationType.BOOKING_COMPLETION_PENDING);
                AssertTrue(compPendingNotif != null && compPendingNotif.TargetRole == "OWNER", "(a.6) Sitter hoàn tất -> Owner nhận BOOKING_COMPLETION_PENDING (TargetRole=OWNER)");

                // A.7: Owner Confirm -> Sitter nhận BOOKING_COMPLETED (TargetRole=SITTER)
                await bookingService.OwnerConfirmCompletionAsync(ownerUser.UserId, testBookingComplete.BookingId);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var compNotif = sitterNotifs.FirstOrDefault(n => n.BookingId == testBookingComplete.BookingId && n.Type == NotificationType.BOOKING_COMPLETED);
                AssertTrue(compNotif != null && compNotif.TargetRole == "SITTER", "(a.7) Owner xác nhận hoàn tất -> Sitter nhận BOOKING_COMPLETED (TargetRole=SITTER)");

                // A.8: Owner Dispute -> Sitter & Admin nhận BOOKING_DISPUTED (NO raw reason in message)
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

                AssertTrue(disputeNotifSitter != null && disputeNotifSitter.TargetRole == "SITTER" &&
                           disputeNotifAdmin != null && disputeNotifAdmin.TargetRole == "ADMIN",
                           "(a.8) Owner khiếu nại -> Sitter (TargetRole=SITTER) và Admin (TargetRole=ADMIN) đều nhận đúng TargetRole");
                AssertTrue(disputeNotifSitter != null && !disputeNotifSitter.Message.Contains(rawSecretReason) &&
                           disputeNotifAdmin != null && !disputeNotifAdmin.Message.Contains(rawSecretReason),
                           "(a.8.1) Thông báo khiếu nại KHÔNG in lý do thô (bảo mật)");

                // A.9: Admin Resolve Dispute -> Owner & Sitter nhận BOOKING_RESOLVED
                await bookingService.AdminResolveDisputeAsync(adminUser.UserId, testBookingDispute.BookingId, new ResolveDisputeDto
                {
                    Resolution = BookingStatus.COMPLETED,
                    Note = "Admin da kiem tra va xac nhan don hop le."
                });
                ownerNotifs = await notificationService.GetNotificationsAsync(ownerUser.UserId);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var resolvedOwner = ownerNotifs.FirstOrDefault(n => n.BookingId == testBookingDispute.BookingId && n.Type == NotificationType.BOOKING_RESOLVED);
                var resolvedSitter = sitterNotifs.FirstOrDefault(n => n.BookingId == testBookingDispute.BookingId && n.Type == NotificationType.BOOKING_RESOLVED);
                AssertTrue(resolvedOwner != null && resolvedOwner.TargetRole == "OWNER" &&
                           resolvedSitter != null && resolvedSitter.TargetRole == "SITTER",
                           "(a.9) Admin giải quyết tranh chấp -> Cả Owner (TargetRole=OWNER) và Sitter (TargetRole=SITTER) đều nhận đúng");

                // -------------------------------------------------------------
                // PHẦN B: LAZY EVALUATION (EXPIRED)
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
                // PHẦN C: 50 REQUEST ĐỌC ĐỒNG THỜI (CONCURRENCY RACE LAZY)
                // -------------------------------------------------------------
                // C.1: 50 request đọc đồng thời cùng một đơn PENDING quá hạn -> EXPIRED
                var racePendingBooking = new Booking
                {
                    MaDatLich = "PS_RACE_LAZY_PENDING",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(1),
                    NgayKetThuc = vnToday.AddDays(2),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-49)
                };
                await context.Bookings.InsertOneAsync(racePendingBooking);
                createdBookingIds.Add(racePendingBooking.BookingId);

                var tasksPending = Enumerable.Range(0, 50).Select(_ => bookingService.ProcessLazyBookingUpdatesAsync(ownerId: ownerUser.UserId));
                await Task.WhenAll(tasksPending);

                var pendingOwnerNotifs = await context.Notifications.Find(n => n.BookingId == racePendingBooking.BookingId && n.UserId == ownerUser.UserId && n.Type == NotificationType.BOOKING_EXPIRED).ToListAsync();
                var pendingSitterNotifs = await context.Notifications.Find(n => n.BookingId == racePendingBooking.BookingId && n.UserId == sitterUser.UserId && n.Type == NotificationType.BOOKING_EXPIRED).ToListAsync();
                var updatedPendingBooking = await context.Bookings.Find(b => b.BookingId == racePendingBooking.BookingId).FirstOrDefaultAsync();

                AssertTrue(pendingOwnerNotifs.Count == 1 && pendingSitterNotifs.Count == 1,
                    $"(c.1) 50 request đọc đồng thời cùng 1 đơn PENDING quá hạn -> Mỗi bên đúng 1 thông báo EXPIRED (Owner={pendingOwnerNotifs.Count}, Sitter={pendingSitterNotifs.Count})");
                AssertTrue(updatedPendingBooking.TrangThai == BookingStatus.EXPIRED && updatedPendingBooking.IsActive == false,
                    "(c.1.2) Đơn chuyển trạng thái EXPIRED hợp lệ, IsActive=false");
                var expiredHistories = updatedPendingBooking.History.Where(h => h.HanhDong == BookingStatus.EXPIRED).ToList();
                AssertTrue(expiredHistories.Count == 1,
                    $"(c.1.3) Lịch sử chuyển trạng thái EXPIRED đúng 1 bản ghi duy nhất (Count={expiredHistories.Count})");

                // C.2: 50 request đọc đồng thời cùng một đơn COMPLETION_PENDING quá hạn -> AUTO_COMPLETED
                var raceCompletionBooking = new Booking
                {
                    MaDatLich = "PS_RACE_LAZY_COMPLETION",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(-5),
                    NgayKetThuc = vnToday.AddDays(-2),
                    TrangThai = BookingStatus.COMPLETION_PENDING,
                    CompletionDeadline = DateTime.UtcNow.AddMinutes(-10),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                };
                await context.Bookings.InsertOneAsync(raceCompletionBooking);
                createdBookingIds.Add(raceCompletionBooking.BookingId);

                var tasksCompletion = Enumerable.Range(0, 50).Select(_ => bookingService.ProcessLazyBookingUpdatesAsync(ownerId: ownerUser.UserId));
                await Task.WhenAll(tasksCompletion);

                var compOwnerNotifs = await context.Notifications.Find(n => n.BookingId == raceCompletionBooking.BookingId && n.UserId == ownerUser.UserId && n.Type == NotificationType.BOOKING_COMPLETED).ToListAsync();
                var compSitterNotifs = await context.Notifications.Find(n => n.BookingId == raceCompletionBooking.BookingId && n.UserId == sitterUser.UserId && n.Type == NotificationType.BOOKING_COMPLETED).ToListAsync();
                var updatedCompBooking = await context.Bookings.Find(b => b.BookingId == raceCompletionBooking.BookingId).FirstOrDefaultAsync();

                AssertTrue(compOwnerNotifs.Count == 1 && compSitterNotifs.Count == 1,
                    $"(c.2) 50 request đọc đồng thời cùng 1 đơn COMPLETION_PENDING quá hạn -> Mỗi bên đúng 1 thông báo COMPLETED (Owner={compOwnerNotifs.Count}, Sitter={compSitterNotifs.Count})");
                AssertTrue(updatedCompBooking.TrangThai == BookingStatus.COMPLETED && updatedCompBooking.AutoCompleted == true && updatedCompBooking.IsActive == false,
                    "(c.2.2) Đơn chuyển trạng thái COMPLETED (AutoCompleted=true) hợp lệ, IsActive=false");
                var compHistories = updatedCompBooking.History.Where(h => h.HanhDong == "AUTO_COMPLETED").ToList();
                AssertTrue(compHistories.Count == 1,
                    $"(c.2.3) Lịch sử chuyển trạng thái AUTO_COMPLETED đúng 1 bản ghi duy nhất (Count={compHistories.Count})");

                // -------------------------------------------------------------
                // PHẦN D: PHÂN TRANG CON TRỎ & SỐ ĐẾM CHƯA ĐỌC
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
                // PHẦN E: RESILIENCE (Lỗi thông báo không làm gián đoạn đổi trạng thái booking)
                // -------------------------------------------------------------
                var testResilienceBooking = new Booking
                {
                    MaDatLich = "PS_TEST_RESIL",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(11),
                    NgayKetThuc = vnToday.AddDays(12),
                    TrangThai = BookingStatus.PENDING,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(testResilienceBooking);
                createdBookingIds.Add(testResilienceBooking.BookingId);

                var resilientBookingService = new BookingService(context, config, null, NullLogger<BookingService>.Instance);
                await resilientBookingService.CancelBookingAsync(ownerUser.UserId, testResilienceBooking.BookingId, "Lý do hủy kiểm thử");
                var checkDbBooking = await context.Bookings.Find(b => b.BookingId == testResilienceBooking.BookingId).FirstOrDefaultAsync();
                AssertTrue(checkDbBooking.TrangThai == BookingStatus.CANCELLED, "(e) Lỗi thông báo không làm gián đoạn chuyển trạng thái booking (Resilience)");

                // -------------------------------------------------------------
                // PHẦN F: KIỂM THỬ QUA HTTP CONTROLLER (401 VÀ 404)
                // -------------------------------------------------------------
                var notifCtrl = new NotificationsController(notificationService);

                // F.1: 401 Unauthorized khi không có token
                notifCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                };
                var unauthorizedRes = await notifCtrl.GetNotifications();
                AssertTrue(unauthorizedRes is UnauthorizedResult, "(f.1) HTTP: Gọi API không có token -> Trả về 401 Unauthorized");

                // F.2: 404 Not Found khi đọc thông báo của người khác
                var testNotifOwner = (await notificationService.GetNotificationsAsync(ownerUser.UserId, limit: 1)).First();
                notifCtrl.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, sitterUser.UserId),
                            new Claim(ClaimTypes.Role, "SITTER")
                        }, "TestAuth"))
                    }
                };

                var idorResult = await notifCtrl.MarkAsRead(testNotifOwner.NotificationId);
                var objectResult = idorResult as ObjectResult;
                AssertTrue(objectResult != null && objectResult.StatusCode == 404,
                    $"(f.2) HTTP: Người dùng đọc thông báo của người khác -> Bị chặn với mã 404 Not Found (StatusCode={objectResult?.StatusCode})");

                // -------------------------------------------------------------
                // PHẦN G: IDEMPOTENCY & MARK ALL AS READ
                // -------------------------------------------------------------
                var readTestNotif = new Notification
                {
                    UserId = ownerUser.UserId,
                    Type = NotificationType.BOOKING_ACCEPTED,
                    Title = "Test Read Title",
                    Message = "Test Read Msg",
                    CreatedAt = DateTime.UtcNow,
                    DedupeKey = $"{testPrefix}_READ_TEST"
                };
                await context.Notifications.InsertOneAsync(readTestNotif);

                await notificationService.MarkAsReadAsync(ownerUser.UserId, readTestNotif.NotificationId);
                var afterRead1 = await context.Notifications.Find(n => n.NotificationId == readTestNotif.NotificationId).FirstOrDefaultAsync();
                var readAtFirstTime = afterRead1.ReadAt;
                AssertTrue(readAtFirstTime != null, "(g.1) MarkAsReadAsync cập nhật ReadAt khi ReadAt == null");

                await Task.Delay(50);
                await notificationService.MarkAsReadAsync(ownerUser.UserId, readTestNotif.NotificationId);
                var afterRead2 = await context.Notifications.Find(n => n.NotificationId == readTestNotif.NotificationId).FirstOrDefaultAsync();
                AssertTrue(afterRead2.ReadAt == readAtFirstTime, "(g.2) MarkAsReadAsync giữ nguyên ReadAt khi đã đọc (idempotent, không đổi thời gian)");

                await notificationService.MarkAllAsReadAsync(ownerUser.UserId);
                var unreadAfterMarkAll = await notificationService.GetUnreadCountAsync(ownerUser.UserId);
                AssertTrue(unreadAfterMarkAll.UnreadCount == 0, "(g.3) MarkAllAsReadAsync đặt toàn bộ thông báo về đã đọc (unreadCount = 0)");

                // -------------------------------------------------------------
                // PHẦN H, I, N, O: NHẮC HẠN & THROTTLE & CAPS
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
                    ExpiresAt = DateTime.UtcNow.AddHours(6),
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
                    CompletionDeadline = DateTime.UtcNow.AddHours(12),
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
                    NgayKetThuc = vnToday.AddDays(-2),
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

                // I: Throttle theo user trong 5 phút
                var testPendingThrottle = new Booking
                {
                    MaDatLich = "PS_TEST_REM_THROT",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(13),
                    NgayKetThuc = vnToday.AddDays(14),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddHours(5),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-43)
                };
                await context.Bookings.InsertOneAsync(testPendingThrottle);
                createdBookingIds.Add(testPendingThrottle.BookingId);

                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: false);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var throttledNotif = sitterNotifs.FirstOrDefault(n => n.BookingId == testPendingThrottle.BookingId && n.Type == NotificationType.REMINDER_PENDING_EXPIRES);
                AssertTrue(throttledNotif == null, "(i) Quét nhắc hạn trong 5 phút được throttle và bỏ qua để tiết kiệm tài nguyên");

                // N: Không sinh nhắc hạn cho đơn đã hết hạn
                var testExpiredBookingReminder = new Booking
                {
                    MaDatLich = "PS_TEST_NO_REM",
                    OwnerId = ownerUser.UserId,
                    SitterId = sitterProfile.SitterId,
                    NgayBatDau = vnToday.AddDays(15),
                    NgayKetThuc = vnToday.AddDays(16),
                    TrangThai = BookingStatus.PENDING,
                    ExpiresAt = DateTime.UtcNow.AddHours(-1),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddHours(-49)
                };
                await context.Bookings.InsertOneAsync(testExpiredBookingReminder);
                createdBookingIds.Add(testExpiredBookingReminder.BookingId);

                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: true);
                sitterNotifs = await notificationService.GetNotificationsAsync(sitterUser.UserId);
                var noReminderForExpired = sitterNotifs.FirstOrDefault(n => n.BookingId == testExpiredBookingReminder.BookingId && n.Type == NotificationType.REMINDER_PENDING_EXPIRES);
                AssertTrue(noReminderForExpired == null, "(n) Nhắc hạn KHÔNG sinh cho đơn đã quá hạn (ExpiresAt <= now)");

                // O: ACCEPTED quá hạn nhắc tối đa N lần (đạt 7 lần không sinh thêm)
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

                await notificationService.ScanAndGenerateRemindersAsync(sitterUser.UserId, force: true);
                var countOverdueForBooking = await context.Notifications.CountDocumentsAsync(n =>
                    n.UserId == sitterUser.UserId &&
                    n.BookingId == testMaxRemindersBooking.BookingId &&
                    n.Type == NotificationType.REMINDER_OVERDUE_ACCEPTED
                );
                AssertTrue(countOverdueForBooking == 7, "(o) Đơn ACCEPTED quá hạn nhắc tối đa N lần (đạt 7 lần không sinh thêm)");

                // -------------------------------------------------------------
                // PHẦN M: ĐỐI SOÁT TẠO BÙ THÔNG BÁO LAZY BỊ THIẾU
                // -------------------------------------------------------------
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

                var missingBefore = await context.Notifications.Find(n => n.BookingId == testMissingNotifBooking.BookingId).ToListAsync();
                AssertTrue(missingBefore.Count == 0, "(m.1) Đơn đối soát ban đầu hoàn toàn thiếu thông báo");

                await bookingService.ProcessLazyBookingUpdatesAsync(ownerId: ownerUser.UserId, forceReconcile: true);
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

                // -------------------------------------------------------------
                // PHẦN I (INDEXES): LISTINDEXES COLLECTION Notifications
                // -------------------------------------------------------------
                Console.WriteLine("\n--- KIỂM TRA CÁC INDEX TRÊN COLLECTION Notifications (listIndexes) ---");
                var indexList = await context.Notifications.Indexes.ListAsync();
                var indexDocs = await indexList.ToListAsync();
                bool hasUniqueDedupe = false;
                bool hasTtlCreatedAt = false;

                foreach (var idx in indexDocs)
                {
                    var name = idx["name"].AsString;
                    var key = idx["key"].ToString();
                    var isUnique = idx.Contains("unique") && idx["unique"].AsBoolean;
                    var ttl = idx.Contains("expireAfterSeconds") ? idx["expireAfterSeconds"].ToString() : null;
                    Console.WriteLine($"  * Index: {name} | Keys: {key} | Unique: {isUnique} | ExpireAfterSeconds: {ttl ?? "none"}");

                    if (key != null && key.Contains("DedupeKey") && isUnique) hasUniqueDedupe = true;
                    if (key != null && key.Contains("CreatedAt") && ttl != null) hasTtlCreatedAt = true;
                }

                AssertTrue(hasUniqueDedupe, "(idx.1) MongoDB listIndexes: Tồn tại Unique Index trên DedupeKey");
                AssertTrue(hasTtlCreatedAt, "(idx.2) MongoDB listIndexes: Tồn tại TTL Index trên CreatedAt (expireAfterSeconds)");
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
