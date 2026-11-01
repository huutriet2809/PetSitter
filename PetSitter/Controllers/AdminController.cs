using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "ADMIN")]
    public class AdminController : ControllerBase
    {
        private readonly PetSitterContext _context;
        private readonly PetSitter.Services.IBookingService _bookingService;
        private readonly PetSitter.Services.ITokenService _tokenService;
        private readonly PetSitter.Services.IEmailService? _emailService;
        private readonly ILogger<AdminController>? _logger;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache? _memoryCache;
        private readonly IWebHostEnvironment? _env;
        private readonly IConfiguration? _config;

        public AdminController(
            PetSitterContext context,
            PetSitter.Services.IBookingService bookingService,
            PetSitter.Services.ITokenService tokenService,
            PetSitter.Services.IEmailService? emailService = null,
            ILogger<AdminController>? logger = null,
            Microsoft.Extensions.Caching.Memory.IMemoryCache? memoryCache = null,
            IWebHostEnvironment? env = null,
            IConfiguration? config = null)
        {
            _context = context;
            _bookingService = bookingService;
            _tokenService = tokenService;
            _emailService = emailService;
            _logger = logger;
            _memoryCache = memoryCache;
            _env = env;
            _config = config;
        }

        [HttpGet("sitter-applications")]
        public async Task<IActionResult> GetApplications([FromQuery] string status = "Pending")
        {
            var profiles = await _context.SitterProfiles.Find(s => s.Status == status).ToListAsync();
            var dtos = new List<SitterApplicationDto>();

            foreach (var profile in profiles)
            {
                var user = await _context.Users.Find(u => u.UserId == profile.UserId).FirstOrDefaultAsync();
                var sitterService = await _context.SitterServices.Find(ss => ss.SitterId == profile.SitterId).FirstOrDefaultAsync();

                dtos.Add(new SitterApplicationDto
                {
                    Id = profile.SitterId,
                    UserId = profile.UserId,
                    FullName = user?.FullName ?? "",
                    Bio = profile.Bio ?? "",
                    KhuVuc = profile.Address ?? "",
                    KinhNghiem = profile.ExperienceYears,
                    Tags = profile.Tags,
                    LoaiPetNhan = profile.LoaiPetNhan,
                    GiaMoiNgay = sitterService?.Price ?? 0,
                    Status = profile.Status,
                    LyDoTuChoi = profile.LyDoTuChoi,
                    CreatedAt = profile.CreatedAt
                });
            }

            return Ok(dtos);
        }

        [HttpPatch("sitter-applications/{id}/approve")]
        public async Task<IActionResult> ApproveApplication(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await _context.SitterProfiles.Find(s => s.SitterId == id).FirstOrDefaultAsync();
            
            if (profile == null) return NotFound(new { message = "Không tìm thấy hồ sơ." });
            if (profile.UserId == currentUserId) return BadRequest(new { message = "Không thể tự duyệt hồ sơ của chính mình." });

            var filter = Builders<SitterProfile>.Filter.Eq(s => s.SitterId, id);
            var update = Builders<SitterProfile>.Update
                .Set(s => s.Status, "Approved")
                .Set(s => s.IsVerified, true)
                .Set(s => s.ReviewedAt, DateTime.UtcNow);
            
            await _context.SitterProfiles.UpdateOneAsync(filter, update);

            var userFilter = Builders<User>.Filter.Eq(u => u.UserId, profile.UserId);
            var userUpdate = Builders<User>.Update.Set(u => u.Role, "SITTER");
            await _context.Users.UpdateOneAsync(userFilter, userUpdate);

            // Create default availabilities (60 days) if not exist
            var hasAvailability = await _context.SitterAvailabilities.Find(a => a.SitterId == id).AnyAsync();
            if (!hasAvailability)
            {
                var availabilities = new List<SitterAvailability>();
                var today = DateTime.UtcNow.AddHours(7).Date;
                for (int i = 0; i < 60; i++)
                {
                    availabilities.Add(new SitterAvailability
                    {
                        AvailabilityId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterId = id,
                        AvailableDate = today.AddDays(i),
                        StartTime = new TimeSpan(7, 0, 0),
                        EndTime = new TimeSpan(20, 0, 0),
                        IsAvailable = true
                    });
                }
                await _context.SitterAvailabilities.InsertManyAsync(availabilities);
            }

            return Ok(new { message = "Đã duyệt hồ sơ." });
        }

        [HttpPatch("sitter-applications/{id}/reject")]
        public async Task<IActionResult> RejectApplication(string id, [FromBody] RejectApplicationDto dto)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await _context.SitterProfiles.Find(s => s.SitterId == id).FirstOrDefaultAsync();
            
            if (profile == null) return NotFound(new { message = "Không tìm thấy hồ sơ." });
            if (profile.UserId == currentUserId) return BadRequest(new { message = "Không thể tự từ chối hồ sơ của chính mình." });

            var filter = Builders<SitterProfile>.Filter.Eq(s => s.SitterId, id);
            var update = Builders<SitterProfile>.Update
                .Set(s => s.Status, "Rejected")
                .Set(s => s.IsVerified, false)
                .Set(s => s.LyDoTuChoi, dto.LyDo)
                .Set(s => s.ReviewedAt, DateTime.UtcNow);
            
            await _context.SitterProfiles.UpdateOneAsync(filter, update);

            return Ok(new { message = "Đã từ chối hồ sơ." });
        }

        [HttpGet("disputed-bookings")]
        public async Task<IActionResult> GetDisputedBookings()
        {
            var bookings = await _bookingService.GetDisputedBookingsForAdminAsync();
            return Ok(bookings);
        }

        [HttpPatch("bookings/{id}/resolve")]
        public async Task<IActionResult> ResolveDispute(string id, [FromBody] ResolveDisputeDto dto)
        {
            try
            {
                var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

                var result = await _bookingService.AdminResolveDisputeAsync(adminUserId, id, dto);
                return Ok(new { message = $"Đã giải quyết tranh chấp đơn đặt lịch thành công ({dto.Resolution}).", booking = result });
            }
            catch (PetSitter.Exceptions.AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] string? search = null,
            [FromQuery] string? role = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var filterBuilder = Builders<User>.Filter;
            var filter = filterBuilder.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                var regex = new MongoDB.Bson.BsonRegularExpression(s, "i");
                var searchFilter = filterBuilder.Regex(u => u.FullName, regex)
                                 | filterBuilder.Regex(u => u.Email, regex)
                                 | filterBuilder.Regex(u => u.Phone, regex);
                filter &= searchFilter;
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                filter &= filterBuilder.Eq(u => u.Role, role.ToUpper());
            }

            if (isActive.HasValue)
            {
                if (isActive.Value)
                {
                    // Bao gồm cả document cũ chưa có field IsActive
                    filter &= filterBuilder.Ne(u => u.IsActive, false);
                }
                else
                {
                    filter &= filterBuilder.Eq(u => u.IsActive, false);
                }
            }

            var totalCount = await _context.Users.CountDocumentsAsync(filter);
            var users = await _context.Users.Find(filter)
                .SortByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            var dtos = users.Select(u => new AdminUserDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                Phone = u.Phone,
                Role = u.Role,
                IsActive = u.IsActive != false,
                LockoutEndUtc = u.LockoutEndUtc,
                FailedLoginAttempts = u.FailedLoginAttempts,
                CreatedAt = u.CreatedAt
            }).ToList();

            return Ok(new
            {
                total = totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                data = dtos
            });
        }

        [HttpPatch("users/{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(string id, [FromBody] UpdateUserStatusDto dto)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

            if (id == currentUserId && !dto.IsActive)
            {
                return BadRequest(new { message = "Không thể tự khóa tài khoản của chính mình." });
            }

            var user = await _context.Users.Find(u => u.UserId == id).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var filter = Builders<User>.Filter.Eq(u => u.UserId, id);
            var updateBuilder = Builders<User>.Update.Set(u => u.IsActive, dto.IsActive);

            if (dto.IsActive)
            {
                // Mở khóa: xóa lockout nếu đang bị khóa tạm
                updateBuilder = updateBuilder
                    .Set(u => u.LockoutEndUtc, null)
                    .Set(u => u.FailedLoginAttempts, 0);
            }

            await _context.Users.UpdateOneAsync(filter, updateBuilder);

            // Khi khóa tài khoản: lập tức thu hồi toàn bộ token đăng nhập trên tất cả thiết bị
            if (!dto.IsActive)
            {
                await _tokenService.RevokeAllUserTokensAsync(id);
            }

            // Cập nhật ngay cache trạng thái hoạt động để chặn access token hiện tại tức thì
            _memoryCache?.Set($"user_active_status_{id}", dto.IsActive, TimeSpan.FromSeconds(30));

            // Ghi nhận Audit Log
            _logger?.LogInformation(
                "[AUDIT] Admin {AdminUserId} đã {Action} tài khoản người dùng {TargetUserId} ({Email})",
                currentUserId, dto.IsActive ? "mở khóa" : "khóa", id, user.Email);

            if (_emailService != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                try
                {
                    await _emailService.SendAccountStatusNoticeAsync(
                        user.Email,
                        user.FullName ?? "Người dùng",
                        dto.IsActive
                    );
                }
                catch (Exception ex)
                {
                    // Lỗi gửi mail không chặn thao tác quản trị
                }
            }

            return Ok(new
            {
                message = dto.IsActive ? "Đã mở khóa tài khoản thành công." : "Đã khóa tài khoản và thu hồi toàn bộ phiên đăng nhập.",
                userId = id,
                isActive = dto.IsActive
            });
        }

        [HttpPost("seed-e2e-data")]
        public async Task<IActionResult> SeedE2eData()
        {
            // 1. Chỉ cho phép chạy khi môi trường Development hoặc cờ cấu hình E2ESeed:Enabled = true (mặc định false)
            bool isDev = _env?.IsDevelopment() ?? false;
            bool isSeedEnabled = bool.TryParse(_config?["E2ESeed:Enabled"], out var enabled) && enabled;
            if (!isDev && !isSeedEnabled)
            {
                return NotFound();
            }

            // 2. Bảo vệ dữ liệu thật: Chỉ cho phép seed vào cơ sở dữ liệu có tên kết thúc bằng "_e2e" hoặc "_test"
            var dbName = _context.Database?.DatabaseNamespace?.DatabaseName ?? string.Empty;
            if (!dbName.EndsWith("_e2e", StringComparison.OrdinalIgnoreCase) && !dbName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message = $"Từ chối seed dữ liệu: Cơ sở dữ liệu '{dbName}' không phải là cơ sở dữ liệu test (tên DB phải kết thúc bằng '_e2e' hoặc '_test')."
                });
            }

            // 1. REFUND_PENDING RefundRequest
            var existingRefund = await _context.RefundRequests.Find(r => r.BookingId == "BOOKING_E2E_01").FirstOrDefaultAsync();
            if (existingRefund == null)
            {
                await _context.RefundRequests.InsertOneAsync(new RefundRequest
                {
                    BookingId = "BOOKING_E2E_01",
                    OwnerUserId = "USER_E2E_01",
                    Amount = 150000,
                    Reason = "Khách hủy đơn trước 48h (E2E Test)",
                    Status = RefundStatus.REFUND_PENDING,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else if (existingRefund.Status != RefundStatus.REFUND_PENDING)
            {
                await _context.RefundRequests.UpdateOneAsync(
                    r => r.RefundId == existingRefund.RefundId,
                    Builders<RefundRequest>.Update.Set(r => r.Status, RefundStatus.REFUND_PENDING)
                );
            }

            // 2. DISPUTED Booking
            var existingBooking = await _context.Bookings.Find(b => b.BookingId == "BOOKING_E2E_DISPUTE_01").FirstOrDefaultAsync();
            if (existingBooking == null)
            {
                await _context.Bookings.InsertOneAsync(new Booking
                {
                    BookingId = "BOOKING_E2E_DISPUTE_01",
                    MaDatLich = "BK-E2E-01",
                    OwnerId = "USER_E2E_01",
                    SitterId = "SITTER_E2E_01",
                    TrangThai = BookingStatus.DISPUTED,
                    TongTien = 450000,
                    NgayBatDau = DateTime.UtcNow.AddDays(-3),
                    NgayKetThuc = DateTime.UtcNow.AddDays(-1),
                    DisputeReason = "Thú cưng bị trầy xước nhẹ (E2E Test)",
                    CreatedAt = DateTime.UtcNow.AddDays(-3)
                });
            }
            else if (existingBooking.TrangThai != BookingStatus.DISPUTED)
            {
                await _context.Bookings.UpdateOneAsync(
                    b => b.BookingId == existingBooking.BookingId,
                    Builders<Booking>.Update.Set(b => b.TrangThai, BookingStatus.DISPUTED)
                );
            }

            // 3. REQUESTED WithdrawalRequest
            var existingWithdrawal = await _context.WithdrawalRequests.Find(w => w.SitterUserId == "SITTER_E2E_01" && w.Status == "REQUESTED").FirstOrDefaultAsync();
            if (existingWithdrawal == null)
            {
                await _context.WithdrawalRequests.InsertOneAsync(new WithdrawalRequest
                {
                    SitterUserId = "SITTER_E2E_01",
                    Amount = 300000,
                    Status = "REQUESTED",
                    BankName = "MBBank",
                    BankBin = "970422",
                    MaskedAccountNumber = "****1234",
                    AccountHolderName = "NGUYEN VAN SITTER",
                    ReferenceId = "WDR-E2E-01",
                    IdempotencyKey = "IDEMP-E2E-01",
                    EncryptedBankSnapshot = "dummy_encrypted",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 4. SUSPENSE Payment (ABNORMAL)
            var existingPayment = await _context.Payments.Find(p => p.OrderCode == 99887766).FirstOrDefaultAsync();
            if (existingPayment == null)
            {
                await _context.Payments.InsertOneAsync(new Payment
                {
                    OrderCode = 99887766,
                    Amount = 250000,
                    ReceivedAmount = 300000,
                    Status = "ABNORMAL",
                    AbnormalReason = "Khách chuyển thừa tiền (E2E Test)",
                    CreatedAt = DateTime.UtcNow
                });
            }

            return Ok(new { message = "Đã seed 4 bản ghi test E2E thành công (REFUND_PENDING, DISPUTED, REQUESTED Withdrawal, ABNORMAL Payment)." });
        }
    }
}
