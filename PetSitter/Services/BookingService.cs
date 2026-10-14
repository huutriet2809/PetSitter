using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class BookingService : IBookingService
    {
        private readonly PetSitterContext _context;
        private readonly IConfiguration _configuration;
        private readonly INotificationService? _notificationService;
        private readonly ILogger<BookingService>? _logger;

        // Ghi chú kiến trúc: _lastReconcileTimes lưu trữ throttle đối soát trong bộ nhớ tiến trình (ConcurrentDictionary).
        // Giới hạn hệ thống: Khi restart tiến trình hoặc chạy nhiều instance phân tán, bộ đệm này bị reset hoặc độc lập.
        // Tuy nhiên, tính toàn vẹn dữ liệu được bảo đảm 100% nhờ truy vấn $in tra DedupeKey và unique index trên MongoDB.
        private static readonly ConcurrentDictionary<string, DateTime> _lastReconcileTimes = new();

        public BookingService(
            PetSitterContext context,
            IConfiguration configuration,
            INotificationService? notificationService = null,
            ILogger<BookingService>? logger = null)
        {
            _context = context;
            _configuration = configuration;
            _notificationService = notificationService;
            _logger = logger;
        }

        public static DateTime GetVnToday()
        {
            var vnNow = DateTime.UtcNow.AddHours(7);
            return DateTime.SpecifyKind(vnNow.Date, DateTimeKind.Utc);
        }

        // GHI CHÚ BẢO MẬT VÀ TOÀN VẸN TRẠNG THÁI:
        // Luồng trạng thái của đơn đặt lịch (Booking) là một đồ thị có hướng không chu trình (DAG):
        // PENDING -> ACCEPTED -> COMPLETION_PENDING -> COMPLETED (đóng đơn)
        //                                            -> DISPUTED -> COMPLETED hoặc CANCELLED (đóng đơn)
        // Hoặc từ PENDING -> REJECTED / CANCELLED / EXPIRED; ACCEPTED -> CANCELLED.
        // Không có bất kỳ trạng thái nào có thể bị lặp lại hoặc quay lui trên cùng một BookingId.
        // Do đó, định dạng DedupeKey "{BookingId}:{Type}:{RecipientUserId}" là duy nhất tuyệt đối cho mỗi sự kiện thông báo.
        public async Task ProcessLazyBookingUpdatesAsync(string? ownerId = null, string? sitterId = null, DateTime? nowUtc = null, bool forceReconcile = false)
        {
            if (string.IsNullOrEmpty(ownerId) && string.IsNullOrEmpty(sitterId))
                return;

            var effectiveNowUtc = nowUtc ?? DateTime.UtcNow;
            var filterBuilder = Builders<Booking>.Filter;

            var expiredWonBookings = new List<Booking>();
            var autoCompletedWonBookings = new List<Booking>();

            // 1. Quét tìm các ứng viên PENDING quá hạn 48h -> EXPIRED
            var pendingCandidateFilter = filterBuilder.Eq(b => b.TrangThai, BookingStatus.PENDING)
                                       & filterBuilder.Lte(b => b.ExpiresAt, effectiveNowUtc);

            if (!string.IsNullOrEmpty(ownerId))
                pendingCandidateFilter &= filterBuilder.Eq(b => b.OwnerId, ownerId);
            if (!string.IsNullOrEmpty(sitterId))
                pendingCandidateFilter &= filterBuilder.Eq(b => b.SitterId, sitterId);

            var pendingCandidates = await _context.Bookings.Find(pendingCandidateFilter).ToListAsync();

            foreach (var candidate in pendingCandidates)
            {
                var atomicFilter = filterBuilder.Eq(b => b.BookingId, candidate.BookingId)
                                 & filterBuilder.Eq(b => b.TrangThai, BookingStatus.PENDING)
                                 & filterBuilder.Lte(b => b.ExpiresAt, effectiveNowUtc);

                var pendingUpdate = Builders<Booking>.Update
                    .Set(b => b.TrangThai, BookingStatus.EXPIRED)
                    .Set(b => b.IsActive, false)
                    .Push(b => b.History, new InquiryHistory
                    {
                        HanhDong = BookingStatus.EXPIRED,
                        FromStatus = BookingStatus.PENDING,
                        NguoiThucHien = "SYSTEM",
                        ActorUserId = null,
                        LyDo = "Quá hạn 48 giờ không được chấp nhận",
                        ThoiGian = effectiveNowUtc
                    });

                var updated = await _context.Bookings.FindOneAndUpdateAsync(
                    atomicFilter,
                    pendingUpdate,
                    new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After }
                );

                if (updated != null)
                {
                    expiredWonBookings.Add(updated);
                }
            }

            // 2. Quét tìm các ứng viên COMPLETION_PENDING quá hạn CompletionDeadline -> COMPLETED
            var completionCandidateFilter = filterBuilder.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING)
                                          & filterBuilder.Lte(b => b.CompletionDeadline, effectiveNowUtc);

            if (!string.IsNullOrEmpty(ownerId))
                completionCandidateFilter &= filterBuilder.Eq(b => b.OwnerId, ownerId);
            if (!string.IsNullOrEmpty(sitterId))
                completionCandidateFilter &= filterBuilder.Eq(b => b.SitterId, sitterId);

            var completionCandidates = await _context.Bookings.Find(completionCandidateFilter).ToListAsync();

            foreach (var candidate in completionCandidates)
            {
                var atomicFilter = filterBuilder.Eq(b => b.BookingId, candidate.BookingId)
                                 & filterBuilder.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING)
                                 & filterBuilder.Lte(b => b.CompletionDeadline, effectiveNowUtc);

                var completionUpdate = Builders<Booking>.Update
                    .Set(b => b.TrangThai, BookingStatus.COMPLETED)
                    .Set(b => b.IsActive, false)
                    .Set(b => b.AutoCompleted, true)
                    .Push(b => b.History, new InquiryHistory
                    {
                        HanhDong = "AUTO_COMPLETED",
                        FromStatus = BookingStatus.COMPLETION_PENDING,
                        NguoiThucHien = "SYSTEM",
                        ActorUserId = null,
                        LyDo = "Tự động hoàn tất do quá thời hạn xác nhận",
                        ThoiGian = effectiveNowUtc
                    });

                var updated = await _context.Bookings.FindOneAndUpdateAsync(
                    atomicFilter,
                    completionUpdate,
                    new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After }
                );

                if (updated != null)
                {
                    autoCompletedWonBookings.Add(updated);
                }
            }

            // 3. Đối soát (Reconciliation): Tìm các đơn EXPIRED / AUTO_COMPLETED do SYSTEM đổi trong 7 ngày gần đây của người đọc
            var minScanMinutes = _configuration.GetValue<int>("BookingReminderSettings:MinScanIntervalMinutes", 5);
            var userKey = ownerId ?? sitterId ?? "system";
            var shouldReconcile = false;

            if (forceReconcile || !_lastReconcileTimes.TryGetValue(userKey, out var lastReconcile) || effectiveNowUtc - lastReconcile >= TimeSpan.FromMinutes(minScanMinutes))
            {
                _lastReconcileTimes[userKey] = effectiveNowUtc;
                shouldReconcile = true;
            }

            var recentSystemBookings = new List<Booking>();
            if (shouldReconcile)
            {
                var sevenDaysAgo = effectiveNowUtc.AddDays(-7);
                var reconcileFilter = (filterBuilder.Eq(b => b.TrangThai, BookingStatus.EXPIRED) & filterBuilder.Gte(b => b.ExpiresAt, sevenDaysAgo))
                                    | (filterBuilder.Eq(b => b.TrangThai, BookingStatus.COMPLETED) & filterBuilder.Eq(b => b.AutoCompleted, true) & filterBuilder.Gte(b => b.CompletionDeadline, sevenDaysAgo));

                if (!string.IsNullOrEmpty(ownerId))
                    reconcileFilter &= filterBuilder.Eq(b => b.OwnerId, ownerId);
                if (!string.IsNullOrEmpty(sitterId))
                    reconcileFilter &= filterBuilder.Eq(b => b.SitterId, sitterId);

                recentSystemBookings = await _context.Bookings.Find(reconcileFilter).ToListAsync();
            }

            var allExpiredToNotify = expiredWonBookings.Concat(recentSystemBookings.Where(b => b.TrangThai == BookingStatus.EXPIRED)).DistinctBy(b => b.BookingId).ToList();
            var allAutoCompletedToNotify = autoCompletedWonBookings.Concat(recentSystemBookings.Where(b => b.TrangThai == BookingStatus.COMPLETED && b.AutoCompleted == true)).DistinctBy(b => b.BookingId).ToList();

            if (_notificationService != null && (allExpiredToNotify.Any() || allAutoCompletedToNotify.Any()))
            {
                try
                {
                    // Gom SitterId của N đơn và tra một lượt (Bulk lookup SitterProfile.UserId)
                    var distinctSitterIds = allExpiredToNotify.Select(b => b.SitterId)
                        .Concat(allAutoCompletedToNotify.Select(b => b.SitterId))
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .ToList();

                    var sitterProfiles = await _context.SitterProfiles
                        .Find(s => distinctSitterIds.Contains(s.SitterId))
                        .ToListAsync();

                    var sitterUserMap = sitterProfiles
                        .Where(s => !string.IsNullOrEmpty(s.UserId))
                        .ToDictionary(s => s.SitterId, s => s.UserId);

                    // Chuẩn bị danh sách ứng viên thông báo (Candidate notifications)
                    var candidates = new List<(string UserId, string Type, string BookingId, string Title, string Message, string DedupeKey, string TargetRole)>();

                    // Cho các đơn EXPIRED (cả 2 bên)
                    foreach (var b in allExpiredToNotify)
                    {
                        if (!string.IsNullOrEmpty(b.OwnerId))
                        {
                            var ownerDedupeKey = $"{b.BookingId}:{NotificationType.BOOKING_EXPIRED}:{b.OwnerId}";
                            candidates.Add((
                                b.OwnerId,
                                NotificationType.BOOKING_EXPIRED,
                                b.BookingId,
                                "Đơn đặt lịch đã hết hạn",
                                $"Đơn đặt lịch #{b.MaDatLich} đã hết hạn do không được phản hồi trong 48 giờ.",
                                ownerDedupeKey,
                                "OWNER"
                            ));
                        }

                        if (sitterUserMap.TryGetValue(b.SitterId, out var sitterUserId))
                        {
                            var sitterDedupeKey = $"{b.BookingId}:{NotificationType.BOOKING_EXPIRED}:{sitterUserId}";
                            candidates.Add((
                                sitterUserId,
                                NotificationType.BOOKING_EXPIRED,
                                b.BookingId,
                                "Đơn đặt lịch đã hết hạn",
                                $"Đơn đặt lịch #{b.MaDatLich} đã hết hạn do không được phản hồi trong 48 giờ.",
                                sitterDedupeKey,
                                "SITTER"
                            ));
                        }
                    }

                    // Cho các đơn AUTO_COMPLETED (cả 2 bên)
                    foreach (var b in allAutoCompletedToNotify)
                    {
                        if (!string.IsNullOrEmpty(b.OwnerId))
                        {
                            var ownerDedupeKey = $"{b.BookingId}:{NotificationType.BOOKING_COMPLETED}:{b.OwnerId}";
                            candidates.Add((
                                b.OwnerId,
                                NotificationType.BOOKING_COMPLETED,
                                b.BookingId,
                                "Đơn đặt lịch đã tự động hoàn tất",
                                $"Đơn đặt lịch #{b.MaDatLich} đã được hệ thống tự động hoàn tất do hết thời hạn xác nhận.",
                                ownerDedupeKey,
                                "OWNER"
                            ));
                        }

                        if (sitterUserMap.TryGetValue(b.SitterId, out var sitterUserId))
                        {
                            var sitterDedupeKey = $"{b.BookingId}:{NotificationType.BOOKING_COMPLETED}:{sitterUserId}";
                            candidates.Add((
                                sitterUserId,
                                NotificationType.BOOKING_COMPLETED,
                                b.BookingId,
                                "Đơn đặt lịch đã tự động hoàn tất",
                                $"Đơn đặt lịch #{b.MaDatLich} đã được hệ thống tự động hoàn tất do hết thời hạn xác nhận.",
                                sitterDedupeKey,
                                "SITTER"
                            ));
                        }
                    }

                    // Tra trước các DedupeKey đã tồn tại bằng một truy vấn $in và chỉ insert phần thiếu
                    var candidateKeys = candidates.Select(c => c.DedupeKey).Distinct().ToList();
                    if (candidateKeys.Any())
                    {
                        var existingKeys = (await _context.Notifications
                            .Find(Builders<Notification>.Filter.In(n => n.DedupeKey, candidateKeys))
                            .Project(n => n.DedupeKey)
                            .ToListAsync())
                            .ToHashSet();

                        var missingCandidates = candidates.Where(c => !existingKeys.Contains(c.DedupeKey)).ToList();

                        foreach (var c in missingCandidates)
                        {
                            await _notificationService.CreateNotificationAsync(
                                c.UserId,
                                c.Type,
                                c.BookingId,
                                c.Title,
                                c.Message,
                                c.DedupeKey,
                                c.TargetRole
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Lỗi khi xử lý thông báo lazy / đối soát.");
                }
            }
        }

        public Task ExpireOverduePendingBookingsAsync(string? ownerId = null, string? sitterId = null)
            => ProcessLazyBookingUpdatesAsync(ownerId, sitterId);

        public async Task<BookingDetailDto> CreateBookingAsync(string currentUserId, CreateBookingDto dto)
        {
            // 0. Chạy hết hạn lười cho các đơn PENDING và COMPLETION_PENDING của Owner và Sitter
            await ProcessLazyBookingUpdatesAsync(ownerId: currentUserId, sitterId: null);
            await ProcessLazyBookingUpdatesAsync(ownerId: null, sitterId: dto.SitterId);

            // 1. Parse và validate khoảng ngày
            if (!DateTime.TryParseExact(dto.NgayBatDau, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rawStart))
            {
                throw new BadRequestException("Ngày bắt đầu không đúng định dạng yyyy-MM-dd.");
            }
            var ngayBatDau = DateTime.SpecifyKind(rawStart.Date, DateTimeKind.Utc);

            if (!DateTime.TryParseExact(dto.NgayKetThuc, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rawEnd))
            {
                throw new BadRequestException("Ngày kết thúc không đúng định dạng yyyy-MM-dd.");
            }
            var ngayKetThuc = DateTime.SpecifyKind(rawEnd.Date, DateTimeKind.Utc);

            var vnToday = GetVnToday();
            if (ngayBatDau < vnToday)
                throw new BadRequestException("Ngày bắt đầu không được ở quá khứ.");

            if (ngayKetThuc < ngayBatDau)
                throw new BadRequestException("Ngày kết thúc không được nhỏ hơn ngày bắt đầu.");

            var soNgay = (ngayKetThuc - ngayBatDau).Days + 1;
            if (soNgay > 30)
                throw new BadRequestException("Khoảng thời gian đặt lịch tối đa là 30 ngày.");

            if (!string.IsNullOrEmpty(dto.PetId))
            {
                var pet = await _context.Pets.Find(p => p.PetId == dto.PetId).FirstOrDefaultAsync();
                if (pet == null || pet.OwnerId != currentUserId)
                {
                    throw new NotFoundException("Không tìm thấy thông tin thú cưng.");
                }
            }

            // 2. Validate Sitter
            var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == dto.SitterId).FirstOrDefaultAsync();
            if (sitterProfile == null)
                throw new NotFoundException("Sitter không tồn tại.");
            if (!sitterProfile.IsVerified || sitterProfile.Status != "Approved")
                throw new BadRequestException("Sitter chưa được duyệt.");

            // Chặn Owner tự đặt cho hồ sơ Sitter của chính mình
            if (sitterProfile.UserId == currentUserId)
                throw new BadRequestException("Bạn không thể tự đặt dịch vụ cho chính mình.");

            // Ghi chú: Giới hạn 5 PENDING được kiểm tra ở mức service, không tuyệt đối khi có nhiều request đồng thời (race condition).
            var pendingCount = await _context.Bookings.CountDocumentsAsync(b => b.OwnerId == currentUserId && b.TrangThai == BookingStatus.PENDING);
            if (pendingCount >= 5)
                throw new BadRequestException("Bạn đang có 5 yêu cầu đang chờ phản hồi. Vui lòng đợi phản hồi trước khi gửi thêm.");

            // Kiểm tra chồng lấn ở service: cùng OwnerId + SitterId, IsActive == true, NgayBatDau <= newEnd && NgayKetThuc >= newStart
            var hasActiveOverlap = await _context.Bookings.Find(b =>
                b.OwnerId == currentUserId &&
                b.SitterId == dto.SitterId &&
                b.IsActive == true &&
                b.NgayBatDau <= ngayKetThuc &&
                b.NgayKetThuc >= ngayBatDau
            ).AnyAsync();

            if (hasActiveOverlap)
            {
                throw new ConflictException("Bạn đã có một yêu cầu đang chờ hoặc đã được chấp nhận trùng khoảng thời gian với người chăm sóc này.");
            }

            // 3. Validate SitterAvailabilities
            var availabilities = await _context.SitterAvailabilities
                .Find(a => a.SitterId == dto.SitterId && a.IsAvailable && a.AvailableDate >= ngayBatDau && a.AvailableDate <= ngayKetThuc)
                .ToListAsync();

            if (availabilities.Count < soNgay)
                throw new ConflictException("Sitter không có lịch rảnh trong toàn bộ khoảng thời gian này.");

            // 4. Calculate SitterPrice (base)
            var allSitterServices = await _context.SitterServices.Find(ss => ss.SitterId == dto.SitterId).ToListAsync();
            var giaMotNgay = allSitterServices.Any() ? allSitterServices.Min(ss => ss.Price) : 100000;
            var giaSitter = giaMotNgay * soNgay;

            // 5. Calculate Addons
            decimal tongAddons = 0;
            var bookingAddons = new List<BookingAddon>();
            if (dto.Addons != null && dto.Addons.Any())
            {
                var services = await _context.Services.Find(s => dto.Addons.Contains(s.ServiceId)).ToListAsync();
                var sitterServicesForAddons = allSitterServices.Where(ss => dto.Addons.Contains(ss.ServiceId)).ToList();
                foreach(var service in services)
                {
                    var ss = sitterServicesForAddons.FirstOrDefault(x => x.ServiceId == service.ServiceId);
                    if (ss == null) continue;

                    var isDailyAddon = (service.ServiceId == "anh-ngay" || service.ServiceId == "giu-thuoc" || service.ServiceName.Contains("hằng ngày") || service.ServiceName.Contains("thuốc"));
                    
                    var donGia = ss.Price;
                    var thanhTien = isDailyAddon ? donGia * soNgay : donGia;
                    tongAddons += thanhTien;

                    bookingAddons.Add(new BookingAddon {
                        ServiceId = service.ServiceId,
                        Ten = service.ServiceName,
                        DonGia = donGia,
                        IsDaily = isDailyAddon,
                        ThanhTien = thanhTien
                    });
                }
            }

            // 6. Calculate Totals (Chi phí ước tính)
            var tongTien = giaSitter + tongAddons;
            var bookingId = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
            var maDatLich = "PS" + DateTime.UtcNow.ToString("yyMMdd") + new Random().Next(1000, 9999).ToString();

            var nowUtc = DateTime.UtcNow;
            var booking = new Booking
            {
                BookingId = bookingId,
                MaDatLich = maDatLich,
                OwnerId = currentUserId,
                SitterId = dto.SitterId,
                PetId = dto.PetId,
                NgayBatDau = ngayBatDau,
                NgayKetThuc = ngayKetThuc,
                SoNgay = soNgay,
                GhiChu = dto.GhiChu,
                Pet = new BookingPet
                {
                    TenPet = dto.TenPet,
                    LoaiPet = dto.LoaiPet,
                    KichCo = dto.KichCo
                },
                Addons = bookingAddons,
                GiaSitter = giaSitter,
                TongAddons = tongAddons,
                TongTien = tongTien,
                TrangThai = BookingStatus.PENDING,
                IsActive = true,
                ExpiresAt = nowUtc.AddHours(48),
                History = new List<InquiryHistory>
                {
                    new InquiryHistory
                    {
                        HanhDong = BookingStatus.PENDING,
                        FromStatus = null,
                        NguoiThucHien = "OWNER",
                        ActorUserId = currentUserId,
                        LyDo = null,
                        ThoiGian = nowUtc
                    }
                },
                CreatedAt = nowUtc
            };

            try
            {
                await _context.Bookings.InsertOneAsync(booking);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConflictException("Bạn đã có một yêu cầu đang chờ hoặc đã được chấp nhận cho người chăm sóc và khoảng thời gian này.");
            }

            if (_notificationService != null)
            {
                try
                {
                    if (sitterProfile != null && !string.IsNullOrEmpty(sitterProfile.UserId))
                    {
                        var ownerUser = await _context.Users.Find(u => u.UserId == currentUserId).FirstOrDefaultAsync();
                        var dedupeKey = $"{booking.BookingId}:{NotificationType.BOOKING_NEW}:{sitterProfile.UserId}";
                        await _notificationService.CreateNotificationAsync(
                            sitterProfile.UserId,
                            NotificationType.BOOKING_NEW,
                            booking.BookingId,
                            "Có yêu cầu đặt lịch mới",
                            $"Bạn có yêu cầu đặt lịch mới #{booking.MaDatLich} từ {ownerUser?.FullName ?? "chủ nuôi"}.",
                            dedupeKey,
                            targetRole: "SITTER"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_NEW cho đơn {BookingId}", booking.BookingId);
                }
            }

            return await MapToDtoAsync(booking);
        }

        public async Task<BookingDetailDto> GetBookingByIdAsync(string currentUserId, string bookingId)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null)
                throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

            bool isOwner = booking.OwnerId == currentUserId;
            bool isSitter = false;
            if (!isOwner)
            {
                var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == currentUserId).FirstOrDefaultAsync();
                if (sitterProfile != null && sitterProfile.SitterId == booking.SitterId)
                {
                    isSitter = true;
                }
            }

            if (!isOwner && !isSitter)
            {
                throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");
            }

            var nowUtc = DateTime.UtcNow;
            if ((booking.TrangThai == BookingStatus.PENDING && booking.ExpiresAt <= nowUtc)
             || (booking.TrangThai == BookingStatus.COMPLETION_PENDING && booking.CompletionDeadline <= nowUtc))
            {
                if (isOwner)
                    await ProcessLazyBookingUpdatesAsync(ownerId: currentUserId, sitterId: null, nowUtc: nowUtc);
                else
                    await ProcessLazyBookingUpdatesAsync(ownerId: null, sitterId: booking.SitterId, nowUtc: nowUtc);

                booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync() ?? booking;
            }

            return await MapToDtoAsync(booking, currentUserId);
        }

        public async Task<List<BookingDetailDto>> GetMyBookingsAsync(string currentUserId)
        {
            await ProcessLazyBookingUpdatesAsync(ownerId: currentUserId, sitterId: null);

            var bookings = await _context.Bookings.Find(b => b.OwnerId == currentUserId).SortByDescending(b => b.CreatedAt).ToListAsync();
            var dtos = new List<BookingDetailDto>();
            foreach(var booking in bookings)
            {
                dtos.Add(await MapToDtoAsync(booking, currentUserId, role: "OWNER"));
            }
            return dtos;
        }

        public async Task CancelBookingAsync(string currentUserId, string bookingId, string? lyDo = null, string? expectedStatus = null)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null) throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

            bool isOwner = booking.OwnerId == currentUserId;
            bool isSitter = false;

            if (!isOwner)
            {
                var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == currentUserId).FirstOrDefaultAsync();
                if (sitterProfile != null && sitterProfile.SitterId == booking.SitterId)
                {
                    isSitter = true;
                }
            }

            if (!isOwner && !isSitter)
            {
                throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");
            }

            if (!string.IsNullOrEmpty(expectedStatus) && booking.TrangThai != expectedStatus)
            {
                throw new ConflictException($"Trạng thái đơn đã thay đổi (hiện tại: {booking.TrangThai}, mong đợi: {expectedStatus}).");
            }

            var vnToday = GetVnToday();
            var nowUtc = DateTime.UtcNow;

            if (isSitter)
            {
                if (booking.TrangThai != BookingStatus.ACCEPTED)
                    throw new BadRequestException("Người chăm sóc chỉ có thể hủy đơn đã được chấp nhận.");

                if (string.IsNullOrWhiteSpace(lyDo))
                    throw new BadRequestException("Lý do hủy đơn là bắt buộc đối với người chăm sóc.");

                if (lyDo.Trim().Length > 500)
                    throw new BadRequestException("Lý do hủy đơn tối đa 500 ký tự.");

                if (booking.NgayBatDau <= vnToday)
                    throw new BadRequestException("Không thể hủy đơn khi đã đến hoặc qua ngày bắt đầu.");

                var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                           & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.ACCEPTED)
                           & Builders<Booking>.Filter.Gt(b => b.NgayBatDau, vnToday);

                var update = Builders<Booking>.Update
                    .Set(b => b.TrangThai, BookingStatus.CANCELLED)
                    .Set(b => b.IsActive, false)
                    .Push(b => b.History, new InquiryHistory
                    {
                        HanhDong = BookingStatus.CANCELLED,
                        FromStatus = BookingStatus.ACCEPTED,
                        NguoiThucHien = "SITTER",
                        ActorUserId = currentUserId,
                        LyDo = lyDo.Trim(),
                        ThoiGian = nowUtc
                    });

                var updated = await _context.Bookings.FindOneAndUpdateAsync(filter, update);
                if (updated == null)
                    throw new ConflictException("Không thể hủy đơn. Trạng thái hoặc điều kiện thời gian của đơn đã thay đổi.");

                if (_notificationService != null && !string.IsNullOrEmpty(booking.OwnerId))
                {
                    try
                    {
                        var dedupeKey = $"{bookingId}:{NotificationType.BOOKING_CANCELLED}:{booking.OwnerId}";
                        await _notificationService.CreateNotificationAsync(
                            booking.OwnerId,
                            NotificationType.BOOKING_CANCELLED,
                            bookingId,
                            "Đơn đặt lịch đã bị hủy",
                            $"Người chăm sóc đã hủy đơn đặt lịch #{booking.MaDatLich}.",
                            dedupeKey,
                            targetRole: "OWNER"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Lỗi khi gửi thông báo hủy đơn cho chủ nuôi {OwnerId}", booking.OwnerId);
                    }
                }
            }
            else // isOwner
            {
                if (lyDo != null && lyDo.Trim().Length > 500)
                    throw new BadRequestException("Lý do hủy đơn tối đa 500 ký tự.");

                var trimmedReason = string.IsNullOrWhiteSpace(lyDo) ? null : lyDo.Trim();

                if (booking.TrangThai == BookingStatus.PENDING)
                {
                    var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                               & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.PENDING);

                    var update = Builders<Booking>.Update
                        .Set(b => b.TrangThai, BookingStatus.CANCELLED)
                        .Set(b => b.IsActive, false)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = BookingStatus.CANCELLED,
                            FromStatus = BookingStatus.PENDING,
                            NguoiThucHien = "OWNER",
                            ActorUserId = currentUserId,
                            LyDo = trimmedReason,
                            ThoiGian = nowUtc
                        });

                    var updated = await _context.Bookings.FindOneAndUpdateAsync(filter, update);
                    if (updated == null)
                        throw new ConflictException("Không thể hủy đơn. Trạng thái đơn đã thay đổi.");
                }
                else if (booking.TrangThai == BookingStatus.ACCEPTED)
                {
                    if (booking.NgayBatDau <= vnToday)
                        throw new BadRequestException("Không thể hủy đơn đã xác nhận khi đã đến hoặc qua ngày bắt đầu.");

                    var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                               & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.ACCEPTED)
                               & Builders<Booking>.Filter.Gt(b => b.NgayBatDau, vnToday);

                    var update = Builders<Booking>.Update
                        .Set(b => b.TrangThai, BookingStatus.CANCELLED)
                        .Set(b => b.IsActive, false)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = BookingStatus.CANCELLED,
                            FromStatus = BookingStatus.ACCEPTED,
                            NguoiThucHien = "OWNER",
                            ActorUserId = currentUserId,
                            LyDo = trimmedReason,
                            ThoiGian = nowUtc
                        });

                    var updated = await _context.Bookings.FindOneAndUpdateAsync(filter, update);
                    if (updated == null)
                        throw new ConflictException("Không thể hủy đơn. Trạng thái hoặc điều kiện thời gian của đơn đã thay đổi.");
                }
                else
                {
                    throw new BadRequestException("Không thể hủy đặt lịch ở trạng thái này.");
                }

                if (_notificationService != null)
                {
                    try
                    {
                        var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == booking.SitterId).FirstOrDefaultAsync();
                        if (sitterProfile != null && !string.IsNullOrEmpty(sitterProfile.UserId))
                        {
                            var dedupeKey = $"{bookingId}:{NotificationType.BOOKING_CANCELLED}:{sitterProfile.UserId}";
                            await _notificationService.CreateNotificationAsync(
                                sitterProfile.UserId,
                                NotificationType.BOOKING_CANCELLED,
                                bookingId,
                                "Đơn đặt lịch đã bị hủy",
                                $"Chủ nuôi đã hủy đơn đặt lịch #{booking.MaDatLich}.",
                                dedupeKey,
                                targetRole: "SITTER"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Lỗi khi gửi thông báo hủy đơn cho Sitter {SitterId}", booking.SitterId);
                    }
                }
            }
        }

        public async Task<ReviewDto> AddReviewAsync(string currentUserId, string bookingId, CreateReviewDto dto)
        {
            // Gọi hàm lazy trước khi kiểm tra trạng thái
            await ProcessLazyBookingUpdatesAsync(ownerId: currentUserId, sitterId: null);

            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null || booking.OwnerId != currentUserId)
                throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

            if (booking.TrangThai != BookingStatus.COMPLETED)
                throw new BadRequestException("Chỉ có thể đánh giá sau khi hoàn thành dịch vụ.");

            var existingReview = await _context.Reviews.Find(r => r.BookingId == bookingId).FirstOrDefaultAsync();
            if (existingReview != null)
                throw new BadRequestException("Bạn đã đánh giá cho lần đặt lịch này rồi.");

            var review = new Review
            {
                ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = bookingId,
                OwnerId = currentUserId,
                SitterId = booking.SitterId,
                Rating = dto.Rating,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Reviews.InsertOneAsync(review);

            // Update Sitter Rating & TotalReviews
            var allReviews = await _context.Reviews.Find(r => r.SitterId == booking.SitterId).ToListAsync();
            var totalReviews = allReviews.Count;
            var averageRating = (decimal)allReviews.Average(r => r.Rating);

            var sitterFilter = Builders<SitterProfile>.Filter.Eq(s => s.SitterId, booking.SitterId);
            var sitterUpdate = Builders<SitterProfile>.Update
                .Set(s => s.TotalReviews, totalReviews)
                .Set(s => s.Rating, Math.Round(averageRating, 1));
            await _context.SitterProfiles.UpdateOneAsync(sitterFilter, sitterUpdate);

            var user = await _context.Users.Find(u => u.UserId == currentUserId).FirstOrDefaultAsync();

            return new ReviewDto
            {
                ReviewId = review.ReviewId,
                NguoiDung = user?.FullName ?? "Unknown",
                Avatar = "",
                Rating = review.Rating,
                Comment = review.Comment ?? "",
                Ngay = review.CreatedAt
            };
        }

        public async Task<BookingDetailDto> SitterCompleteBookingAsync(string currentUserId, string bookingId)
        {
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == currentUserId).FirstOrDefaultAsync();
            if (sitterProfile == null)
                throw new NotFoundException("Không tìm thấy hồ sơ Sitter.");

            var vnToday = GetVnToday();
            var nowUtc = DateTime.UtcNow;
            var deadlineDays = _configuration.GetValue<int>("BookingSettings:CompletionDeadlineDays", 3);
            if (deadlineDays <= 0) deadlineDays = 3;
            var completionDeadline = nowUtc.AddDays(deadlineDays);

            // Chỉ hoàn tất khi TrangThai == ACCEPTED và NgayKetThuc < vnToday (qua ngày kết thúc)
            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                       & Builders<Booking>.Filter.Eq(b => b.SitterId, sitterProfile.SitterId)
                       & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.ACCEPTED)
                       & Builders<Booking>.Filter.Lt(b => b.NgayKetThuc, vnToday);

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, BookingStatus.COMPLETION_PENDING)
                .Set(b => b.IsActive, true)
                .Set(b => b.SitterCompletedAt, nowUtc)
                .Set(b => b.CompletionDeadline, completionDeadline)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = "SITTER_COMPLETED",
                    FromStatus = BookingStatus.ACCEPTED,
                    NguoiThucHien = "SITTER",
                    ActorUserId = currentUserId,
                    LyDo = null,
                    ThoiGian = nowUtc
                });

            var result = await _context.Bookings.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After });

            if (result == null)
            {
                var existing = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
                if (existing == null || existing.SitterId != sitterProfile.SitterId)
                    throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

                if (existing.TrangThai == BookingStatus.ACCEPTED && existing.NgayKetThuc >= vnToday)
                    throw new BadRequestException("Chưa thể hoàn thành booking do chưa qua ngày kết thúc.");

                throw new ConflictException("Không thể đánh dấu hoàn tất. Trạng thái đơn đã thay đổi.");
            }

            if (_notificationService != null && !string.IsNullOrEmpty(result.OwnerId))
            {
                try
                {
                    var dedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_COMPLETION_PENDING}:{result.OwnerId}";
                    var deadlineStr = result.CompletionDeadline.HasValue ? result.CompletionDeadline.Value.AddHours(7).ToString("dd/MM/yyyy HH:mm") : "";
                    await _notificationService.CreateNotificationAsync(
                        result.OwnerId,
                        NotificationType.BOOKING_COMPLETION_PENDING,
                        result.BookingId,
                        "Dịch vụ đã hoàn tất - Chờ xác nhận",
                        $"Người chăm sóc đã đánh dấu hoàn tất dịch vụ #{result.MaDatLich}. Vui lòng xác nhận trước {deadlineStr}.",
                        dedupeKey,
                        targetRole: "OWNER"
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_COMPLETION_PENDING cho chủ nuôi {OwnerId}", result.OwnerId);
                }
            }

            return await MapToDtoAsync(result, currentUserId, role: "SITTER");
        }

        public async Task<BookingDetailDto> OwnerConfirmCompletionAsync(string currentUserId, string bookingId)
        {
            var nowUtc = DateTime.UtcNow;
            await ProcessLazyBookingUpdatesAsync(ownerId: currentUserId, sitterId: null, nowUtc: nowUtc);

            // Ghi chú nghiệp vụ: Nếu chủ nuôi bấm xác nhận khi đã quá CompletionDeadline nhưng lazy auto-complete
            // chưa kịp quét chạy, lệnh dưới vẫn chuyển đơn sang COMPLETED hợp lệ với HanhDong = "OWNER_CONFIRMED".
            // Đây là hành vi chấp nhận có chủ ý (intended behavior), vì mục tiêu cuối cùng vẫn là hoàn tất đơn dịch vụ.
            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                       & Builders<Booking>.Filter.Eq(b => b.OwnerId, currentUserId)
                       & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING);

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, BookingStatus.COMPLETED)
                .Set(b => b.IsActive, false)
                .Set(b => b.OwnerConfirmedAt, nowUtc)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = "OWNER_CONFIRMED",
                    FromStatus = BookingStatus.COMPLETION_PENDING,
                    NguoiThucHien = "OWNER",
                    ActorUserId = currentUserId,
                    LyDo = null,
                    ThoiGian = nowUtc
                });

            var result = await _context.Bookings.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After });

            if (result == null)
            {
                var existing = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
                if (existing == null || existing.OwnerId != currentUserId)
                    throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

                if (existing.TrangThai == BookingStatus.COMPLETED)
                    throw new ConflictException("Đơn đặt lịch đã ở trạng thái hoàn tất.");

                if (existing.TrangThai == BookingStatus.DISPUTED)
                    throw new ConflictException("Đơn đặt lịch đang trong trạng thái khiếu nại.");

                throw new ConflictException("Không thể xác nhận hoàn tất. Trạng thái đơn không hợp lệ hoặc đã thay đổi.");
            }

            if (_notificationService != null)
            {
                try
                {
                    var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == result.SitterId).FirstOrDefaultAsync();
                    if (sitterProfile != null && !string.IsNullOrEmpty(sitterProfile.UserId))
                    {
                        var dedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_COMPLETED}:{sitterProfile.UserId}";
                        await _notificationService.CreateNotificationAsync(
                            sitterProfile.UserId,
                            NotificationType.BOOKING_COMPLETED,
                            result.BookingId,
                            "Chủ nuôi đã xác nhận hoàn tất",
                            $"Chủ nuôi đã xác nhận hoàn tất dịch vụ cho đơn #{result.MaDatLich}.",
                            dedupeKey,
                            targetRole: "SITTER"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_COMPLETED cho Sitter {SitterId}", result.SitterId);
                }
            }

            return await MapToDtoAsync(result, currentUserId, role: "OWNER");
        }

        public async Task<BookingDetailDto> OwnerDisputeBookingAsync(string currentUserId, string bookingId, string lyDo)
        {
            if (string.IsNullOrWhiteSpace(lyDo))
                throw new BadRequestException("Lý do khiếu nại là bắt buộc.");

            var trimmedReason = lyDo.Trim();
            if (trimmedReason.Length < 10 || trimmedReason.Length > 500)
                throw new BadRequestException("Lý do khiếu nại phải từ 10 đến 500 ký tự.");

            var nowUtc = DateTime.UtcNow;
            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                       & Builders<Booking>.Filter.Eq(b => b.OwnerId, currentUserId)
                       & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING)
                       & Builders<Booking>.Filter.Gt(b => b.CompletionDeadline, nowUtc);

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, BookingStatus.DISPUTED)
                .Set(b => b.IsActive, true)
                .Set(b => b.DisputeReason, trimmedReason)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = "DISPUTED",
                    FromStatus = BookingStatus.COMPLETION_PENDING,
                    NguoiThucHien = "OWNER",
                    ActorUserId = currentUserId,
                    LyDo = trimmedReason,
                    ThoiGian = nowUtc
                });

            var result = await _context.Bookings.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After });

            if (result == null)
            {
                var existing = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
                if (existing == null || existing.OwnerId != currentUserId)
                    throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

                if (existing.TrangThai == BookingStatus.COMPLETION_PENDING && existing.CompletionDeadline <= nowUtc)
                {
                    await ProcessLazyBookingUpdatesAsync(ownerId: currentUserId, sitterId: null, nowUtc: nowUtc);
                    throw new ConflictException("Đã quá thời hạn khiếu nại (đơn đặt lịch đã tự động hoàn tất).");
                }

                if (existing.TrangThai == BookingStatus.COMPLETED)
                    throw new ConflictException("Đơn đặt lịch đã ở trạng thái hoàn tất.");

                throw new ConflictException("Không thể khiếu nại đơn. Trạng thái đơn không hợp lệ hoặc đã thay đổi.");
            }

            if (_notificationService != null)
            {
                try
                {
                    // 1. Cho Sitter (chỉ thông báo "đơn có khiếu nại", không in lý do thô để tránh quấy rối / lộ bảo mật)
                    var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == result.SitterId).FirstOrDefaultAsync();
                    if (sitterProfile != null && !string.IsNullOrEmpty(sitterProfile.UserId))
                    {
                        var dedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_DISPUTED}:{sitterProfile.UserId}";
                        await _notificationService.CreateNotificationAsync(
                            sitterProfile.UserId,
                            NotificationType.BOOKING_DISPUTED,
                            result.BookingId,
                            "Đơn đặt lịch có khiếu nại",
                            $"Đơn đặt lịch #{result.MaDatLich} có khiếu nại đang chờ xử lý.",
                            dedupeKey,
                            targetRole: "SITTER"
                        );
                    }

                    // 2. Cho tất cả Admin (giới hạn tối đa 50 tài khoản để tránh tràn tải / write amplification)
                    // Ghi chú kiến trúc & giới hạn tải: Giới hạn tối đa 50 tài khoản Admin được thông báo khi có khiếu nại
                    // nhằm ngăn chặn bùng nổ fan-out tải ghi trong CSDL nếu số lượng Admin lớn.
                    // Trong hệ thống mở rộng tương lai, nên sử dụng queue/message broker (RabbitMQ/Kafka) hoặc phân luồng
                    // thông báo theo nhóm trực/ca trực thay vì fan-out trực tiếp trong luồng request HTTP.
                    var admins = await _context.Users.Find(u => u.Role == "ADMIN").Limit(50).ToListAsync();
                    foreach (var admin in admins)
                    {
                        var adminDedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_DISPUTED}:{admin.UserId}";
                        await _notificationService.CreateNotificationAsync(
                            admin.UserId,
                            NotificationType.BOOKING_DISPUTED,
                            result.BookingId,
                            "Có khiếu nại đơn đặt lịch mới",
                            $"Đơn đặt lịch #{result.MaDatLich} có khiếu nại đang chờ xử lý.",
                            adminDedupeKey,
                            targetRole: "ADMIN"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_DISPUTED cho đơn {BookingId}", result.BookingId);
                }
            }

            return await MapToDtoAsync(result, currentUserId, role: "OWNER");
        }

        public async Task<BookingDetailDto> AdminResolveDisputeAsync(string adminUserId, string bookingId, ResolveDisputeDto dto)
        {
            if (dto == null)
                throw new BadRequestException("Dữ liệu không hợp lệ.");

            if (dto.Resolution != BookingStatus.COMPLETED && dto.Resolution != BookingStatus.CANCELLED)
                throw new BadRequestException("Resolution chỉ nhận COMPLETED hoặc CANCELLED.");

            if (string.IsNullOrWhiteSpace(dto.Note))
                throw new BadRequestException("Ghi chú xử lý là bắt buộc.");

            var trimmedNote = dto.Note.Trim();
            if (trimmedNote.Length < 10 || trimmedNote.Length > 500)
                throw new BadRequestException("Ghi chú xử lý phải từ 10 đến 500 ký tự.");

            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null)
                throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

            // Kiểm tra xung đột lợi ích: Admin không được xử lý đơn mà chính mình là Owner hoặc Sitter
            if (booking.OwnerId == adminUserId)
                throw new BadRequestException("Admin không thể tự xử lý đơn khiếu nại của chính mình với tư cách là chủ thú cưng.");

            var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == booking.SitterId).FirstOrDefaultAsync();
            if (sitterProfile != null && sitterProfile.UserId == adminUserId)
                throw new BadRequestException("Admin không thể tự xử lý đơn khiếu nại của chính mình với tư cách là người chăm sóc.");

            var nowUtc = DateTime.UtcNow;
            var isCompleted = dto.Resolution == BookingStatus.COMPLETED;
            var actionName = isCompleted ? "ADMIN_RESOLVED_COMPLETED" : "ADMIN_RESOLVED_CANCELLED";

            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId)
                       & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.DISPUTED);

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, dto.Resolution)
                .Set(b => b.IsActive, false)
                .Set(b => b.AdminResolutionNote, trimmedNote)
                .Set(b => b.ResolvedByAdmin, true)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = actionName,
                    FromStatus = BookingStatus.DISPUTED,
                    NguoiThucHien = "ADMIN",
                    ActorUserId = adminUserId,
                    LyDo = trimmedNote,
                    ThoiGian = nowUtc
                });

            var result = await _context.Bookings.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After });

            if (result == null)
                throw new ConflictException("Không thể xử lý tranh chấp. Đơn không ở trạng thái DISPUTED hoặc đã được xử lý.");

            if (_notificationService != null)
            {
                try
                {
                    var resolutionText = isCompleted ? "Hoàn tất dịch vụ" : "Hủy đơn";

                    // 1. Cho Chủ nuôi
                    if (!string.IsNullOrEmpty(result.OwnerId))
                    {
                        var ownerDedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_RESOLVED}:{result.OwnerId}";
                        await _notificationService.CreateNotificationAsync(
                            result.OwnerId,
                            NotificationType.BOOKING_RESOLVED,
                            result.BookingId,
                            "Khiếu nại đã được xử lý",
                            $"Khiếu nại cho đơn #{result.MaDatLich} đã được Admin xử lý: {resolutionText}.",
                            ownerDedupeKey,
                            targetRole: "OWNER"
                        );
                    }

                    // 2. Cho Sitter
                    if (sitterProfile != null && !string.IsNullOrEmpty(sitterProfile.UserId))
                    {
                        var sitterDedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_RESOLVED}:{sitterProfile.UserId}";
                        await _notificationService.CreateNotificationAsync(
                            sitterProfile.UserId,
                            NotificationType.BOOKING_RESOLVED,
                            result.BookingId,
                            "Khiếu nại đã được xử lý",
                            $"Khiếu nại cho đơn #{result.MaDatLich} đã được Admin xử lý: {resolutionText}.",
                            sitterDedupeKey,
                            targetRole: "SITTER"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_RESOLVED cho đơn {BookingId}", result.BookingId);
                }
            }

            return await MapToDtoAsync(result, adminUserId, role: "ADMIN");
        }

        public async Task<List<BookingDetailDto>> GetDisputedBookingsForAdminAsync()
        {
            var bookings = await _context.Bookings.Find(b => b.TrangThai == BookingStatus.DISPUTED).SortByDescending(b => b.CreatedAt).ToListAsync();
            var dtos = new List<BookingDetailDto>();
            foreach (var b in bookings)
            {
                dtos.Add(await MapToDtoAsync(b, null, role: "ADMIN"));
            }
            return dtos;
        }

        private async Task<BookingDetailDto> MapToDtoAsync(Booking booking, string? currentUserId = null, string? role = null)
        {
            var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == booking.SitterId).FirstOrDefaultAsync();
            var user = sitterProfile != null ? await _context.Users.Find(u => u.UserId == sitterProfile.UserId).FirstOrDefaultAsync() : null;
            
            var hasReview = await _context.Reviews.CountDocumentsAsync(r => r.BookingId == booking.BookingId) > 0;

            var historyDtos = (booking.History ?? new List<InquiryHistory>()).Select(h => new InquiryHistoryDto
            {
                HanhDong = h.HanhDong,
                FromStatus = h.FromStatus,
                NguoiThucHien = h.NguoiThucHien,
                ActorUserId = h.ActorUserId,
                LyDo = h.LyDo,
                ThoiGian = h.ThoiGian
            }).ToList();

            bool isOwner = !string.IsNullOrEmpty(currentUserId) && booking.OwnerId == currentUserId;
            bool isSitter = !string.IsNullOrEmpty(currentUserId) && sitterProfile != null && sitterProfile.UserId == currentUserId;
            bool isAdmin = role == "ADMIN";

            // Yêu cầu 5: DTO list/detail trả CompletionDeadline, DisputeReason, AdminResolutionNote theo đúng vai (chỉ Owner, Sitter của đơn, Admin)
            DateTime? completionDeadline = null;
            string? disputeReason = null;
            string? adminResolutionNote = null;
            DateTime? sitterCompletedAt = null;
            DateTime? ownerConfirmedAt = null;
            bool? autoCompleted = null;
            bool? resolvedByAdmin = null;

            if (isOwner || isSitter || isAdmin)
            {
                completionDeadline = booking.CompletionDeadline;
                disputeReason = booking.DisputeReason;
                adminResolutionNote = booking.AdminResolutionNote;
                sitterCompletedAt = booking.SitterCompletedAt;
                ownerConfirmedAt = booking.OwnerConfirmedAt;
                autoCompleted = booking.AutoCompleted;
                resolvedByAdmin = booking.ResolvedByAdmin;
            }

            return new BookingDetailDto
            {
                BookingId = booking.BookingId,
                MaDatLich = booking.MaDatLich,
                OwnerId = booking.OwnerId,
                SitterId = booking.SitterId,
                SitterName = user?.FullName ?? "Unknown",
                SitterAvatarUrl = sitterProfile?.AvatarUrl ?? "",
                NgayBatDau = booking.NgayBatDau,
                NgayKetThuc = booking.NgayKetThuc,
                SoNgay = booking.SoNgay,
                GiaSitter = booking.GiaSitter,
                TongAddons = booking.TongAddons,
                TongTien = booking.TongTien,
                TrangThai = booking.TrangThai,
                IsActive = booking.IsActive,
                ExpiresAt = booking.ExpiresAt,
                LyDoTuChoi = booking.LyDoTuChoi,
                HasReview = hasReview,
                GhiChu = booking.GhiChu,
                SitterCompletedAt = sitterCompletedAt,
                CompletionDeadline = completionDeadline,
                OwnerConfirmedAt = ownerConfirmedAt,
                AutoCompleted = autoCompleted,
                DisputeReason = disputeReason,
                AdminResolutionNote = adminResolutionNote,
                ResolvedByAdmin = resolvedByAdmin,
                Pet = booking.Pet,
                Addons = booking.Addons,
                History = historyDtos
            };
        }
    }
}
