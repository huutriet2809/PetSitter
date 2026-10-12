using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using System;
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

        public BookingService(PetSitterContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public static DateTime GetVnToday()
        {
            var vnNow = DateTime.UtcNow.AddHours(7);
            return DateTime.SpecifyKind(vnNow.Date, DateTimeKind.Utc);
        }

        public async Task ExpireOverduePendingBookingsAsync(string? ownerId = null, string? sitterId = null)
        {
            if (string.IsNullOrEmpty(ownerId) && string.IsNullOrEmpty(sitterId))
                return;

            var nowUtc = DateTime.UtcNow;
            var filterBuilder = Builders<Booking>.Filter;
            var filter = filterBuilder.Eq(b => b.TrangThai, BookingStatus.PENDING)
                       & filterBuilder.Lte(b => b.ExpiresAt, nowUtc);

            if (!string.IsNullOrEmpty(ownerId))
            {
                filter &= filterBuilder.Eq(b => b.OwnerId, ownerId);
            }
            if (!string.IsNullOrEmpty(sitterId))
            {
                filter &= filterBuilder.Eq(b => b.SitterId, sitterId);
            }

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, BookingStatus.EXPIRED)
                .Set(b => b.IsActive, false)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = BookingStatus.EXPIRED,
                    FromStatus = BookingStatus.PENDING,
                    NguoiThucHien = "SYSTEM",
                    ActorUserId = null,
                    LyDo = "Quá hạn 48 giờ không được chấp nhận",
                    ThoiGian = nowUtc
                });

            await _context.Bookings.UpdateManyAsync(filter, update);
        }

        public async Task<BookingDetailDto> CreateBookingAsync(string currentUserId, CreateBookingDto dto)
        {
            // 0. Chạy hết hạn lười cho các đơn PENDING quá hạn của Owner và Sitter
            await ExpireOverduePendingBookingsAsync(ownerId: currentUserId, sitterId: null);
            await ExpireOverduePendingBookingsAsync(ownerId: null, sitterId: dto.SitterId);

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

            if (booking.TrangThai == BookingStatus.PENDING && booking.ExpiresAt <= DateTime.UtcNow)
            {
                if (isOwner)
                    await ExpireOverduePendingBookingsAsync(ownerId: currentUserId, sitterId: null);
                else
                    await ExpireOverduePendingBookingsAsync(ownerId: null, sitterId: booking.SitterId);

                booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync() ?? booking;
            }

            return await MapToDtoAsync(booking);
        }

        public async Task<List<BookingDetailDto>> GetMyBookingsAsync(string currentUserId)
        {
            await ExpireOverduePendingBookingsAsync(ownerId: currentUserId, sitterId: null);

            var bookings = await _context.Bookings.Find(b => b.OwnerId == currentUserId).SortByDescending(b => b.CreatedAt).ToListAsync();
            var dtos = new List<BookingDetailDto>();
            foreach(var booking in bookings)
            {
                dtos.Add(await MapToDtoAsync(booking));
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
            }
        }

        public async Task<ReviewDto> AddReviewAsync(string currentUserId, string bookingId, CreateReviewDto dto)
        {
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

        private async Task<BookingDetailDto> MapToDtoAsync(Booking booking)
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
                Pet = booking.Pet,
                Addons = booking.Addons,
                History = historyDtos
            };
        }
    }
}
