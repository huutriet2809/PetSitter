using MongoDB.Driver;
using MongoDB.Driver.Linq;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace PetSitter.Services
{
    public class SitterService : ISitterService
    {
        private readonly PetSitterContext _context;

        public SitterService(PetSitterContext context)
        {
            _context = context;
        }

        public async Task<List<SitterListDto>> GetSittersAsync(string? khuVuc, string? loaiPet, int page = 1, int pageSize = 10)
        {
            var sitters = await _context.SitterProfiles.Find(s => s.Status == "Approved" && s.IsVerified == true).ToListAsync();
            var users = await _context.Users.Find(_ => true).ToListAsync();
            var sitterServices = await _context.SitterServices.Find(_ => true).ToListAsync();
            var services = await _context.Services.Find(_ => true).ToListAsync();

            var query = sitters.Select(s => {
                var user = users.FirstOrDefault(u => u.UserId == s.UserId);
                var srvs = sitterServices.Where(ss => ss.SitterId == s.SitterId)
                                         .Select(ss => services.FirstOrDefault(srv => srv.ServiceId == ss.ServiceId))
                                         .Where(srv => srv != null)
                                         .ToList();
                var tags = srvs.Select(srv => srv.ServiceName).ToList();
                decimal minPrice = sitterServices.Where(ss => ss.SitterId == s.SitterId).Select(ss => ss.Price).DefaultIfEmpty(0).Min();

                return new SitterListDto
                {
                    SitterId = s.SitterId,
                    Ten = user?.FullName ?? "Unknown",
                    KhuVuc = s.Address ?? "Đà Nẵng",
                    DanhGia = s.Rating,
                    SoDanhGia = s.TotalReviews,
                    MoTaNgan = s.Bio?.Length > 100 ? s.Bio.Substring(0, 97) + "..." : (s.Bio ?? ""),
                    AvatarUrl = s.AvatarUrl ?? "",
                    Tags = tags,
                    GiaTu = minPrice,
                    NoiBat = s.Rating >= 4.8m && s.TotalReviews > 10 // Dummy logic for feature
                };
            }).AsQueryable();

            if (!string.IsNullOrEmpty(khuVuc))
            {
                query = query.Where(q => q.KhuVuc.Contains(khuVuc, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(loaiPet))
            {
                query = query.Where(q => q.Tags.Any(t => t.Contains(loaiPet, StringComparison.OrdinalIgnoreCase)));
            }

            return query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        public async Task<List<SitterListDto>> GetFeaturedSittersAsync()
        {
            var sitters = await _context.SitterProfiles.Find(s => s.Status == "Approved" && s.IsVerified == true).ToListAsync();
            var users = await _context.Users.Find(_ => true).ToListAsync();
            var sitterServices = await _context.SitterServices.Find(_ => true).ToListAsync();
            var services = await _context.Services.Find(_ => true).ToListAsync();

            var result = sitters
                .Where(s => s.Rating >= 4.8m && s.TotalReviews > 10)
                .Take(6) // Only top 6
                .Select(s => {
                    var user = users.FirstOrDefault(u => u.UserId == s.UserId);
                    var srvs = sitterServices.Where(ss => ss.SitterId == s.SitterId)
                                             .Select(ss => services.FirstOrDefault(srv => srv.ServiceId == ss.ServiceId))
                                             .Where(srv => srv != null)
                                             .ToList();
                    var tags = srvs.Select(srv => srv.ServiceName).ToList();
                    decimal minPrice = sitterServices.Where(ss => ss.SitterId == s.SitterId).Select(ss => ss.Price).DefaultIfEmpty(0).Min();

                    return new SitterListDto
                    {
                        SitterId = s.SitterId,
                        Ten = user?.FullName ?? "Unknown",
                        KhuVuc = s.Address ?? "Đà Nẵng",
                        DanhGia = s.Rating,
                        SoDanhGia = s.TotalReviews,
                        MoTaNgan = s.Bio?.Length > 100 ? s.Bio.Substring(0, 97) + "..." : (s.Bio ?? ""),
                        AvatarUrl = s.AvatarUrl ?? "",
                        Tags = tags,
                        GiaTu = minPrice,
                        NoiBat = true
                    };
                }).ToList();

            // Fallback if not enough featured sitters
            if (!result.Any())
            {
                result = sitters.Take(6).Select(s => {
                    var user = users.FirstOrDefault(u => u.UserId == s.UserId);
                    var srvs = sitterServices.Where(ss => ss.SitterId == s.SitterId)
                                             .Select(ss => services.FirstOrDefault(srv => srv.ServiceId == ss.ServiceId))
                                             .Where(srv => srv != null)
                                             .ToList();
                    var tags = srvs.Select(srv => srv.ServiceName).ToList();
                    decimal minPrice = sitterServices.Where(ss => ss.SitterId == s.SitterId).Select(ss => ss.Price).DefaultIfEmpty(0).Min();

                    return new SitterListDto
                    {
                        SitterId = s.SitterId,
                        Ten = user?.FullName ?? "Unknown",
                        KhuVuc = s.Address ?? "Đà Nẵng",
                        DanhGia = s.Rating,
                        SoDanhGia = s.TotalReviews,
                        MoTaNgan = s.Bio?.Length > 100 ? s.Bio.Substring(0, 97) + "..." : (s.Bio ?? ""),
                        AvatarUrl = s.AvatarUrl ?? "",
                        Tags = tags,
                        GiaTu = minPrice,
                        NoiBat = true
                    };
                }).ToList();
            }

            return result;
        }

        public async Task<SitterDetailDto?> GetSitterByIdAsync(string id)
        {
            var sitter = await _context.SitterProfiles.Find(s => s.SitterId == id).FirstOrDefaultAsync();
            if (sitter == null) return null;

            var user = await _context.Users.Find(u => u.UserId == sitter.UserId).FirstOrDefaultAsync();
            
            var sitterServicesList = await _context.SitterServices.Find(ss => ss.SitterId == id).ToListAsync();
            var serviceIds = sitterServicesList.Select(ss => ss.ServiceId).ToList();
            var services = await _context.Services.Find(s => serviceIds.Contains(s.ServiceId)).ToListAsync();

            var availabilities = await _context.SitterAvailabilities
                .Find(a => a.SitterId == id && a.AvailableDate >= DateTime.UtcNow.Date && a.IsAvailable)
                .ToListAsync();

            var dichVu = sitterServicesList.Select(ss => {
                var srv = services.FirstOrDefault(s => s.ServiceId == ss.ServiceId);
                return new ServiceDto {
                    Id = ss.SitterServiceId,
                    Ten = srv?.ServiceName ?? "",
                    MoTa = srv?.Description ?? "",
                    Gia = ss.Price
                };
            }).ToList();

            var tags = services.Select(s => s.ServiceName).ToList();
            var lichRanh = availabilities.Select(a => a.AvailableDate.ToString("yyyy-MM-dd")).Distinct().ToList();

            return new SitterDetailDto
            {
                SitterId = sitter.SitterId,
                Ten = user?.FullName ?? "Unknown",
                KhuVuc = sitter.Address ?? "Đà Nẵng",
                DanhGia = sitter.Rating,
                SoDanhGia = sitter.TotalReviews,
                AvatarUrl = sitter.AvatarUrl ?? "",
                Bio = sitter.Bio ?? "",
                KinhNghiem = sitter.ExperienceYears,
                Tags = tags,
                DichVu = dichVu,
                LichRanh = lichRanh
            };
        }

        public async Task<List<ReviewDto>> GetSitterReviewsAsync(string id)
        {
            var reviews = await _context.Reviews.Find(r => r.SitterId == id).ToListAsync();
            var userIds = reviews.Select(r => r.OwnerId).Distinct().ToList();
            var users = await _context.Users.Find(u => userIds.Contains(u.UserId)).ToListAsync();

            return reviews.Select(r => {
                var user = users.FirstOrDefault(u => u.UserId == r.OwnerId);
                return new ReviewDto
                {
                    ReviewId = r.ReviewId,
                    NguoiDung = user?.FullName ?? "Người dùng",
                    Avatar = user?.SitterProfile?.AvatarUrl ?? "", // Usually owner avatar, fallback to empty
                    Rating = r.Rating,
                    Comment = r.Comment ?? "",
                    Ngay = r.CreatedAt
                };
            }).OrderByDescending(r => r.Ngay).ToList();
        }

        public async Task<List<PublicReviewDto>> GetTopReviewsAsync(int limit = 6)
        {
            var reviews = await _context.Reviews
                .Find(r => r.Rating >= 4)
                .SortByDescending(r => r.CreatedAt)
                .Limit(limit)
                .ToListAsync();

            if (!reviews.Any()) return new List<PublicReviewDto>();

            var ownerIds = reviews.Select(r => r.OwnerId).Distinct().ToList();
            var owners = await _context.Users.Find(u => ownerIds.Contains(u.UserId)).ToListAsync();

            var sitterIds = reviews.Select(r => r.SitterId).Distinct().ToList();
            var sitterProfiles = await _context.SitterProfiles.Find(sp => sitterIds.Contains(sp.SitterId)).ToListAsync();
            var sitterUserIds = sitterProfiles.Select(sp => sp.UserId).Distinct().ToList();
            var sitterUsers = await _context.Users.Find(u => sitterUserIds.Contains(u.UserId)).ToListAsync();

            var result = new List<PublicReviewDto>();
            foreach (var r in reviews)
            {
                var owner = owners.FirstOrDefault(o => o.UserId == r.OwnerId);
                var sitterProfile = sitterProfiles.FirstOrDefault(sp => sp.SitterId == r.SitterId);
                var sitterUser = sitterProfile != null ? sitterUsers.FirstOrDefault(u => u.UserId == sitterProfile.UserId) : null;

                result.Add(new PublicReviewDto
                {
                    NguoiDung = owner?.FullName ?? "Khách hàng",
                    Avatar = owner?.AvatarUrl,
                    Rating = r.Rating,
                    Comment = r.Comment ?? "",
                    SitterName = sitterUser?.FullName ?? "Sitter",
                    Ngay = r.CreatedAt
                });
            }

            return result;
        }

                public async Task<List<string>> GetSitterAvailabilityAsync(string id, string month)
        {
            var dates = new List<string>();
            if (string.IsNullOrEmpty(month) || month.Length != 7) return dates;
            
            var startDate = DateTime.Parse(month + "-01").ToUniversalTime();
            var endDate = startDate.AddMonths(1);

            var availabilities = await _context.SitterAvailabilities
                .Find(a => a.SitterId == id && a.IsAvailable && a.AvailableDate >= startDate && a.AvailableDate < endDate)
                .ToListAsync();

            var acceptedBookings = await _context.Bookings
                .Find(b => b.SitterId == id && b.TrangThai == BookingStatus.ACCEPTED && b.NgayBatDau < endDate && b.NgayKetThuc >= startDate)
                .ToListAsync();

            foreach (var a in availabilities)
            {
                bool isBooked = acceptedBookings.Any(b => a.AvailableDate >= b.NgayBatDau && a.AvailableDate <= b.NgayKetThuc);
                if (!isBooked)
                {
                    dates.Add(a.AvailableDate.ToString("yyyy-MM-dd"));
                }
            }

            return dates;
        }

        public async Task<List<ServiceDto>> GetServicesAsync()
        {
            var services = await _context.Services.Find(_ => true).ToListAsync();
            return services.Select(s => new ServiceDto
            {
                Id = s.ServiceId,
                Ten = s.ServiceName,
                MoTa = s.Description ?? "",
                Gia = 0 // Base price is not in model, SitterService has the specific price
            }).ToList();
        }

        public async Task<StatsDto> GetStatsAsync()
        {
            var sitters = (int)await _context.SitterProfiles.CountDocumentsAsync(s => s.Status == "Approved" && s.IsVerified == true);
            var users = (int)await _context.Users.CountDocumentsAsync(_ => true);
            var bookings = (int)await _context.Bookings.CountDocumentsAsync(b => b.TrangThai == BookingStatus.COMPLETED);

            return new StatsDto
            {
                Sitters = sitters,
                Users = users,
                Bookings = bookings
            };
        }
            public async Task<SitterDetailDto?> GetSitterProfileByUserIdAsync(string userId)
        {
            var sitter = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitter == null) return null;
            return await GetSitterByIdAsync(sitter.SitterId);
        }

        public async Task UpdateSitterProfileAsync(string userId, UpdateSitterProfileDto dto)
        {
            var sitter = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitter == null) throw new NotFoundException("Không tìm thấy hồ sơ sitter.");

            if (dto.Bio != null && dto.Bio.Length > 1000) throw new BadRequestException("Tiểu sử tối đa 1000 ký tự.");
            if (dto.GiaMoiNgay <= 0 || dto.GiaMoiNgay > 10000000) throw new BadRequestException("Giá mỗi ngày không hợp lệ.");

            var update = Builders<SitterProfile>.Update
                .Set(s => s.Bio, dto.Bio ?? "")
                .Set(s => s.Address, dto.KhuVuc ?? "");
                
            await _context.SitterProfiles.UpdateOneAsync(s => s.UserId == userId, update);

            // Update SitterServices: giữ giá hiện có của SitterService đã tồn tại; dịch vụ mới thêm lấy giá từ catalog Services (Service.Gia)
            var existingSitterServices = await _context.SitterServices.Find(ss => ss.SitterId == sitter.SitterId).ToListAsync();
            var allCatalogServices = await _context.Services.Find(_ => true).ToListAsync();

            await _context.SitterServices.DeleteManyAsync(ss => ss.SitterId == sitter.SitterId);
            
            var newSitterServices = new List<PetSitter.Models.SitterService>();
            foreach (var srvId in dto.DichVuBoSung)
            {
                var existing = existingSitterServices.FirstOrDefault(x => x.ServiceId == srvId);
                var catalogService = allCatalogServices.FirstOrDefault(x => x.ServiceId == srvId);
                decimal servicePrice = existing != null ? existing.Price : (catalogService?.Gia ?? 50000);

                newSitterServices.Add(new PetSitter.Models.SitterService {
                    SitterServiceId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    SitterId = sitter.SitterId,
                    ServiceId = srvId,
                    Price = servicePrice
                });
            }
            
            if (newSitterServices.Any())
                await _context.SitterServices.InsertManyAsync(newSitterServices);
        }

        public async Task<List<SitterDayAvailabilityDto>> GetSitterAvailabilityCalendarAsync(string userId, string month)
        {
            var sitter = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitter == null) throw new NotFoundException("Không tìm thấy hồ sơ sitter.");

            var dates = new List<SitterDayAvailabilityDto>();
            if (string.IsNullOrEmpty(month) || month.Length != 7) return dates;
            
            var startDate = DateTime.Parse(month + "-01").ToUniversalTime();
            var endDate = startDate.AddMonths(1);
            var daysInMonth = DateTime.DaysInMonth(startDate.Year, startDate.Month);

            var availabilities = await _context.SitterAvailabilities
                .Find(a => a.SitterId == sitter.SitterId && a.AvailableDate >= startDate && a.AvailableDate < endDate)
                .ToListAsync();
            
            var availDict = availabilities.ToDictionary(a => a.AvailableDate, a => a.IsAvailable);

            var bookings = await _context.Bookings
                .Find(b => b.SitterId == sitter.SitterId && b.NgayBatDau < endDate && b.NgayKetThuc >= startDate && b.TrangThai == BookingStatus.ACCEPTED)
                .ToListAsync();

            for (int i = 1; i <= daysInMonth; i++)
            {
                var currentDay = new DateTime(startDate.Year, startDate.Month, i, 0, 0, 0, DateTimeKind.Utc);
                var isBooked = false;
                string? bookingId = null;

                foreach (var b in bookings)
                {
                    if (currentDay >= b.NgayBatDau && currentDay <= b.NgayKetThuc)
                    {
                        isBooked = true;
                        bookingId = b.BookingId;
                        break;
                    }
                }

                string status = "Unavailable";
                if (isBooked) status = "Booked";
                else if (availDict.ContainsKey(currentDay) && availDict[currentDay]) status = "Available";

                dates.Add(new SitterDayAvailabilityDto
                {
                    Date = currentDay.ToString("yyyy-MM-dd"),
                    Status = status,
                    BookingId = bookingId
                });
            }
            return dates;
        }

        public async Task UpdateSitterAvailabilityAsync(string userId, UpdateSitterAvailabilityDto dto)
        {
            var sitter = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitter == null) throw new NotFoundException("Không tìm thấy hồ sơ sitter.");

            if (dto.Dates.Count > 62) throw new BadRequestException("Tối đa cập nhật 62 ngày một lần.");

            var currentVnTime = DateTime.UtcNow.AddHours(7).Date;
            var rejectedDates = new List<string>();

            foreach (var dateStr in dto.Dates)
            {
                if (!DateTime.TryParse(dateStr, out var d)) continue;
                var dt = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
                if (dt < currentVnTime) continue; // Skip past dates

                bool isBooked = false;
                if (!dto.IsAvailable)
                {
                    isBooked = await _context.Bookings.Find(b =>
                        b.SitterId == sitter.SitterId &&
                        b.TrangThai == BookingStatus.ACCEPTED &&
                        b.NgayBatDau <= dt &&
                        b.NgayKetThuc >= dt
                    ).AnyAsync();
                }
                
                if (isBooked)
                {
                    rejectedDates.Add(dateStr);
                    continue; // Cannot change booked to unavailable
                }

                var update = Builders<SitterAvailability>.Update.Set(a => a.IsAvailable, dto.IsAvailable);
                var filter = Builders<SitterAvailability>.Filter.Where(a => a.SitterId == sitter.SitterId && a.AvailableDate == dt);
                
                var existing = await _context.SitterAvailabilities.Find(filter).FirstOrDefaultAsync();
                if (existing != null)
                {
                    await _context.SitterAvailabilities.UpdateOneAsync(filter, update);
                }
                else if (dto.IsAvailable)
                {
                    await _context.SitterAvailabilities.InsertOneAsync(new SitterAvailability
                    {
                        AvailabilityId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterId = sitter.SitterId,
                        AvailableDate = dt,
                        IsAvailable = true
                    });
                }
            }

            if (rejectedDates.Any())
            {
                throw new ConflictException($"Một số ngày đã có đơn đã chấp nhận (ACCEPTED), không thể đổi thành bận: {string.Join(", ", rejectedDates)}");
            }
        }
    }
}