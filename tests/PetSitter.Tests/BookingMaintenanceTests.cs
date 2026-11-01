using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using PetSitter.BackgroundServices;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class BookingMaintenanceTests : IDisposable
    {
        private readonly MongoTestServer _server;
        private readonly PetSitterContext _context;
        private readonly IConfiguration _config;
        private readonly BookingMaintenanceBackgroundService _maintenanceService;
        private readonly NotificationService _notificationService;

        public BookingMaintenanceTests()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_MaintenanceDb"));

            // Tạo unique index cho DedupeKey để NotificationService có thể chống gửi trùng lặp
            _context.Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(n => n.DedupeKey),
                new CreateIndexOptions { Unique = true }
            ));

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "BookingMaintenance:AutoCompletionPendingAfterDays", "7" },
                { "BookingMaintenance:ReminderDays:0", "0" },
                { "BookingMaintenance:ReminderDays:1", "1" },
                { "BookingMaintenance:ReminderDays:2", "2" },
                { "BookingMaintenance:ReminderDays:3", "3" },
                { "BookingSettings:CompletionDeadlineDays", "3" },
                { "Payments:PaymentGracePeriodMinutes", "5" }
            };

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _notificationService = new NotificationService(_context, _config, NullLogger<NotificationService>.Instance);

            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            _maintenanceService = new BookingMaintenanceBackgroundService(
                scopeFactoryMock.Object,
                NullLogger<BookingMaintenanceBackgroundService>.Instance,
                _config
            );
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        [Fact]
        public async Task Test1_Job1_PendingExpired_DoesNotProcessTwice()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            var booking = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                OwnerId = ObjectId.GenerateNewId().ToString(),
                SitterId = ObjectId.GenerateNewId().ToString(),
                TrangThai = BookingStatus.PENDING,
                PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                NgayBatDau = nowUtc.AddDays(1),
                NgayKetThuc = nowUtc.AddDays(3),
                ExpiresAt = nowUtc.AddHours(-2), // Đã hết hạn 2 tiếng trước
                IsActive = true,
                CreatedAt = nowUtc.AddDays(-2),
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(booking);

            var bookingServiceMock = new Mock<IBookingService>();

            // Act 1: Chạy lần thứ nhất
            await _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);

            // Assert 1: Đã chuyển sang EXPIRED
            var b1 = await _context.Bookings.Find(b => b.BookingId == booking.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(b1);
            Assert.Equal(BookingStatus.EXPIRED, b1.TrangThai);
            Assert.False(b1.IsActive);
            Assert.Single(b1.History, h => h.HanhDong == BookingStatus.EXPIRED);

            // Act 2: Chạy lần thứ hai (Idempotency check)
            await _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);

            // Assert 2: Không được tạo thêm history hoặc lỗi
            var b2 = await _context.Bookings.Find(b => b.BookingId == booking.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(b2);
            Assert.Equal(BookingStatus.EXPIRED, b2.TrangThai);
            Assert.Single(b2.History, h => h.HanhDong == BookingStatus.EXPIRED);
        }

        [Fact]
        public async Task Test1b_Job1_PendingNotYetExpired_RemainsPending()
        {
            // Arrange: Đơn PENDING còn hạn 24 tiếng nữa
            var nowUtc = DateTime.UtcNow;
            var booking = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                OwnerId = ObjectId.GenerateNewId().ToString(),
                SitterId = ObjectId.GenerateNewId().ToString(),
                TrangThai = BookingStatus.PENDING,
                PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                NgayBatDau = nowUtc.AddDays(2),
                NgayKetThuc = nowUtc.AddDays(4),
                ExpiresAt = nowUtc.AddHours(24), // Còn 24 giờ nữa mới hết hạn
                IsActive = true,
                CreatedAt = nowUtc,
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(booking);

            var bookingServiceMock = new Mock<IBookingService>();

            // Act
            await _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);

            // Assert: Đơn phải giữ nguyên PENDING, không chuyển sang EXPIRED
            var b = await _context.Bookings.Find(x => x.BookingId == booking.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(b);
            Assert.Equal(BookingStatus.PENDING, b.TrangThai);
            Assert.True(b.IsActive);
            Assert.Empty(b.History.Where(h => h.HanhDong == BookingStatus.EXPIRED));
        }

        [Fact]
        public async Task Test2_Job2_CompletionPending_AutoCompleted_Idempotent()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            var booking = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                OwnerId = ObjectId.GenerateNewId().ToString(),
                SitterId = ObjectId.GenerateNewId().ToString(),
                SitterUserId = ObjectId.GenerateNewId().ToString(),
                TrangThai = BookingStatus.COMPLETION_PENDING,
                PaymentStatus = PaymentStatus.HELD,
                NgayBatDau = nowUtc.AddDays(-5),
                NgayKetThuc = nowUtc.AddDays(-3),
                CompletionDeadline = nowUtc.AddHours(-1), // Quá hạn deadline xác nhận
                IsActive = false,
                CreatedAt = nowUtc.AddDays(-6),
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(booking);

            var bookingServiceMock = new Mock<IBookingService>();
            bookingServiceMock
                .Setup(s => s.ProcessLazyBookingUpdatesAsync(booking.OwnerId, null, It.IsAny<DateTime?>(), false))
                .Returns(Task.CompletedTask);

            // Act 1: Chạy lần 1
            await _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);

            // Assert 1: Đã chuyển sang COMPLETED (AutoCompleted = true) và gọi giải ngân 1 lần
            var b1 = await _context.Bookings.Find(b => b.BookingId == booking.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(b1);
            Assert.Equal(BookingStatus.COMPLETED, b1.TrangThai);
            Assert.True(b1.AutoCompleted);
            bookingServiceMock.Verify(s => s.ProcessLazyBookingUpdatesAsync(booking.OwnerId, null, It.IsAny<DateTime?>(), false), Times.Once);

            // Act 2: Chạy lần 2
            await _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);

            // Assert 2: Không được gọi giải ngân lần 2
            bookingServiceMock.Verify(s => s.ProcessLazyBookingUpdatesAsync(booking.OwnerId, null, It.IsAny<DateTime?>(), false), Times.Once);
        }

        [Fact]
        public async Task Test2b_Job2_ConcurrentExecution_ParallelTasks_OnlyCompletesAndReleasesOnce()
        {
            // Arrange: Tạo đơn COMPLETION_PENDING quá hạn deadline
            var nowUtc = DateTime.UtcNow;
            var booking = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK_CONCURRENT" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
                OwnerId = ObjectId.GenerateNewId().ToString(),
                SitterId = ObjectId.GenerateNewId().ToString(),
                SitterUserId = ObjectId.GenerateNewId().ToString(),
                TrangThai = BookingStatus.COMPLETION_PENDING,
                PaymentStatus = PaymentStatus.HELD,
                NgayBatDau = nowUtc.AddDays(-5),
                NgayKetThuc = nowUtc.AddDays(-3),
                CompletionDeadline = nowUtc.AddHours(-1), // Quá deadline
                IsActive = false,
                CreatedAt = nowUtc.AddDays(-6),
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(booking);

            var bookingServiceMock = new Mock<IBookingService>();
            bookingServiceMock
                .Setup(s => s.ProcessLazyBookingUpdatesAsync(booking.OwnerId, null, It.IsAny<DateTime?>(), false))
                .Returns(Task.CompletedTask);

            // Act: Chạy đồng thời 2 task song song gọi RunJob1And2Async
            var task1 = _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);
            var task2 = _maintenanceService.RunJob1And2Async(_context, bookingServiceMock.Object, _notificationService, nowUtc, CancellationToken.None);
            await Task.WhenAll(task1, task2);

            // Assert: Trạng thái là COMPLETED, và giải ngân CHỈ được gọi đúng 1 lần duy nhất nhờ điều kiện cập nhật nguyên tử
            var b = await _context.Bookings.Find(x => x.BookingId == booking.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(b);
            Assert.Equal(BookingStatus.COMPLETED, b.TrangThai);
            Assert.True(b.AutoCompleted);
            bookingServiceMock.Verify(s => s.ProcessLazyBookingUpdatesAsync(booking.OwnerId, null, It.IsAny<DateTime?>(), false), Times.Once);
        }

        [Fact]
        public async Task Test3_Job3_AcceptedOverdue_RemindsAndAutoTimeout_Idempotent()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            var vnToday = BookingService.GetVnToday();

            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();

            // Sitter Profile
            var sitterProfile = new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId
            };
            await _context.SitterProfiles.InsertOneAsync(sitterProfile);

            var ownerId = ObjectId.GenerateNewId().ToString();

            // 1. Đơn T+1 (qua ngày hôm qua) -> nhận reminder
            var bookingReminder = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK_REMIND",
                OwnerId = ownerId,
                SitterId = sitterProfile.SitterId,
                TrangThai = BookingStatus.ACCEPTED,
                PaymentStatus = PaymentStatus.HELD,
                NgayBatDau = vnToday.AddDays(-3),
                NgayKetThuc = vnToday.AddDays(-1), // T+1
                IsActive = true,
                CreatedAt = nowUtc.AddDays(-5),
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(bookingReminder);

            // 2. Đơn T+10 (qua 10 ngày > 7 ngày) -> auto timeout sang COMPLETION_PENDING
            var bookingTimeout = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK_TIMEOUT",
                OwnerId = ownerId,
                SitterId = sitterProfile.SitterId,
                TrangThai = BookingStatus.ACCEPTED,
                PaymentStatus = PaymentStatus.HELD,
                NgayBatDau = vnToday.AddDays(-15),
                NgayKetThuc = vnToday.AddDays(-10), // T+10
                IsActive = true,
                CreatedAt = nowUtc.AddDays(-16),
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(bookingTimeout);

            var bookingServiceMock = new Mock<IBookingService>();

            // Act 1: Chạy vòng 1
            await _maintenanceService.RunJob3Async(_context, bookingServiceMock.Object, _notificationService, null, nowUtc, CancellationToken.None);

            // Assert 1:
            // a) Đơn timeout phải chuyển sang COMPLETION_PENDING và IsActive = false
            var timeoutBooking1 = await _context.Bookings.Find(b => b.BookingId == bookingTimeout.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(timeoutBooking1);
            Assert.Equal(BookingStatus.COMPLETION_PENDING, timeoutBooking1.TrangThai);
            Assert.False(timeoutBooking1.IsActive); // Pet được mở khóa
            Assert.NotNull(timeoutBooking1.CompletionDeadline);

            // b) Đơn reminder nhận được đúng 1 in-app notification cho Sitter
            var notifs1 = await _context.Notifications
                .Find(n => n.BookingId == bookingReminder.BookingId && n.UserId == sitterProfile.UserId)
                .ToListAsync();
            Assert.Single(notifs1);

            // Act 2: Chạy vòng 2 cùng ngày (Idempotency check)
            await _maintenanceService.RunJob3Async(_context, bookingServiceMock.Object, _notificationService, null, nowUtc, CancellationToken.None);

            // Assert 2:
            // Không được sinh thêm notification trùng cho Sitter
            var notifs2 = await _context.Notifications
                .Find(n => n.BookingId == bookingReminder.BookingId && n.UserId == sitterProfile.UserId)
                .ToListAsync();
            Assert.Single(notifs2);

            // Đơn timeout vẫn ở COMPLETION_PENDING
            var timeoutBooking2 = await _context.Bookings.Find(b => b.BookingId == bookingTimeout.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(timeoutBooking2);
            Assert.Equal(BookingStatus.COMPLETION_PENDING, timeoutBooking2.TrangThai);
        }

        [Fact]
        public async Task Test3b_Job3_AcceptedOverdue_TPlus5_DoesNotRemindAndDoesNotTimeout()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            var vnToday = BookingService.GetVnToday();

            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();

            var sitterProfile = new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId
            };
            await _context.SitterProfiles.InsertOneAsync(sitterProfile);

            var ownerId = ObjectId.GenerateNewId().ToString();

            // Đơn T+5: đã qua ngày kết thúc 5 ngày (ReminderDays cấu hình là 0, 1, 2, 3 -> không nhắc; AutoCompletionPendingAfterDays là 7 -> chưa timeout)
            var bookingT5 = new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = "BK_T5_TEST",
                OwnerId = ownerId,
                SitterId = sitterProfile.SitterId,
                TrangThai = BookingStatus.ACCEPTED,
                PaymentStatus = PaymentStatus.HELD,
                NgayBatDau = vnToday.AddDays(-8),
                NgayKetThuc = vnToday.AddDays(-5), // T+5
                IsActive = true,
                CreatedAt = nowUtc.AddDays(-10),
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(bookingT5);

            var bookingServiceMock = new Mock<IBookingService>();

            // Act
            await _maintenanceService.RunJob3Async(_context, bookingServiceMock.Object, _notificationService, null, nowUtc, CancellationToken.None);

            // Assert:
            // 1. Đơn vẫn giữ nguyên trạng thái ACCEPTED, IsActive = true
            var b = await _context.Bookings.Find(x => x.BookingId == bookingT5.BookingId).FirstOrDefaultAsync();
            Assert.NotNull(b);
            Assert.Equal(BookingStatus.ACCEPTED, b.TrangThai);
            Assert.True(b.IsActive);
            Assert.Null(b.CompletionDeadline);

            // 2. Không có notification nào được gửi cho Sitter hoặc Owner ở mốc T+5
            var notifs = await _context.Notifications
                .Find(n => n.BookingId == bookingT5.BookingId)
                .ToListAsync();
            Assert.Empty(notifs);
        }
    }
}
