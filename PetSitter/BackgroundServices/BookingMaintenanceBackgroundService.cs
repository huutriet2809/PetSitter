using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PetSitter.BackgroundServices
{
    /// <summary>
    /// Background service chạy định kỳ xử lý các tác vụ bảo trì đơn đặt lịch:
    ///   Job 1 – Đơn PENDING quá 48h → EXPIRED
    ///   Job 2 – Đơn COMPLETION_PENDING quá CompletionDeadline → AUTO_COMPLETED
    ///   Job 3 – Đơn ACCEPTED quá NgayKetThuc: nhắc Sitter T+0..T+3, sau T+N ngày tự chuyển COMPLETION_PENDING
    ///   Job 4 – Đối soát bù bút toán sổ cái (ReconcileMissingLedgerEntriesAsync)
    ///
    /// Thiết kế idempotent: mọi thay đổi trạng thái đều dùng FindOneAndUpdateAsync với bộ lọc
    /// dựa trên trạng thái hiện tại của document, đảm bảo an toàn khi chạy đa instance.
    /// </summary>
    public class BookingMaintenanceBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingMaintenanceBackgroundService> _logger;
        private readonly IConfiguration _configuration;

        public BookingMaintenanceBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<BookingMaintenanceBackgroundService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("BookingMaintenance:Enabled", true);
            if (!enabled)
            {
                _logger.LogInformation("[Maintenance] BookingMaintenanceBackgroundService bị tắt qua cấu hình (BookingMaintenance:Enabled = false). Không khởi động.");
                return;
            }

            var intervalMinutes = _configuration.GetValue<int>("BookingMaintenance:IntervalMinutes", 10);
            intervalMinutes = Math.Max(1, Math.Min(60, intervalMinutes)); // clamp 1–60 phút

            _logger.LogInformation("[Maintenance] BookingMaintenanceBackgroundService khởi động. Chu kỳ: {IntervalMinutes} phút.", intervalMinutes);

            // Chờ ngắn khi khởi động để ứng dụng hoàn tất kết nối DB
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var runStart = DateTime.UtcNow;
                try
                {
                    await RunAllJobsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Thoát sạch khi ứng dụng shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Maintenance] Lỗi nghiêm trọng trong vòng lặp background. Service tiếp tục sau {IntervalMinutes} phút.", intervalMinutes);
                }

                var elapsed = DateTime.UtcNow - runStart;
                var delay = TimeSpan.FromMinutes(intervalMinutes) - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }
            }

            _logger.LogInformation("[Maintenance] BookingMaintenanceBackgroundService đã dừng.");
        }

        private async Task RunAllJobsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PetSitterContext>();
            var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var emailService = scope.ServiceProvider.GetService<IEmailService>();

            var nowUtc = DateTime.UtcNow;
            _logger.LogInformation("[Maintenance] === Bắt đầu vòng bảo trì lúc {NowUtc:yyyy-MM-dd HH:mm:ss} UTC ===", nowUtc);

            // Job 1 & 2: Quét PENDING→EXPIRED và COMPLETION_PENDING→AUTO_COMPLETED (toàn bộ DB)
            await RunJob1And2Async(context, bookingService, notificationService, nowUtc, stoppingToken);

            // Job 3: Xử lý đơn ACCEPTED quá NgayKetThuc (nhắc + auto-timeout)
            await RunJob3Async(context, bookingService, notificationService, emailService, nowUtc, stoppingToken);

            // Job 4: Đối soát bù bút toán sổ cái
            await RunJob4Async(bookingService, nowUtc);

            _logger.LogInformation("[Maintenance] === Kết thúc vòng bảo trì lúc {EndUtc:yyyy-MM-dd HH:mm:ss} UTC ===", DateTime.UtcNow);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Job 1 & 2 – PENDING→EXPIRED  và  COMPLETION_PENDING→AUTO_COMPLETED
        // ─────────────────────────────────────────────────────────────────────
        internal async Task RunJob1And2Async(
            PetSitterContext context,
            IBookingService bookingService,
            INotificationService notificationService,
            DateTime nowUtc,
            CancellationToken ct)
        {
            try
            {
                var filterBuilder = Builders<Booking>.Filter;

                // --- Job 1: PENDING quá ExpiresAt → EXPIRED ---
                var graceMinutes = _configuration.GetValue<int>("Payments:PaymentGracePeriodMinutes", 5);

                // 1a. ACCEPTED + PENDING_PAYMENT + quá PaymentDeadline → EXPIRED
                var paymentExpiredCandidates = await context.Bookings.Find(
                    filterBuilder.Eq(b => b.TrangThai, BookingStatus.ACCEPTED) &
                    filterBuilder.Eq(b => b.PaymentStatus, PaymentStatus.PENDING_PAYMENT) &
                    filterBuilder.Lte(b => b.PaymentDeadline, nowUtc.AddMinutes(-graceMinutes))
                ).ToListAsync(ct);

                int expiredCount = 0;
                foreach (var candidate in paymentExpiredCandidates)
                {
                    if (ct.IsCancellationRequested) break;
                    var atomicFilter =
                        filterBuilder.Eq(b => b.BookingId, candidate.BookingId) &
                        filterBuilder.Eq(b => b.TrangThai, BookingStatus.ACCEPTED) &
                        filterBuilder.Eq(b => b.PaymentStatus, PaymentStatus.PENDING_PAYMENT) &
                        filterBuilder.Lte(b => b.PaymentDeadline, nowUtc.AddMinutes(-graceMinutes));

                    var update = Builders<Booking>.Update
                        .Set(b => b.TrangThai, BookingStatus.EXPIRED)
                        .Set(b => b.PaymentStatus, PaymentStatus.EXPIRED)
                        .Set(b => b.IsActive, false)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = BookingStatus.EXPIRED,
                            FromStatus = BookingStatus.ACCEPTED,
                            NguoiThucHien = "SYSTEM",
                            ActorUserId = null,
                            LyDo = "Quá hạn thanh toán 24 giờ (Background Worker)",
                            ThoiGian = nowUtc
                        });

                    var updated = await context.Bookings.FindOneAndUpdateAsync(atomicFilter, update,
                        new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After }, ct);

                    if (updated != null)
                    {
                        expiredCount++;
                        await SendExpiredNotificationsAsync(context, notificationService, updated, nowUtc, "do không được thanh toán trong 24 giờ.");
                    }
                }

                // 1b. PENDING quá ExpiresAt → EXPIRED
                var pendingExpiredCandidates = await context.Bookings.Find(
                    filterBuilder.Eq(b => b.TrangThai, BookingStatus.PENDING) &
                    filterBuilder.Lte(b => b.ExpiresAt, nowUtc)
                ).ToListAsync(ct);

                foreach (var candidate in pendingExpiredCandidates)
                {
                    if (ct.IsCancellationRequested) break;
                    var atomicFilter =
                        filterBuilder.Eq(b => b.BookingId, candidate.BookingId) &
                        filterBuilder.Eq(b => b.TrangThai, BookingStatus.PENDING) &
                        filterBuilder.Lte(b => b.ExpiresAt, nowUtc);

                    var update = Builders<Booking>.Update
                        .Set(b => b.TrangThai, BookingStatus.EXPIRED)
                        .Set(b => b.IsActive, false)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = BookingStatus.EXPIRED,
                            FromStatus = BookingStatus.PENDING,
                            NguoiThucHien = "SYSTEM",
                            ActorUserId = null,
                            LyDo = "Quá hạn 48 giờ không được chấp nhận (Background Worker)",
                            ThoiGian = nowUtc
                        });

                    var updated = await context.Bookings.FindOneAndUpdateAsync(atomicFilter, update,
                        new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After }, ct);

                    if (updated != null)
                    {
                        expiredCount++;
                        await SendExpiredNotificationsAsync(context, notificationService, updated, nowUtc, "do không được phản hồi trong 48 giờ.");
                    }
                }

                if (expiredCount > 0)
                    _logger.LogInformation("[Job1] Chuyển {Count} đơn sang EXPIRED.", expiredCount);
                else
                    _logger.LogDebug("[Job1] Không có đơn nào cần chuyển EXPIRED.");

                // --- Job 2: COMPLETION_PENDING quá CompletionDeadline → AUTO_COMPLETED ---
                var completionCandidates = await context.Bookings.Find(
                    filterBuilder.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING) &
                    filterBuilder.Lte(b => b.CompletionDeadline, nowUtc)
                ).ToListAsync(ct);

                int autoCompletedCount = 0;
                foreach (var candidate in completionCandidates)
                {
                    if (ct.IsCancellationRequested) break;
                    var atomicFilter =
                        filterBuilder.Eq(b => b.BookingId, candidate.BookingId) &
                        filterBuilder.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING) &
                        filterBuilder.Lte(b => b.CompletionDeadline, nowUtc);

                    var update = Builders<Booking>.Update
                        .Set(b => b.TrangThai, BookingStatus.COMPLETED)
                        .Set(b => b.IsActive, false)
                        .Set(b => b.AutoCompleted, true)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = "AUTO_COMPLETED",
                            FromStatus = BookingStatus.COMPLETION_PENDING,
                            NguoiThucHien = "SYSTEM",
                            ActorUserId = null,
                            LyDo = "Tự động hoàn tất do quá thời hạn xác nhận (Background Worker)",
                            ThoiGian = nowUtc
                        });

                    var updated = await context.Bookings.FindOneAndUpdateAsync(atomicFilter, update,
                        new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After }, ct);

                    if (updated != null)
                    {
                        autoCompletedCount++;
                        // Giải ngân tự động
                        try
                        {
                            await bookingService.ProcessLazyBookingUpdatesAsync(
                                ownerId: updated.OwnerId,
                                sitterId: null,
                                nowUtc: nowUtc,
                                forceReconcile: false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Job2] Lỗi giải ngân cho đơn {BookingId} sau AUTO_COMPLETED.", updated.BookingId);
                        }

                        await SendAutoCompletedNotificationsAsync(context, notificationService, updated, nowUtc);
                    }
                }

                if (autoCompletedCount > 0)
                    _logger.LogInformation("[Job2] Tự động hoàn tất {Count} đơn COMPLETION_PENDING.", autoCompletedCount);
                else
                    _logger.LogDebug("[Job2] Không có đơn COMPLETION_PENDING nào quá hạn.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job1&2] Lỗi trong Job 1 & 2.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Job 3 – ACCEPTED quá NgayKetThuc: nhắc Sitter + auto COMPLETION_PENDING
        // ─────────────────────────────────────────────────────────────────────
        internal async Task RunJob3Async(
            PetSitterContext context,
            IBookingService bookingService,
            INotificationService notificationService,
            IEmailService? emailService,
            DateTime nowUtc,
            CancellationToken ct)
        {
            try
            {
                // Đọc cấu hình
                var autoCompletionPendingAfterDays = _configuration.GetValue<int>("BookingMaintenance:AutoCompletionPendingAfterDays", 7);
                var rawReminderDays = _configuration.GetSection("BookingMaintenance:ReminderDays").Get<int[]>() ?? new[] { 0, 1, 2, 3 };

                // vnToday tính theo múi giờ Việt Nam (UTC+7) để so sánh với NgayKetThuc (lưu UTC date only)
                var vnToday = BookingService.GetVnToday();
                var autoTimeoutThreshold = vnToday.AddDays(-autoCompletionPendingAfterDays);

                // Lấy tất cả đơn ACCEPTED có NgayKetThuc < vnToday (đã qua ngày kết thúc, không bao gồm đơn chờ thanh toán)
                var overdueBookings = await context.Bookings.Find(b =>
                    b.TrangThai == BookingStatus.ACCEPTED &&
                    b.PaymentStatus != PaymentStatus.PENDING_PAYMENT &&
                    b.NgayKetThuc < vnToday
                ).ToListAsync(ct);

                if (!overdueBookings.Any())
                {
                    _logger.LogDebug("[Job3] Không có đơn ACCEPTED nào quá NgayKetThuc.");
                    return;
                }

                _logger.LogInformation("[Job3] Tìm thấy {Count} đơn ACCEPTED quá NgayKetThuc. Xử lý...", overdueBookings.Count);

                // Tra trước hồ sơ Sitter (bulk lookup)
                var sitterIds = overdueBookings.Select(b => b.SitterId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
                var sitterProfiles = await context.SitterProfiles
                    .Find(Builders<SitterProfile>.Filter.In(s => s.SitterId, sitterIds))
                    .ToListAsync(ct);
                var sitterUserMap = sitterProfiles
                    .Where(s => !string.IsNullOrEmpty(s.UserId))
                    .ToDictionary(s => s.SitterId, s => s.UserId!);

                int reminderCount = 0;
                int autoTimeoutCount = 0;
                int completionDeadlineDays = _configuration.GetValue<int>("BookingSettings:CompletionDeadlineDays", 3);

                foreach (var booking in overdueBookings)
                {
                    if (ct.IsCancellationRequested) break;

                    sitterUserMap.TryGetValue(booking.SitterId, out var sitterUserId);

                    // Tính số ngày đã qua kể từ NgayKetThuc (giá trị là ngày UTC+7 date only)
                    var daysSinceEnd = (int)(vnToday - booking.NgayKetThuc.Date).TotalDays;

                    // --- Auto-timeout sau N ngày: chuyển ACCEPTED → COMPLETION_PENDING ---
                    // Điều kiện: ngày đã qua > AutoCompletionPendingAfterDays (tức là NgayKetThuc < autoTimeoutThreshold)
                    if (booking.NgayKetThuc < autoTimeoutThreshold)
                    {
                        var atomicFilter =
                            Builders<Booking>.Filter.Eq(b => b.BookingId, booking.BookingId) &
                            Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.ACCEPTED) &
                            Builders<Booking>.Filter.Ne(b => b.PaymentStatus, PaymentStatus.PENDING_PAYMENT) &
                            Builders<Booking>.Filter.Lt(b => b.NgayKetThuc, autoTimeoutThreshold);

                        var completionDeadline = nowUtc.AddDays(completionDeadlineDays);
                        var update = Builders<Booking>.Update
                            .Set(b => b.TrangThai, BookingStatus.COMPLETION_PENDING)
                            .Set(b => b.IsActive, false) // Mở khóa IsActive của thú cưng để giải phóng đặt trùng lịch
                            .Set(b => b.SitterCompletedAt, nowUtc)
                            .Set(b => b.CompletionDeadline, completionDeadline)
                            .Push(b => b.History, new InquiryHistory
                            {
                                HanhDong = BookingStatus.COMPLETION_PENDING,
                                FromStatus = BookingStatus.ACCEPTED,
                                NguoiThucHien = "SYSTEM",
                                ActorUserId = null,
                                LyDo = $"Tự động chuyển sang COMPLETION_PENDING do Sitter không bấm Hoàn tất sau {autoCompletionPendingAfterDays} ngày kể từ NgayKetThuc (Background Worker)",
                                ThoiGian = nowUtc
                            });

                        var updated = await context.Bookings.FindOneAndUpdateAsync(atomicFilter, update,
                            new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After }, ct);

                        if (updated != null)
                        {
                            autoTimeoutCount++;
                            _logger.LogInformation("[Job3] Đơn {BookingId} ({MaDatLich}) tự động chuyển ACCEPTED→COMPLETION_PENDING sau {Days} ngày quá hạn.", booking.BookingId, booking.MaDatLich, daysSinceEnd);

                            // Thông báo cho Owner: yêu cầu xác nhận hoặc khiếu nại
                            if (!string.IsNullOrEmpty(updated.OwnerId))
                            {
                                var ownerDedupeKey = $"{updated.BookingId}:{NotificationType.BOOKING_COMPLETION_PENDING}:{updated.OwnerId}:AUTO_TIMEOUT";
                                await notificationService.CreateNotificationAsync(
                                    updated.OwnerId,
                                    NotificationType.BOOKING_COMPLETION_PENDING,
                                    updated.BookingId,
                                    "Đơn đặt lịch chờ bạn xác nhận hoàn tất",
                                    $"Đơn #{updated.MaDatLich} đã qua {daysSinceEnd} ngày kể từ ngày kết thúc. Hệ thống đã chuyển sang chờ xác nhận. Bạn có {completionDeadlineDays} ngày để xác nhận hoặc báo vấn đề trước khi hệ thống tự động hoàn tất.",
                                    ownerDedupeKey,
                                    targetRole: "OWNER"
                                );
                            }

                            // Thông báo cho Sitter
                            if (!string.IsNullOrEmpty(sitterUserId))
                            {
                                var sitterDedupeKey = $"{updated.BookingId}:{NotificationType.BOOKING_COMPLETION_PENDING}:{sitterUserId}:AUTO_TIMEOUT";
                                await notificationService.CreateNotificationAsync(
                                    sitterUserId,
                                    NotificationType.BOOKING_COMPLETION_PENDING,
                                    updated.BookingId,
                                    "Đơn đặt lịch đã được hệ thống xác nhận hoàn tất thay bạn",
                                    $"Đơn #{updated.MaDatLich} đã qua {daysSinceEnd} ngày kể từ ngày kết thúc mà không có phản hồi từ bạn. Hệ thống đã tự động chuyển sang giai đoạn chờ chủ nuôi xác nhận.",
                                    sitterDedupeKey,
                                    targetRole: "SITTER"
                                );
                            }
                        }
                        // Không cần gửi reminder nữa vì đã timeout
                        continue;
                    }

                    // --- Gửi reminder cho Sitter theo các mốc T+N (nếu chưa timeout) ---
                    if (string.IsNullOrEmpty(sitterUserId)) continue;

                    // Chỉ gửi reminder nếu daysSinceEnd nằm trong danh sách ReminderDays cấu hình
                    if (!rawReminderDays.Contains(daysSinceEnd)) continue;

                    // DedupeKey theo bookingId + ngày (vnDate) để mỗi mốc ngày chỉ gửi 1 lần
                    var vnDateStr = vnToday.ToString("yyyyMMdd");
                    var reminderDedupeKey = $"{booking.BookingId}:{NotificationType.REMINDER_OVERDUE_ACCEPTED}:{vnDateStr}:{sitterUserId}:BG";

                    var sent = await notificationService.CreateNotificationAsync(
                        sitterUserId,
                        NotificationType.REMINDER_OVERDUE_ACCEPTED,
                        booking.BookingId,
                        "Nhắc nhở: Vui lòng xác nhận hoàn tất dịch vụ",
                        $"Đơn #{booking.MaDatLich} đã qua {daysSinceEnd} ngày kể từ ngày kết thúc ({booking.NgayKetThuc:dd/MM/yyyy}). Vui lòng đăng nhập và bấm \"Hoàn tất\" để xác nhận dịch vụ. Nếu không có phản hồi trong {autoCompletionPendingAfterDays} ngày, hệ thống sẽ tự động xử lý.",
                        reminderDedupeKey,
                        targetRole: "SITTER"
                    );

                    if (sent)
                    {
                        reminderCount++;
                        _logger.LogInformation("[Job3] Gửi nhắc nhở T+{Days} cho Sitter {SitterUserId}, đơn {BookingId}.", daysSinceEnd, sitterUserId, booking.BookingId);

                        if (emailService != null)
                        {
                            try
                            {
                                var sitterUser = await context.Users.Find(u => u.UserId == sitterUserId).FirstOrDefaultAsync(ct);
                                if (sitterUser != null && !string.IsNullOrWhiteSpace(sitterUser.Email))
                                {
                                    await emailService.SendOverdueReminderAsync(
                                        sitterUser.Email,
                                        sitterUser.FullName ?? "Chăm sóc viên",
                                        booking,
                                        daysSinceEnd
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[Job3] Lỗi gửi email nhắc nhở quá hạn cho Sitter {SitterUserId}, đơn {BookingId}", sitterUserId, booking.BookingId);
                            }
                        }
                    }

                    // Đồng thời nhắc Owner từ T+1 trở đi để nắm tình hình
                    if (daysSinceEnd >= 1 && !string.IsNullOrEmpty(booking.OwnerId))
                    {
                        var ownerReminderKey = $"{booking.BookingId}:{NotificationType.REMINDER_OVERDUE_ACCEPTED}:{vnDateStr}:{booking.OwnerId}:OWNER_BG";
                        await notificationService.CreateNotificationAsync(
                            booking.OwnerId,
                            NotificationType.REMINDER_OVERDUE_ACCEPTED,
                            booking.BookingId,
                            "Thông báo: Sitter chưa xác nhận hoàn tất dịch vụ",
                            $"Đơn #{booking.MaDatLich} đã qua {daysSinceEnd} ngày kể từ ngày kết thúc. Nếu có vấn đề, bạn có thể liên hệ hỗ trợ hoặc chờ hệ thống tự động xử lý sau {autoCompletionPendingAfterDays - daysSinceEnd} ngày nữa.",
                            ownerReminderKey,
                            targetRole: "OWNER"
                        );
                    }
                }

                if (reminderCount > 0)
                    _logger.LogInformation("[Job3] Đã gửi {Count} nhắc nhở REMINDER_OVERDUE_ACCEPTED.", reminderCount);
                if (autoTimeoutCount > 0)
                    _logger.LogInformation("[Job3] Đã tự động timeout {Count} đơn ACCEPTED → COMPLETION_PENDING.", autoTimeoutCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job3] Lỗi trong Job 3 (ACCEPTED overdue).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Job 4 – Đối soát bù bút toán sổ cái
        // ─────────────────────────────────────────────────────────────────────
        private async Task RunJob4Async(IBookingService bookingService, DateTime nowUtc)
        {
            try
            {
                var lookbackDays = _configuration.GetValue<int>("BookingMaintenance:ReconcileLookbackDays", 7);
                var limit = _configuration.GetValue<int>("BookingMaintenance:ReconcileLimit", 100);

                // force = true để bỏ qua throttle 30 giây của lazy path; vòng lặp background đã tự throttle qua IntervalMinutes
                var repairedCount = await bookingService.ReconcileMissingLedgerEntriesAsync(
                    nowUtc, specificBookingId: null, lookbackDays: lookbackDays, limit: limit, force: true);

                if (repairedCount > 0)
                    _logger.LogInformation("[Job4] Đối soát bù thành công {Count} bút toán sổ cái.", repairedCount);
                else
                    _logger.LogDebug("[Job4] Không phát hiện bút toán sổ cái nào cần bù.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job4] Lỗi trong Job 4 (Reconcile).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper: Gửi thông báo EXPIRED cho cả 2 bên (idempotent qua DedupeKey)
        // ─────────────────────────────────────────────────────────────────────
        private async Task SendExpiredNotificationsAsync(
            PetSitterContext context,
            INotificationService notificationService,
            Booking booking,
            DateTime nowUtc,
            string reasonDesc)
        {
            try
            {
                if (!string.IsNullOrEmpty(booking.OwnerId))
                {
                    var dedupeKey = $"{booking.BookingId}:{NotificationType.BOOKING_EXPIRED}:{booking.OwnerId}";
                    await notificationService.CreateNotificationAsync(
                        booking.OwnerId,
                        NotificationType.BOOKING_EXPIRED,
                        booking.BookingId,
                        "Đơn đặt lịch đã hết hạn",
                        $"Đơn đặt lịch #{booking.MaDatLich} đã hết hạn {reasonDesc}",
                        dedupeKey,
                        targetRole: "OWNER"
                    );
                }

                if (!string.IsNullOrEmpty(booking.SitterId))
                {
                    var sitterProfile = await context.SitterProfiles
                        .Find(s => s.SitterId == booking.SitterId)
                        .FirstOrDefaultAsync();
                    if (sitterProfile?.UserId != null)
                    {
                        var dedupeKey = $"{booking.BookingId}:{NotificationType.BOOKING_EXPIRED}:{sitterProfile.UserId}";
                        await notificationService.CreateNotificationAsync(
                            sitterProfile.UserId,
                            NotificationType.BOOKING_EXPIRED,
                            booking.BookingId,
                            "Đơn đặt lịch đã hết hạn",
                            $"Đơn đặt lịch #{booking.MaDatLich} đã hết hạn {reasonDesc}",
                            dedupeKey,
                            targetRole: "SITTER"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi gửi thông báo EXPIRED cho đơn {BookingId}.", booking.BookingId);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper: Gửi thông báo AUTO_COMPLETED cho cả 2 bên
        // ─────────────────────────────────────────────────────────────────────
        private async Task SendAutoCompletedNotificationsAsync(
            PetSitterContext context,
            INotificationService notificationService,
            Booking booking,
            DateTime nowUtc)
        {
            try
            {
                if (!string.IsNullOrEmpty(booking.OwnerId))
                {
                    var dedupeKey = $"{booking.BookingId}:{NotificationType.BOOKING_COMPLETED}:{booking.OwnerId}";
                    await notificationService.CreateNotificationAsync(
                        booking.OwnerId,
                        NotificationType.BOOKING_COMPLETED,
                        booking.BookingId,
                        "Đơn đặt lịch đã tự động hoàn tất",
                        $"Đơn đặt lịch #{booking.MaDatLich} đã được hệ thống tự động hoàn tất do hết thời hạn xác nhận.",
                        dedupeKey,
                        targetRole: "OWNER"
                    );
                }

                if (!string.IsNullOrEmpty(booking.SitterId))
                {
                    var sitterProfile = await context.SitterProfiles
                        .Find(s => s.SitterId == booking.SitterId)
                        .FirstOrDefaultAsync();
                    if (sitterProfile?.UserId != null)
                    {
                        var dedupeKey = $"{booking.BookingId}:{NotificationType.BOOKING_COMPLETED}:{sitterProfile.UserId}";
                        await notificationService.CreateNotificationAsync(
                            sitterProfile.UserId,
                            NotificationType.BOOKING_COMPLETED,
                            booking.BookingId,
                            "Đơn đặt lịch đã tự động hoàn tất",
                            $"Đơn đặt lịch #{booking.MaDatLich} đã được hệ thống tự động hoàn tất do hết thời hạn xác nhận.",
                            dedupeKey,
                            targetRole: "SITTER"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi gửi thông báo AUTO_COMPLETED cho đơn {BookingId}.", booking.BookingId);
            }
        }
    }
}

