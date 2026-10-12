using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

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

        public async Task<BookingDetailDto> CreateBookingAsync(string currentUserId, CreateBookingDto dto)
        {
            // 1. Validate dates
            var currentVnTime = DateTime.UtcNow.AddHours(7).Date;
            if (dto.NgayBatDau.Date < currentVnTime)
                throw new BadRequestException("Ngày bắt đầu không được ở quá khứ.");

            if (dto.NgayKetThuc.Date < dto.NgayBatDau.Date)
                throw new BadRequestException("Ngày kết thúc không hợp lệ.");

            var soNgay = (dto.NgayKetThuc.Date - dto.NgayBatDau.Date).Days + 1;

            if (!string.IsNullOrEmpty(dto.PetId))
            {
                var pet = await _context.Pets.Find(p => p.PetId == dto.PetId).FirstOrDefaultAsync();
                if (pet == null || pet.OwnerId != currentUserId)
                {
                    throw new BadRequestException("Thú cưng không hợp lệ.");
                }
            }

            // 2. Validate Sitter
            var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == dto.SitterId).FirstOrDefaultAsync();
            if (sitterProfile == null)
                throw new NotFoundException("Sitter không tồn tại.");
            if (!sitterProfile.IsVerified || sitterProfile.Status != "Approved")
                throw new BadRequestException("Sitter chưa được duyệt.");

            // 3. Validate SitterAvailabilities
            var availabilities = await _context.SitterAvailabilities
                .Find(a => a.SitterId == dto.SitterId && a.AvailableDate >= dto.NgayBatDau.Date && a.AvailableDate <= dto.NgayKetThuc.Date)
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
                    if (ss == null) continue; // Sitter doesn't offer this

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

            // 7. Try to insert BookedDates
            var datesToInsert = new List<BookedDate>();
            for (int i = 0; i < soNgay; i++)
            {
                datesToInsert.Add(new BookedDate
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    SitterId = dto.SitterId,
                    BookingId = bookingId,
                    Date = DateTime.SpecifyKind(dto.NgayBatDau.Date.AddDays(i), DateTimeKind.Utc)
                });
            }

            try
            {
                if (datesToInsert.Count > 0)
                {
                    await _context.BookedDates.InsertManyAsync(datesToInsert);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConflictException("Sitter đã có lịch vào khoảng thời gian này.");
            }
            
            // 8. Create booking
            var booking = new Booking
            {
                BookingId = bookingId,
                MaDatLich = maDatLich,
                OwnerId = currentUserId,
                SitterId = dto.SitterId,
                NgayBatDau = DateTime.SpecifyKind(dto.NgayBatDau.Date, DateTimeKind.Utc),
                NgayKetThuc = DateTime.SpecifyKind(dto.NgayKetThuc.Date, DateTimeKind.Utc),
                SoNgay = soNgay,
                GhiChu = dto.GhiChu,
                Pet = new BookingPet {
                    TenPet = dto.TenPet,
                    LoaiPet = dto.LoaiPet,
                    KichCo = dto.KichCo
                },
                Addons = bookingAddons,
                GiaSitter = giaSitter,
                TongAddons = tongAddons,
                TongTien = tongTien,
                TrangThai = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            await _context.Bookings.InsertOneAsync(booking);

            return await MapToDtoAsync(booking);
        }

        public async Task<BookingDetailDto> GetBookingByIdAsync(string currentUserId, string bookingId)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null)
                throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

            // Check access
            if (booking.OwnerId != currentUserId)
            {
                var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == currentUserId).FirstOrDefaultAsync();
                if (sitterProfile == null || sitterProfile.SitterId != booking.SitterId)
                    throw new ForbiddenException("Không có quyền truy cập thông tin đặt lịch này.");
            }

            return await MapToDtoAsync(booking);
        }

                        public async Task<System.Collections.Generic.List<BookingDetailDto>> GetMyBookingsAsync(string currentUserId)
        {
            var bookings = await _context.Bookings.Find(b => b.OwnerId == currentUserId).SortByDescending(b => b.CreatedAt).ToListAsync();
            var dtos = new System.Collections.Generic.List<BookingDetailDto>();
            foreach(var booking in bookings)
            {
                dtos.Add(await MapToDtoAsync(booking));
            }
            return dtos;
        }

        public async Task CancelBookingAsync(string currentUserId, string bookingId)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null) throw new NotFoundException("Không tìm thấy thông tin đặt lịch.");

            if (booking.OwnerId != currentUserId)
            {
                var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == currentUserId).FirstOrDefaultAsync();
                if (sitterProfile == null || sitterProfile.SitterId != booking.SitterId)
                    throw new ForbiddenException("Không có quyền truy cập thông tin đặt lịch này.");
            }

            if (booking.TrangThai == "CANCELLED" || booking.TrangThai == "REJECTED" || booking.TrangThai == "COMPLETED")
                throw new BadRequestException("Không thể hủy đặt lịch ở trạng thái này.");

            var currentVnTime = DateTime.UtcNow.AddHours(7).Date;
            if (booking.TrangThai == "CONFIRMED" && booking.NgayBatDau.Date <= currentVnTime)
                throw new BadRequestException("Không thể hủy đặt lịch đã xác nhận khi đã đến ngày bắt đầu.");

            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, bookingId);
            var update = Builders<Booking>.Update.Set(b => b.TrangThai, "CANCELLED");
            await _context.Bookings.UpdateOneAsync(filter, update);

            var deleteFilter = Builders<BookedDate>.Filter.Eq(b => b.BookingId, bookingId);
            await _context.BookedDates.DeleteManyAsync(deleteFilter);
        }

                        public async Task<ReviewDto> AddReviewAsync(string currentUserId, string bookingId, CreateReviewDto dto)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null) throw new NotFoundException("Booking không tồn tại.");

            if (booking.OwnerId != currentUserId) throw new ForbiddenException("Bạn không có quyền đánh giá.");
            if (booking.TrangThai != "COMPLETED") throw new BadRequestException("Chỉ có thể đánh giá booking đã hoàn thành.");

            var existingReview = await _context.Reviews.Find(r => r.BookingId == bookingId).FirstOrDefaultAsync();
            if (existingReview != null) throw new ConflictException("Booking này đã được đánh giá.");

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

                        try
            {
                await _context.Reviews.InsertOneAsync(review);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConflictException("Booking này đã được đánh giá.");
            }

            // Update Sitter Profile
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
                LyDoTuChoi = booking.LyDoTuChoi,
                HasReview = hasReview,
                GhiChu = booking.GhiChu,
                Pet = booking.Pet,
                Addons = booking.Addons
            };
        }
    }
}
