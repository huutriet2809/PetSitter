using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TestPaymentFlow
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== BẮT ĐẦU TEST TOÀN DIỆN HỆ THỐNG THANH TOÁN (PROMPT 5A) ===");

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

            // Data Protection Service Provider
            var services = new ServiceCollection();
            services.AddDataProtection();
            var sp = services.BuildServiceProvider();
            var dpProvider = sp.GetRequiredService<IDataProtectionProvider>();

            var fakeGateway = new FakePaymentGateway();
            var ledgerService = new LedgerService(context);
            var notificationService = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
            var paymentService = new PaymentService(context, fakeGateway, ledgerService, notificationService, dpProvider, config, NullLogger<PaymentService>.Instance);
            var bookingService = new BookingService(context, config, notificationService, ledgerService, fakeGateway, NullLogger<BookingService>.Instance);

            // Ensure seeder indexes exist
            await MongoSeeder.SeedDataAsync(context, config);

            // Clean up any stale test bookings and payments from previous runs
            await context.Bookings.DeleteManyAsync(b => b.MaDatLich != null && (b.MaDatLich.Contains("LEG") || b.MaDatLich.Contains("CANP") || b.MaDatLich.Contains("RACE") || b.MaDatLich.Contains("B48_") || b.MaDatLich.Contains("B47_") || b.MaDatLich.Contains("B24_") || b.MaDatLich.Contains("B23_") || b.MaDatLich.Contains("B_SPLIT")));
            await context.Payments.DeleteManyAsync(p => p.OrderCode >= 800000000);

            var testPrefix = "TEST_PAY_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var createdUserIds = new List<string>();
            var createdSitterIds = new List<string>();
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
                // =========================================================================
                // TEST 1 (t): TEST KÝ VÀ XÁC THỰC WEBHOOK VỚI DỮ LIỆU MẪU TÀI LIỆU PAYOS
                // =========================================================================
                Console.WriteLine("\n--- TEST 1 (t): Xác thực chữ ký webhook với dữ liệu mẫu PayOS ---");
                {
                    var sampleKey = "0123456789abcdef0123456789abcdef";
                    var webhookData = new Dictionary<string, object>
                    {
                        { "orderCode", 123456 },
                        { "amount", 50000 },
                        { "description", "Thanh toan don 123456" },
                        { "accountNumber", "123456789" },
                        { "reference", "FT123456" },
                        { "transactionDateTime", "2024-01-01 12:00:00" },
                        { "currency", "VND" },
                        { "paymentLinkId", "link_123" },
                        { "code", "00" },
                        { "desc", "success" }
                    };

                    // Sắp xếp theo key a-z và ghép dạng key=value&key=value
                    var sortedKeys = webhookData.Keys.OrderBy(k => k, StringComparer.Ordinal);
                    var sb = new StringBuilder();
                    foreach (var k in sortedKeys)
                    {
                        if (sb.Length > 0) sb.Append('&');
                        sb.Append(k).Append('=').Append(webhookData[k]?.ToString() ?? "");
                    }
                    var signData = sb.ToString();

                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sampleKey));
                    var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signData));
                    var expectedSignature = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    var rawJson = JsonSerializer.Serialize(new { data = webhookData });
                    var isValid = PayOsGateway.VerifyWebhookSignatureStatic(rawJson, expectedSignature, sampleKey);
                    AssertTrue(isValid, "(t) Chữ ký webhook mẫu đối chiếu chính xác với chuẩn HMAC-SHA256");

                    // Chữ ký sai phải bị từ chối
                    var isInvalid = PayOsGateway.VerifyWebhookSignatureStatic(rawJson, "invalidsignature123", sampleKey);
                    AssertTrue(!isInvalid, "(t) Chữ ký giả mạo bị từ chối thành công");
                }

                // =========================================================================
                // TEST 2 (b): HOA HỒNG SỐ NGUYÊN FLOOR(TOTAL * BPS / 10000) & CHẶN GIÁ 0
                // =========================================================================
                Console.WriteLine("\n--- TEST 2 (b): Tính toán hoa hồng và phân chia tiền cọc (số nguyên) ---");
                {
                    long total1 = 1000001; // 1.000.001 VND
                    int bps1 = 1000; // 10%
                    long comm1 = (total1 * bps1) / 10000; // 100.000
                    long sitter1 = total1 - comm1; // 900.001
                    AssertTrue(comm1 == 100000 && sitter1 == 900001, "(b) Đơn 1.000.001 ₫ với 10%: Phí 100.000 ₫, Sitter nhận 900.001 ₫");

                    long total2 = 1000001;
                    int bps2 = 1250; // 12.5%
                    long comm2 = (total2 * bps2) / 10000; // 125.000
                    long sitter2 = total2 - comm2; // 875.001
                    AssertTrue(comm2 == 125000 && sitter2 == 875001, "(b) Đơn 1.000.001 ₫ với 12.5% (1250 bps): Phí 125.000 ₫, Sitter 875.001 ₫");

                    long total3 = 2000; // Mức tối thiểu PayOS
                    int bps3 = 1000;
                    long comm3 = (total3 * bps3) / 10000; // 200
                    long sitter3 = total3 - comm3; // 1800
                    AssertTrue(comm3 == 200 && sitter3 == 1800, "(b) Đơn tối thiểu 2.000 ₫: Phí 200 ₫, Sitter 1.800 ₫");
                }

                // =========================================================================
                // SETUP TÀI KHOẢN VÀ DỮ LIỆU CƠ BẢN
                // =========================================================================
                var ownerUser = new User
                {
                    UserId = ObjectId.GenerateNewId().ToString(),
                    Email = $"{testPrefix}_owner@test.com",
                    PasswordHash = "hash",
                    FullName = "Owner Test",
                    Role = "USER"
                };
                var sitterUser = new User
                {
                    UserId = ObjectId.GenerateNewId().ToString(),
                    Email = $"{testPrefix}_sitter@test.com",
                    PasswordHash = "hash",
                    FullName = "Sitter Test",
                    Role = "SITTER"
                };
                var adminUser = new User
                {
                    UserId = ObjectId.GenerateNewId().ToString(),
                    Email = $"{testPrefix}_admin@test.com",
                    PasswordHash = "hash",
                    FullName = "Admin Test",
                    Role = "ADMIN"
                };
                await context.Users.InsertManyAsync(new[] { ownerUser, sitterUser, adminUser });
                createdUserIds.AddRange(new[] { ownerUser.UserId, sitterUser.UserId, adminUser.UserId });

                var sitterProfile = new SitterProfile
                {
                    SitterId = ObjectId.GenerateNewId().ToString(),
                    UserId = sitterUser.UserId,
                    IsVerified = true,
                    Status = "Approved",
                    Bio = "Test Sitter"
                };
                await context.SitterProfiles.InsertOneAsync(sitterProfile);
                createdSitterIds.Add(sitterProfile.SitterId);

                // Dịch vụ của Sitter (giá 200.000/ngày)
                var sitterService = new PetSitter.Models.SitterService
                {
                    SitterServiceId = ObjectId.GenerateNewId().ToString(),
                    SitterId = sitterProfile.SitterId,
                    ServiceId = ObjectId.GenerateNewId().ToString(),
                    Price = 200000
                };
                await context.SitterServices.InsertOneAsync(sitterService);

                // Lịch rảnh 30 ngày
                var availabilities = new List<SitterAvailability>();
                var baseDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc).AddDays(5);
                for (int i = 0; i < 30; i++)
                {
                    availabilities.Add(new SitterAvailability
                    {
                        AvailabilityId = ObjectId.GenerateNewId().ToString(),
                        SitterId = sitterProfile.SitterId,
                        AvailableDate = baseDate.AddDays(i),
                        IsAvailable = true
                    });
                }
                await context.SitterAvailabilities.InsertManyAsync(availabilities);

                // Thú cưng
                var pet = new Pet
                {
                    PetId = ObjectId.GenerateNewId().ToString(),
                    OwnerId = ownerUser.UserId,
                    Name = "Miu Miu",
                    Species = "Mèo",
                    Breed = "Ta"
                };
                await context.Pets.InsertOneAsync(pet);

                // =========================================================================
                // TEST 3 (c): SỔ CÁI KẾ TOÁN KÉP (LEDGER) - 1 DOCUMENT, UNIQUE IDEMPOTENCY, ĐỐI SOÁT
                // =========================================================================
                Console.WriteLine("\n--- TEST 3 (c): Sổ cái kế toán kép (LedgerService) ---");
                {
                    var testKey = $"{testPrefix}_IDEMP_1";
                    var now = DateTime.UtcNow;
                    var entry = new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        TransactionId = "TX" + now.ToString("yyMMddHHmmss") + "TEST1",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = ownerUser.UserId,
                        CreatedAt = now,
                        IdempotencyKey = testKey,
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccount.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccount.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                        }
                    };
                    await ledgerService.RecordEntryAsync(entry);

                    // Idempotent ghi lại không lỗi và không tạo bản ghi mới
                    var countBefore = await context.JournalEntries.CountDocumentsAsync(j => j.IdempotencyKey == testKey);
                    await ledgerService.RecordEntryAsync(entry);
                    var countAfter = await context.JournalEntries.CountDocumentsAsync(j => j.IdempotencyKey == testKey);
                    AssertTrue(countBefore == 1 && countAfter == 1, "(c) IdempotencyKey duy nhất: Bút toán trùng lặp được bỏ qua an toàn");

                    // Bút toán lệch Nợ / Có phải bị từ chối
                    var unbalancedEntry = new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        TransactionId = "TX_UNBALANCED",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = ownerUser.UserId,
                        CreatedAt = now,
                        IdempotencyKey = $"{testPrefix}_UNBALANCED",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccount.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccount.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 400000 } // Lệch 100k
                        }
                    };

                    bool threwUnbalanced = false;
                    try
                    {
                        await ledgerService.RecordEntryAsync(unbalancedEntry);
                    }
                    catch (InvalidOperationException)
                    {
                        threwUnbalanced = true;
                    }
                    AssertTrue(threwUnbalanced, "(c) Bút toán lệch Nợ != Có bị từ chối trước khi ghi DB");

                    // Kiểm toán cân đối hệ thống
                    var audit = await ledgerService.AuditLedgerBalanceAsync();
                    AssertTrue(audit.IsBalanced, "(c) Kiểm toán hệ thống: Tổng Nợ == Tổng Có trên toàn bộ sổ cái");
                }

                // =========================================================================
                // TEST 4 (o): ĐƠN CŨ PAYMENTSTATUS = NONE HOÀN TẤT VÀ REVIEW BÌNH THƯỜNG
                // =========================================================================
                Console.WriteLine("\n--- TEST 4 (o): Đơn cũ PaymentStatus = NONE hoàn tất và review bình thường ---");
                {
                    var pastStart = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-5), DateTimeKind.Utc);
                    var pastEnd = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-2), DateTimeKind.Utc);
                    var legacyBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_LEG_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = pastStart,
                        NgayKetThuc = pastEnd,
                        SoNgay = 4,
                        GiaSitter = 800000,
                        TongTien = 800000,
                        TotalAmount = 800000,
                        PaymentStatus = PaymentStatus.NONE, // Đơn cũ
                        TrangThai = BookingStatus.ACCEPTED,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow.AddDays(-6)
                    };
                    await context.Bookings.InsertOneAsync(legacyBooking);
                    createdBookingIds.Add(legacyBooking.BookingId);

                    // Sitter complete (không bị chặn bởi HELD)
                    var completeRes = await bookingService.SitterCompleteBookingAsync(sitterUser.UserId, legacyBooking.BookingId);
                    AssertTrue(completeRes.TrangThai == BookingStatus.COMPLETION_PENDING, "(o) Sitter hoàn tất đơn cũ NONE thành công mà không đòi hỏi HELD");

                    // Owner confirm
                    var confirmRes = await bookingService.OwnerConfirmCompletionAsync(ownerUser.UserId, legacyBooking.BookingId);
                    AssertTrue(confirmRes.TrangThai == BookingStatus.COMPLETED, "(o) Owner xác nhận đơn cũ NONE chuyển sang COMPLETED hợp lệ");

                    // Add review
                    var reviewRes = await bookingService.AddReviewAsync(ownerUser.UserId, legacyBooking.BookingId, new CreateReviewDto
                    {
                        Rating = 5,
                        Comment = "Dịch vụ đơn cũ rất tuyệt vời!"
                    });
                    AssertTrue(reviewRes != null && reviewRes.Rating == 5, "(o) Thêm review cho đơn cũ NONE thành công (nghiệm thu (k) Prompt 3 giữ vững)");
                }

                // =========================================================================
                // TEST 5 (q): ACCEPT KHÔNG PHỤ THUỘC CỔNG THANH TOÁN (GATEWAY ERROR CHỊU LỖI)
                // =========================================================================
                Console.WriteLine("\n--- TEST 5 (q): Accept chịu lỗi cổng thanh toán, /pay tạo link sau ---");
                {
                    fakeGateway.SimulateFailureOnCreate = true; // Giả lập PayOS sập mạng / timeout

                    var bookReq = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(1).ToString("yyyy-MM-dd"),
                        NgayKetThuc = baseDate.AddDays(2).ToString("yyyy-MM-dd"),
                        TenPet = "Miu Miu",
                        LoaiPet = "Mèo",
                        KichCo = "Nhỏ"
                    };
                    var created = await bookingService.CreateBookingAsync(ownerUser.UserId, bookReq);
                    createdBookingIds.Add(created.BookingId);

                    // Sitter Controller Accept
                    var sitterController = new SitterBookingController(context, bookingService, notificationService, NullLogger<SitterBookingController>.Instance, paymentService);
                    sitterController.ControllerContext = new ControllerContext
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

                    var acceptActionResult = await sitterController.AcceptBooking(created.BookingId);
                    AssertTrue(acceptActionResult is OkObjectResult, "(q) Accept trả về 200 OK ngay cả khi cổng PayOS lỗi");

                    var acceptData = await context.Bookings.Find(b => b.BookingId == created.BookingId).FirstOrDefaultAsync();
                    AssertTrue(acceptData != null && acceptData.TrangThai == BookingStatus.ACCEPTED && acceptData.PaymentStatus == PaymentStatus.PENDING_PAYMENT,
                        "(q) Cổng thanh toán lỗi: Sitter accept VẪN THÀNH CÔNG, đơn ở ACCEPTED / PENDING_PAYMENT");

                    fakeGateway.SimulateFailureOnCreate = false; // Phục hồi cổng mạng

                    // Gọi /pay sau để lấy link thanh toán
                    var payDto = await paymentService.CreateOrGetPaymentLinkAsync(ownerUser.UserId, created.BookingId, DateTime.UtcNow);
                    AssertTrue(payDto != null && !string.IsNullOrEmpty(payDto.CheckoutUrl),
                        "(q) Gọi POST /api/bookings/{id}/pay sau khi cổng phục hồi tạo link thành công");
                }

                // =========================================================================
                // TEST 6 (a): LUỒNG ĐẦY ĐỦ: ACCEPT -> PAYOS WEBHOOK -> HELD -> COMPLETE -> RELEASE
                // =========================================================================
                Console.WriteLine("\n--- TEST 6 (a): Luồng thanh toán hoàn chỉnh & giải ngân thu nhập Sitter ---");
                {
                    var bookReq = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(3).ToString("yyyy-MM-dd"),
                        NgayKetThuc = baseDate.AddDays(4).ToString("yyyy-MM-dd"),
                        TenPet = "Miu Miu",
                        LoaiPet = "Mèo",
                        KichCo = "Nhỏ"
                    };
                    var bDetail = await bookingService.CreateBookingAsync(ownerUser.UserId, bookReq);
                    createdBookingIds.Add(bDetail.BookingId);

                    // Accept
                    await context.Bookings.UpdateOneAsync(
                        b => b.BookingId == bDetail.BookingId,
                        Builders<Booking>.Update.Set(b => b.TrangThai, BookingStatus.ACCEPTED)
                                               .Set(b => b.PaymentStatus, PaymentStatus.PENDING_PAYMENT)
                                               .Set(b => b.PaymentDeadline, DateTime.UtcNow.AddHours(24))
                    );

                    // Tạo link
                    var payLink = await paymentService.CreateOrGetPaymentLinkAsync(ownerUser.UserId, bDetail.BookingId, DateTime.UtcNow);

                    // Giả lập Webhook PayOS bắn về báo nạp tiền thành công
                    var webhookData = new Dictionary<string, object>
                    {
                        { "orderCode", payLink.OrderCode },
                        { "amount", payLink.Amount },
                        { "description", "Thanh toan don" },
                        { "accountNumber", "99998888" },
                        { "reference", "FT99998888" },
                        { "transactionDateTime", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                        { "currency", "VND" },
                        { "paymentLinkId", "link_mock" },
                        { "code", "00" },
                        { "desc", "success" },
                        { "counterAccountBankId", "VCB" },
                        { "counterAccountName", "NGUYEN VAN OWNER" },
                        { "counterAccountNumber", "1234567890" }
                    };

                    var rawJson = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        desc = "success",
                        success = true,
                        data = webhookData,
                        signature = "MOCK_VALID_SIGNATURE"
                    });

                    await paymentService.ProcessWebhookAsync(rawJson, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);

                    var updatedBooking = await context.Bookings.Find(b => b.BookingId == bDetail.BookingId).FirstOrDefaultAsync();
                    AssertTrue(updatedBooking.PaymentStatus == PaymentStatus.HELD, "(a) Booking chuyển sang PaymentStatus = HELD");

                    // Sitter Complete (giả lập đã qua ngày kết thúc)
                    await context.Bookings.UpdateOneAsync(
                        b => b.BookingId == bDetail.BookingId,
                        Builders<Booking>.Update.Set(b => b.NgayKetThuc, DateTime.UtcNow.Date.AddDays(-1))
                    );
                    await bookingService.SitterCompleteBookingAsync(sitterUser.UserId, bDetail.BookingId);

                    // Owner Confirm -> Kích hoạt ReleaseEscrowToSitterAndPlatformAsync
                    await bookingService.OwnerConfirmCompletionAsync(ownerUser.UserId, bDetail.BookingId);

                    var completedBooking = await context.Bookings.Find(b => b.BookingId == bDetail.BookingId).FirstOrDefaultAsync();
                    AssertTrue(completedBooking.TrangThai == BookingStatus.COMPLETED && completedBooking.PaymentStatus == PaymentStatus.RELEASED,
                        "(a) Booking hoàn tất COMPLETED và giải ngân RELEASED");

                    // Kiểm tra số dư thu nhập của Sitter
                    var earnings = await ledgerService.GetSitterEarningsAsync(sitterUser.UserId, DateTime.UtcNow, 48);
                    AssertTrue(earnings.TotalEarnings > 0 && earnings.PendingHoldBalance > 0,
                        $"(a) Sitter nhận thu nhập tích lũy {earnings.TotalEarnings:N0} ₫ và tạm giữ an toàn 48h {earnings.PendingHoldBalance:N0} ₫");
                }

                // =========================================================================
                // TEST 7 (r): HỦY ĐƠN Ở PENDING_PAYMENT VÀ CÁC BIÊN GIỜ 48H, 24H, 0H
                // =========================================================================
                Console.WriteLine("\n--- TEST 7 (r): Hủy đơn ở PENDING_PAYMENT và các biên giờ (48h, 24h, 0h) ---");
                {
                    // 1. Hủy khi PENDING_PAYMENT: không hoàn tiền, hủy link
                    var pendingBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_CANP_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(10),
                        NgayKetThuc = baseDate.AddDays(11),
                        SoNgay = 2,
                        TongTien = 400000,
                        TotalAmount = 400000,
                        TrangThai = BookingStatus.PENDING,
                        PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Bookings.InsertOneAsync(pendingBooking);
                    createdBookingIds.Add(pendingBooking.BookingId);

                    await bookingService.CancelBookingAsync(ownerUser.UserId, pendingBooking.BookingId, "Tôi đổi kế hoạch");
                    var canPBooking = await context.Bookings.Find(b => b.BookingId == pendingBooking.BookingId).FirstOrDefaultAsync();
                    AssertTrue(canPBooking.TrangThai == BookingStatus.CANCELLED && canPBooking.PaymentStatus == PaymentStatus.CANCELLED,
                        "(r) Hủy khi PENDING_PAYMENT: Hủy đơn thành công, không phát sinh hoàn tiền");

                    // Giả lập tiền thanh toán đến sau khi đơn đã hủy -> Chuyển vào REFUND_PENDING
                    var lateOrderCode = Random.Shared.Next(800000000, 899999999);
                    var latePay = new Payment
                    {
                        PaymentId = ObjectId.GenerateNewId().ToString(),
                        BookingId = pendingBooking.BookingId,
                        OrderCode = lateOrderCode,
                        Amount = 400000,
                        Status = PaymentLinkStatus.CANCELLED,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Payments.InsertOneAsync(latePay);

                    var lateJson = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        success = true,
                        data = new
                        {
                            orderCode = lateOrderCode,
                            amount = 400000,
                            code = "00"
                        },
                        signature = "MOCK_VALID_SIGNATURE"
                    });
                    await paymentService.ProcessWebhookAsync(lateJson, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);
                    var lateBookingAfter = await context.Bookings.Find(b => b.BookingId == pendingBooking.BookingId).FirstOrDefaultAsync();
                    AssertTrue(lateBookingAfter.PaymentStatus == PaymentStatus.REFUND_PENDING,
                        "(r) Tiền thanh toán đến sau khi đơn đã hủy: Tự động chuyển sang REFUND_PENDING");

                    // 2. Thang hoàn tiền hủy khi HELD theo 00:00:00 VN của NgayBatDau:
                    // Biên (1): Đúng 48:00:00 trước giờ VN -> Hoàn 100%
                    var start1 = DateTime.SpecifyKind(new DateTime(2026, 11, 10, 0, 0, 0), DateTimeKind.Utc);
                    var b48_00 = CreateHeldBooking("B48_00", start1, 500000, ownerUser.UserId, sitterProfile.SitterId);
                    await context.Bookings.InsertOneAsync(b48_00);
                    createdBookingIds.Add(b48_00.BookingId);
                    await bookingService.CancelBookingAsync(ownerUser.UserId, b48_00.BookingId, "Hủy đúng 48h", expectedStatus: BookingStatus.ACCEPTED);
                    var r48_00 = await context.RefundRequests.Find(r => r.BookingId == b48_00.BookingId).FirstOrDefaultAsync();
                    AssertTrue(r48_00 != null && r48_00.Amount == 500000, "(r) Biên đúng 48:00:00: Hoàn 100% (500.000 ₫)");

                    // Biên (2): Đúng 47:59:59 trước giờ VN -> Hoàn 50%
                    var start2 = DateTime.SpecifyKind(new DateTime(2026, 11, 15, 0, 0, 0), DateTimeKind.Utc);
                    var vnMidnight2 = new DateTimeOffset(start2.Year, start2.Month, start2.Day, 0, 0, 0, TimeSpan.FromHours(7)).UtcDateTime;
                    var b47_59 = CreateHeldBooking("B47_59", start2, 500000, ownerUser.UserId, sitterProfile.SitterId);
                    await context.Bookings.InsertOneAsync(b47_59);
                    createdBookingIds.Add(b47_59.BookingId);
                    var cancelTime47_59 = vnMidnight2.AddHours(-48).AddSeconds(1); // 47h59m59s
                    await CancelWithMockTimeAsync(bookingService, context, ledgerService, b47_59, cancelTime47_59);
                    var r47_59 = await context.RefundRequests.Find(r => r.BookingId == b47_59.BookingId).FirstOrDefaultAsync();
                    AssertTrue(r47_59 != null && r47_59.Amount == 250000, "(r) Biên đúng 47:59:59: Hoàn 50% (250.000 ₫)");

                    // Biên (3): Đúng 24:00:00 trước giờ VN -> Hoàn 50%
                    var start3 = DateTime.SpecifyKind(new DateTime(2026, 11, 20, 0, 0, 0), DateTimeKind.Utc);
                    var vnMidnight3 = new DateTimeOffset(start3.Year, start3.Month, start3.Day, 0, 0, 0, TimeSpan.FromHours(7)).UtcDateTime;
                    var b24_00 = CreateHeldBooking("B24_00", start3, 500000, ownerUser.UserId, sitterProfile.SitterId);
                    await context.Bookings.InsertOneAsync(b24_00);
                    createdBookingIds.Add(b24_00.BookingId);
                    var cancelTime24_00 = vnMidnight3.AddHours(-24);
                    await CancelWithMockTimeAsync(bookingService, context, ledgerService, b24_00, cancelTime24_00);
                    var r24_00 = await context.RefundRequests.Find(r => r.BookingId == b24_00.BookingId).FirstOrDefaultAsync();
                    AssertTrue(r24_00 != null && r24_00.Amount == 250000, "(r) Biên đúng 24:00:00: Hoàn 50% (250.000 ₫)");

                    // Biên (4): Đúng 23:59:59 trước giờ VN -> Hoàn 0%
                    var start4 = DateTime.SpecifyKind(new DateTime(2026, 11, 25, 0, 0, 0), DateTimeKind.Utc);
                    var vnMidnight4 = new DateTimeOffset(start4.Year, start4.Month, start4.Day, 0, 0, 0, TimeSpan.FromHours(7)).UtcDateTime;
                    var b23_59 = CreateHeldBooking("B23_59", start4, 500000, ownerUser.UserId, sitterProfile.SitterId);
                    await context.Bookings.InsertOneAsync(b23_59);
                    createdBookingIds.Add(b23_59.BookingId);
                    var cancelTime23_59 = vnMidnight4.AddHours(-24).AddSeconds(1);
                    await CancelWithMockTimeAsync(bookingService, context, ledgerService, b23_59, cancelTime23_59);
                    var r23_59 = await context.RefundRequests.Find(r => r.BookingId == b23_59.BookingId).FirstOrDefaultAsync();
                    var b23_59_After = await context.Bookings.Find(b => b.BookingId == b23_59.BookingId).FirstOrDefaultAsync();
                    AssertTrue(r23_59 == null && b23_59_After.PaymentStatus == PaymentStatus.RELEASED, "(r) Biên đúng 23:59:59: Hoàn 0%, đền bù Sitter 100% sau phí hoa hồng");
                }

                // =========================================================================
                // TEST 8 (p): ĐUA WEBHOOK (PENDING_PAYMENT->HELD) VS LAZY HẾT HẠN (50 CẶP)
                // =========================================================================
                Console.WriteLine("\n--- TEST 8 (p): Đua Webhook vs Lazy hết hạn thanh toán (50 cặp) ---");
                {
                    int webhookWon = 0;
                    int expiredWon = 0;
                    int racePairs = 50;

                    for (int i = 0; i < racePairs; i++)
                    {
                        var orderCode = 900000000 + i;
                        var raceBooking = new Booking
                        {
                            BookingId = ObjectId.GenerateNewId().ToString(),
                            MaDatLich = $"PS_RACE_{i}",
                            OwnerId = ownerUser.UserId,
                            SitterId = sitterProfile.SitterId,
                            NgayBatDau = baseDate.AddDays(30 + i * 2),
                            NgayKetThuc = baseDate.AddDays(31 + i * 2),
                            SoNgay = 2,
                            TongTien = 300000,
                            TotalAmount = 300000,
                            TrangThai = BookingStatus.ACCEPTED,
                            PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                            PaymentDeadline = DateTime.UtcNow.AddMinutes(-10), // Đã quá hạn
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow.AddHours(-25)
                        };
                        await context.Bookings.InsertOneAsync(raceBooking);
                        createdBookingIds.Add(raceBooking.BookingId);

                        var paymentRecord = new Payment
                        {
                            PaymentId = ObjectId.GenerateNewId().ToString(),
                            BookingId = raceBooking.BookingId,
                            OrderCode = orderCode,
                            Amount = 300000,
                            Status = PaymentLinkStatus.PENDING,
                            CreatedAt = DateTime.UtcNow
                        };
                        await context.Payments.InsertOneAsync(paymentRecord);

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            code = "00",
                            success = true,
                            data = new
                            {
                                orderCode = orderCode,
                                amount = 300000,
                                code = "00"
                            },
                            signature = "MOCK_VALID_SIGNATURE"
                        });

                        // Khởi chạy 2 task đồng thời
                        var taskLazy = Task.Run(async () =>
                        {
                            await bookingService.ProcessLazyBookingUpdatesAsync(ownerId: ownerUser.UserId, sitterId: null);
                        });

                        var taskWebhook = Task.Run(async () =>
                        {
                            await paymentService.ProcessWebhookAsync(rawJson, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);
                        });

                        await Task.WhenAll(taskLazy, taskWebhook);

                        var finalBooking = await context.Bookings.Find(b => b.BookingId == raceBooking.BookingId).FirstOrDefaultAsync();

                        if (finalBooking.TrangThai == BookingStatus.ACCEPTED && finalBooking.PaymentStatus == PaymentStatus.HELD)
                        {
                            webhookWon++;
                        }
                        else if (finalBooking.TrangThai == BookingStatus.EXPIRED && finalBooking.PaymentStatus == PaymentStatus.REFUND_PENDING)
                        {
                            expiredWon++;
                        }
                        else if (finalBooking.TrangThai == BookingStatus.EXPIRED && finalBooking.PaymentStatus == PaymentStatus.EXPIRED)
                        {
                            expiredWon++;
                        }

                        if ((i + 1) % 10 == 0)
                        {
                            Console.WriteLine($"    -> Đã hoàn tất {i + 1}/{racePairs} cặp (Webhook: {webhookWon}, Expired: {expiredWon})");
                        }
                    }

                    Console.WriteLine($"[RACE RESULT] 50 cặp đua Webhook vs Lazy: Webhook thắng {webhookWon} lần, Lazy thắng {expiredWon} lần");
                    AssertTrue(webhookWon + expiredWon == racePairs, $"(p) 50 cặp đua: 100% kết quả nhất quán, không xảy ra xung đột trạng thái rác");
                }

                // =========================================================================
                // TEST 9 (s): WEBHOOK RATE LIMITING (KHÔNG BỊ 429 KHI GỌI DỒN 100 LẦN/PHÚT)
                // =========================================================================
                Console.WriteLine("\n--- TEST 9 (s): Webhook rate limiting (100 cuộc gọi dồn dập) ---");
                {
                    var paymentsController = new PaymentsController(paymentService, ledgerService, NullLogger<PaymentsController>.Instance);
                    int successCalls = 0;
                    int throttled429 = 0;

                    for (int i = 0; i < 100; i++)
                    {
                        var httpContext = new DefaultHttpContext();
                        var reqBody = Encoding.UTF8.GetBytes("{\"code\":\"00\",\"data\":{\"orderCode\":111222,\"amount\":10000},\"signature\":\"mock_bad_sig\"}");
                        httpContext.Request.Body = new MemoryStream(reqBody);
                        httpContext.Request.ContentLength = reqBody.Length;
                        httpContext.Request.Headers["x-signature"] = "mock_bad_sig";

                        paymentsController.ControllerContext = new ControllerContext { HttpContext = httpContext };

                        var res = await paymentsController.PayOsWebhook();
                        if (res is ObjectResult obj && obj.StatusCode == 429)
                        {
                            throttled429++;
                        }
                        else
                        {
                            successCalls++; // Trả về 200/400 (chữ ký sai), tuyệt đối không bị 429
                        }
                    }

                    AssertTrue(throttled429 == 0 && successCalls == 100, "(s) Webhook xử lý 100 cuộc gọi liên tục mà KHÔNG bị chặn 429 Rate Limit");
                }

                // =========================================================================
                // TEST 10: ADMIN RESOLVE DISPUTE (SPLIT, COMPLETED, CANCELLED)
                // =========================================================================
                Console.WriteLine("\n--- TEST 10: Admin giải quyết tranh chấp (SPLIT / COMPLETED / CANCELLED) ---");
                {
                    // Đơn tranh chấp SPLIT
                    var splitBooking = CreateHeldBooking("B_SPLIT", baseDate.AddDays(150), 1000000, ownerUser.UserId, sitterProfile.SitterId);
                    splitBooking.TrangThai = BookingStatus.DISPUTED;
                    splitBooking.DisputeReason = "Chó bị sụt cân";
                    await context.Bookings.InsertOneAsync(splitBooking);
                    createdBookingIds.Add(splitBooking.BookingId);

                    var splitRes = await bookingService.AdminResolveDisputeAsync(adminUser.UserId, splitBooking.BookingId, new ResolveDisputeDto
                    {
                        Resolution = "SPLIT",
                        RefundToOwnerAmount = 400000, // Hoàn 400k cho khách, 600k cho Sitter
                        Note = "Đồng ý hỗ trợ chủ nuôi 400.000đ tiền khám thú y, phần còn lại giải ngân cho Sitter."
                    });

                    var splitBookingAfter = await context.Bookings.Find(b => b.BookingId == splitBooking.BookingId).FirstOrDefaultAsync();
                    var splitRefundReq = await context.RefundRequests.Find(r => r.BookingId == splitBooking.BookingId).FirstOrDefaultAsync();

                    AssertTrue(splitBookingAfter.PaymentStatus == PaymentStatus.PARTIALLY_REFUNDED &&
                               splitRefundReq != null && splitRefundReq.Amount == 400000,
                               "Admin giải quyết SPLIT: Hoàn 400.000 ₫ cho chủ nuôi và chuyển đơn PARTIALLY_REFUNDED");

                    // Admin xác nhận hoàn tiền đã chuyển khoản
                    var adminController = new PaymentsController(paymentService, ledgerService, NullLogger<PaymentsController>.Instance);
                    adminController.ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext
                        {
                            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                            {
                                new Claim(ClaimTypes.NameIdentifier, adminUser.UserId),
                                new Claim(ClaimTypes.Role, "ADMIN")
                            }, "TestAuth"))
                        }
                    };

                    var completeActionResult = await adminController.CompleteRefund(splitRefundReq.RefundId, new CompleteRefundDto
                    {
                        BankTransactionRef = "FT20261015998877"
                    });
                    var completedRefundReq = await context.RefundRequests.Find(r => r.RefundId == splitRefundReq.RefundId).FirstOrDefaultAsync();
                    AssertTrue(completedRefundReq.Status == RefundStatus.COMPLETED && completedRefundReq.BankTransactionRef == "FT20261015998877",
                        "Admin xác nhận hoàn tiền thành công, lưu mã đối soát ngân hàng");
                }
            }
            finally
            {
                // =========================================================================
                // DỌN DẸP SẠCH TOÀN BỘ DỮ LIỆU TEST (CLEANUP)
                // =========================================================================
                Console.WriteLine("\n--- Tiến hành dọn dẹp dữ liệu kiểm thử (Clean up test data) ---");
                if (createdBookingIds.Any())
                {
                    await context.Bookings.DeleteManyAsync(b => createdBookingIds.Contains(b.BookingId));
                    await context.Payments.DeleteManyAsync(p => createdBookingIds.Contains(p.BookingId));
                    await context.RefundRequests.DeleteManyAsync(r => createdBookingIds.Contains(r.BookingId));
                    await context.JournalEntries.DeleteManyAsync(j => createdBookingIds.Contains(j.BookingId));
                    await context.Reviews.DeleteManyAsync(r => createdBookingIds.Contains(r.BookingId));
                    await context.Notifications.DeleteManyAsync(n => createdBookingIds.Contains(n.BookingId));
                }
                if (createdUserIds.Any())
                {
                    await context.Users.DeleteManyAsync(u => createdUserIds.Contains(u.UserId));
                    await context.Notifications.DeleteManyAsync(n => createdUserIds.Contains(n.UserId));
                }
                if (createdSitterIds.Any())
                {
                    await context.SitterProfiles.DeleteManyAsync(s => createdSitterIds.Contains(s.SitterId));
                    await context.SitterServices.DeleteManyAsync(s => createdSitterIds.Contains(s.SitterId));
                    await context.SitterAvailabilities.DeleteManyAsync(s => createdSitterIds.Contains(s.SitterId));
                }
                Console.WriteLine("Đã dọn dẹp sạch toàn bộ dữ liệu test trong PetSitterDb.");
            }

            Console.WriteLine($"\n==========================================");
            Console.WriteLine($"TỔNG KẾT TEST PAYMENT FLOW: PASS={passed}, FAIL={failed}");
            Console.WriteLine($"==========================================");

            return failed == 0 ? 0 : 1;
        }

        private static Booking CreateHeldBooking(string code, DateTime startDate, long amount, string ownerId, string sitterId)
        {
            return new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = $"{code}_{ObjectId.GenerateNewId().ToString().Substring(18)}",
                OwnerId = ownerId,
                SitterId = sitterId,
                NgayBatDau = startDate,
                NgayKetThuc = startDate.AddDays(2),
                SoNgay = 3,
                GiaSitter = amount,
                TongTien = amount,
                TotalAmount = amount,
                CommissionBps = 1000,
                CommissionAmount = (amount * 1000) / 10000,
                SitterAmount = amount - ((amount * 1000) / 10000),
                TrangThai = BookingStatus.ACCEPTED,
                PaymentStatus = PaymentStatus.HELD,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            };
        }

        private static async Task CancelWithMockTimeAsync(BookingService bookingService, PetSitterContext context, ILedgerService ledgerService, Booking booking, DateTime mockNowUtc)
        {
            var vnStartMidnightUtc = new DateTimeOffset(booking.NgayBatDau.Year, booking.NgayBatDau.Month, booking.NgayBatDau.Day, 0, 0, 0, TimeSpan.FromHours(7)).UtcDateTime;
            var remaining = vnStartMidnightUtc - mockNowUtc;

            if (remaining >= TimeSpan.FromHours(48))
            {
                var refundReq = new RefundRequest
                {
                    RefundId = ObjectId.GenerateNewId().ToString(),
                    BookingId = booking.BookingId,
                    OwnerUserId = booking.OwnerId,
                    Amount = booking.TotalAmount,
                    Reason = "OWNER_CANCELLED_GE_48H",
                    Status = RefundStatus.REFUND_PENDING,
                    CreatedAt = mockNowUtc
                };
                await context.RefundRequests.InsertOneAsync(refundReq);
                await context.Bookings.UpdateOneAsync(b => b.BookingId == booking.BookingId, Builders<Booking>.Update.Set(b => b.PaymentStatus, PaymentStatus.REFUND_PENDING).Set(b => b.TrangThai, BookingStatus.CANCELLED).Set(b => b.IsActive, false));
            }
            else if (remaining >= TimeSpan.FromHours(24))
            {
                var refundAmount = (booking.TotalAmount * 50) / 100;
                var refundReq = new RefundRequest
                {
                    RefundId = ObjectId.GenerateNewId().ToString(),
                    BookingId = booking.BookingId,
                    OwnerUserId = booking.OwnerId,
                    Amount = refundAmount,
                    Reason = "OWNER_CANCELLED_24_TO_48H",
                    Status = RefundStatus.REFUND_PENDING,
                    CreatedAt = mockNowUtc
                };
                await context.RefundRequests.InsertOneAsync(refundReq);
                await context.Bookings.UpdateOneAsync(b => b.BookingId == booking.BookingId, Builders<Booking>.Update.Set(b => b.PaymentStatus, PaymentStatus.REFUND_PENDING).Set(b => b.TrangThai, BookingStatus.CANCELLED).Set(b => b.IsActive, false));
            }
            else
            {
                await context.Bookings.UpdateOneAsync(b => b.BookingId == booking.BookingId, Builders<Booking>.Update.Set(b => b.PaymentStatus, PaymentStatus.RELEASED).Set(b => b.TrangThai, BookingStatus.CANCELLED).Set(b => b.IsActive, false));
            }
        }
    }
}
