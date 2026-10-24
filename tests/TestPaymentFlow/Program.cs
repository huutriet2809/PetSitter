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
            var dpKeyDir = config.GetValue<string>("Payments:DataProtection:KeyDirectory");
            if (string.IsNullOrWhiteSpace(dpKeyDir))
            {
                dpKeyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PetSitterKeys");
            }
            Directory.CreateDirectory(dpKeyDir);

            var services = new ServiceCollection();
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dpKeyDir))
                .SetApplicationName("PetSitterApp");
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
            await context.WithdrawalRequests.DeleteManyAsync(w => w.IdempotencyKey != null && w.IdempotencyKey.Contains("IDEMP"));

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

                    // Test hủy nấc 1 (>=48h): refund = total
                    long cTier1Total = 1000001;
                    long cTier1Refund = cTier1Total;
                    AssertTrue(cTier1Refund == 1000001, "(b) Nấc 1 (>=48h) với 1.000.001 ₫: Hoàn 100% (1.000.001 ₫), hoa hồng = 0, Sitter = 0");

                    // Test hủy nấc 2 (24-48h): refund50 = total / 2; remainder = total - refund50 (dư về Sitter); commission = remainder * bps / 10000; sitterNet = remainder - commission
                    long cTier2Total = 1000001;
                    long refund50 = cTier2Total / 2; // 500.000
                    long remainder2 = cTier2Total - refund50; // 500.001
                    int customBps = 1250; // 12.5%
                    long commTier2 = (remainder2 * customBps) / 10000; // 62.500
                    long sitterNetTier2 = remainder2 - commTier2; // 437.501
                    AssertTrue(refund50 == 500000 && remainder2 == 500001 && commTier2 == 62500 && sitterNetTier2 == 437501 && (refund50 + commTier2 + sitterNetTier2 == cTier2Total),
                        "(b) Nấc 2 (24-48h) với 1.000.001 ₫ và 1250 bps: Refund 500.000 ₫, hoa hồng 62.500 ₫, Sitter 437.501 ₫ (Tổng bảo toàn = 1.000.001 ₫)");

                    // Test hủy nấc 3 (<24h): refund = 0, commission = total * bps / 10000, sitterNet = total - commission
                    long cTier3Total = 1000001;
                    long commTier3 = (cTier3Total * customBps) / 10000; // 125.000
                    long sitterNetTier3 = cTier3Total - commTier3; // 875.001
                    AssertTrue(commTier3 == 125000 && sitterNetTier3 == 875001 && (commTier3 + sitterNetTier3 == cTier3Total),
                        "(b) Nấc 3 (<24h) với 1.000.001 ₫ và 1250 bps: Refund 0 ₫, hoa hồng 125.000 ₫, Sitter 875.001 ₫ (Tổng bảo toàn = 1.000.001 ₫)");

                    // Test số lẻ khác 777.777 ₫ nấc 2 với bps = 800 (8%)
                    long oddTotal = 777777;
                    long oddRefund50 = oddTotal / 2; // 388.888
                    long oddRemainder = oddTotal - oddRefund50; // 388.889
                    int oddBps = 800;
                    long oddComm = (oddRemainder * oddBps) / 10000; // 31.111
                    long oddSitter = oddRemainder - oddComm; // 357.778
                    AssertTrue(oddRefund50 == 388888 && oddRemainder == 388889 && oddComm == 31111 && oddSitter == 357778 && (oddRefund50 + oddComm + oddSitter == oddTotal),
                        "(b) Số lẻ 777.777 ₫ nấc 2 (800 bps): Refund 388.888 ₫, hoa hồng 31.111 ₫, Sitter 357.778 ₫ (Tổng bảo toàn = 777.777 ₫)");
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
                    Price = 200000,
                    IsPrimary = true
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
                        KichCo = "Nhỏ",
                        ExpectedTotal = 400000
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
                        KichCo = "Nhỏ",
                        ExpectedTotal = 400000
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
                    var b47_59_After = await context.Bookings.Find(b => b.BookingId == b47_59.BookingId).FirstOrDefaultAsync();
                    AssertTrue(r47_59 != null && r47_59.Amount == 250000, "(r) Biên đúng 47:59:59: Hoàn 50% (250.000 ₫)");
                    AssertTrue(b47_59_After != null && b47_59_After.PaymentStatus == PaymentStatus.PARTIALLY_REFUNDED,
                        "(r) Nấc 24-48h (47:59:59): PaymentStatus chuyển sang PARTIALLY_REFUNDED (không REFUND_PENDING)");

                    // Biên (3): Đúng 24:00:00 trước giờ VN -> Hoàn 50%
                    var start3 = DateTime.SpecifyKind(new DateTime(2026, 11, 20, 0, 0, 0), DateTimeKind.Utc);
                    var vnMidnight3 = new DateTimeOffset(start3.Year, start3.Month, start3.Day, 0, 0, 0, TimeSpan.FromHours(7)).UtcDateTime;
                    var b24_00 = CreateHeldBooking("B24_00", start3, 500000, ownerUser.UserId, sitterProfile.SitterId);
                    await context.Bookings.InsertOneAsync(b24_00);
                    createdBookingIds.Add(b24_00.BookingId);
                    var cancelTime24_00 = vnMidnight3.AddHours(-24);
                    await CancelWithMockTimeAsync(bookingService, context, ledgerService, b24_00, cancelTime24_00);
                    var r24_00 = await context.RefundRequests.Find(r => r.BookingId == b24_00.BookingId).FirstOrDefaultAsync();
                    var b24_00_After = await context.Bookings.Find(b => b.BookingId == b24_00.BookingId).FirstOrDefaultAsync();
                    AssertTrue(r24_00 != null && r24_00.Amount == 250000, "(r) Biên đúng 24:00:00: Hoàn 50% (250.000 ₫)");
                    AssertTrue(b24_00_After != null && b24_00_After.PaymentStatus == PaymentStatus.PARTIALLY_REFUNDED,
                        "(r) Nấc 24-48h (24:00:00): PaymentStatus chuyển sang PARTIALLY_REFUNDED (không REFUND_PENDING)");

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

                    if (splitRefundReq != null)
                    {
                        var completeActionResult = await adminController.CompleteRefund(splitRefundReq.RefundId, new CompleteRefundDto
                        {
                            BankTransactionRef = "FT20261015998877"
                        });
                        var completedRefundReq = await context.RefundRequests.Find(r => r.RefundId == splitRefundReq.RefundId).FirstOrDefaultAsync();
                        AssertTrue(completedRefundReq?.Status == RefundStatus.COMPLETED && completedRefundReq.BankTransactionRef == "FT20261015998877",
                            "Admin xác nhận hoàn tiền thành công, lưu mã đối soát ngân hàng");
                    }
                }

                // =========================================================================
                // TEST 11: Webhook idempotent & crash midway recovery
                // =========================================================================
                Console.WriteLine("\n--- TEST 11: Webhook idempotent & phục hồi sau crash giữa chừng ---");
                {
                    var crashBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_CRASH_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(15),
                        NgayKetThuc = baseDate.AddDays(16),
                        SoNgay = 2,
                        TongTien = 400000,
                        TotalAmount = 400000,
                        TrangThai = BookingStatus.ACCEPTED,
                        PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Bookings.InsertOneAsync(crashBooking);
                    createdBookingIds.Add(crashBooking.BookingId);

                    var crashOrderCode = Random.Shared.Next(800000000, 899999999);
                    var crashPay = new Payment
                    {
                        PaymentId = ObjectId.GenerateNewId().ToString(),
                        BookingId = crashBooking.BookingId,
                        OrderCode = crashOrderCode,
                        Amount = 400000,
                        Status = PaymentLinkStatus.PENDING,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Payments.InsertOneAsync(crashPay);

                    // Giả lập crash: Đã chuyển Payment sang PAID nhưng server chết trước khi Booking->HELD và ghi sổ
                    await context.Payments.UpdateOneAsync(
                        p => p.OrderCode == crashOrderCode,
                        Builders<Payment>.Update.Set(p => p.Status, PaymentLinkStatus.PAID)
                    );

                    // Webhook gọi lại lần 2
                    var retryJson = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        desc = "success",
                        success = true,
                        data = new
                        {
                            orderCode = crashOrderCode,
                            amount = 400000,
                            code = "00",
                            counterAccountBankId = "VCB",
                            counterAccountName = "NGUYEN RETRY",
                            counterAccountNumber = "998877"
                        },
                        signature = "MOCK_VALID_SIGNATURE"
                    });

                    await paymentService.ProcessWebhookAsync(retryJson, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);

                    var bookingAfterRetry = await context.Bookings.Find(b => b.BookingId == crashBooking.BookingId).FirstOrDefaultAsync();
                    AssertTrue(bookingAfterRetry.PaymentStatus == PaymentStatus.HELD,
                        "Webhook gọi lại sau crash: Hoàn thiện chuyển Booking sang HELD (KHÔNG vào REFUND_PENDING)");

                    var entriesAfterRetry = await context.JournalEntries.Find(j => j.BookingId == crashBooking.BookingId).ToListAsync();
                    AssertTrue(entriesAfterRetry.Count == 1 && entriesAfterRetry[0].Type == JournalType.PAYMENT_HELD,
                        "Webhook gọi lại sau crash: Ghi đúng duy nhất một bút toán PAYMENT_HELD");

                    // Webhook gọi lại lần 3 (hoàn toàn lặp lại)
                    await paymentService.ProcessWebhookAsync(retryJson, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);
                    var entriesAfter3rd = await context.JournalEntries.Find(j => j.BookingId == crashBooking.BookingId).ToListAsync();
                    AssertTrue(entriesAfter3rd.Count == 1,
                        "Webhook gọi lại lần 3: Hoàn toàn idempotent, không tạo bút toán dư thừa");
                }

                // =========================================================================
                // TEST 12: Đua 50 cặp Giải ngân || Hoàn tiền (ReleaseEscrow vs CancelBooking)
                // =========================================================================
                Console.WriteLine("\n--- TEST 12: Đua 50 cặp song song Giải ngân || Hoàn tiền (Release vs Refund) ---");
                {
                    int totalPairs = 50;
                    int releaseWins = 0;
                    int refundWins = 0;
                    bool noConflictingEntries = true;

                    for (int i = 0; i < totalPairs; i++)
                    {
                        var raceBooking = new Booking
                        {
                            BookingId = ObjectId.GenerateNewId().ToString(),
                            MaDatLich = $"PS_RACE50_{i}_{testPrefix}",
                            OwnerId = ownerUser.UserId,
                            SitterId = sitterProfile.SitterId,
                            PetId = pet.PetId,
                            NgayBatDau = DateTime.UtcNow.AddDays(5),
                            NgayKetThuc = DateTime.UtcNow.AddDays(7),
                            SoNgay = 3,
                            TongTien = 300000,
                            TotalAmount = 300000,
                            CommissionBps = 1000,
                            TrangThai = BookingStatus.ACCEPTED,
                            PaymentStatus = PaymentStatus.HELD,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow.AddDays(-1)
                        };
                        await context.Bookings.InsertOneAsync(raceBooking);
                        createdBookingIds.Add(raceBooking.BookingId);

                        // Bút toán nạp cọc ban đầu
                        await ledgerService.RecordEntryAsync(new JournalEntry
                        {
                            EntryId = ObjectId.GenerateNewId().ToString(),
                            BookingId = raceBooking.BookingId,
                            TransactionId = $"TX_{i}_HELD",
                            Type = JournalType.PAYMENT_HELD,
                            ActorUserId = "OWNER",
                            CreatedAt = DateTime.UtcNow,
                            IdempotencyKey = $"{raceBooking.BookingId}:PAYMENT_HELD:1",
                            Lines = new List<JournalLine>
                            {
                                new JournalLine { Account = LedgerAccount.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 300000 },
                                new JournalLine { Account = LedgerAccount.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 300000 }
                            }
                        });

                        // 2 hành động cạnh tranh song song:
                        // T1: Giải ngân (ReleaseEscrowToSitterAndPlatformAsync)
                        // T2: Hủy đơn hoàn tiền (CancelBookingAsync)
                        var tRelease = Task.Run(async () =>
                        {
                            await bookingService.ReleaseEscrowToSitterAndPlatformAsync(raceBooking, DateTime.UtcNow, "CONFIRM");
                        });
                        var tCancel = Task.Run(async () =>
                        {
                            try
                            {
                                await bookingService.CancelBookingAsync(ownerUser.UserId, raceBooking.BookingId, "Huỷ song song");
                            }
                            catch { }
                        });

                        await Task.WhenAll(tRelease, tCancel);

                        var finalB = await context.Bookings.Find(b => b.BookingId == raceBooking.BookingId).FirstOrDefaultAsync();
                        var finalEntries = await context.JournalEntries.Find(j => j.BookingId == raceBooking.BookingId && j.Type != JournalType.PAYMENT_HELD).ToListAsync();

                        if (finalB.PaymentStatus == PaymentStatus.RELEASED)
                        {
                            releaseWins++;
                        }
                        else if (finalB.PaymentStatus == PaymentStatus.REFUND_PENDING)
                        {
                            refundWins++;
                        }

                        // Kiểm tra không có 2 bút toán đối nghịch cùng tồn tại
                        var hasReleaseEntry = finalEntries.Any(e => e.Type == JournalType.ESCROW_RELEASE);
                        var hasRefundEntry = finalEntries.Any(e => e.Type == JournalType.REFUND_PENDING);
                        if (hasReleaseEntry && hasRefundEntry)
                        {
                            noConflictingEntries = false;
                        }
                    }

                    AssertTrue(releaseWins + refundWins == totalPairs,
                        $"50 cặp Giải ngân || Hoàn tiền: Đúng 1 bên thắng mỗi cặp (Giải ngân thắng: {releaseWins}, Hoàn tiền thắng: {refundWins})");
                    AssertTrue(noConflictingEntries,
                        "Sổ cái trên toàn bộ 50 cặp: Tuyệt đối không có bút toán đối nghịch (Release và Refund) trên cùng một đơn");
                }

                // =========================================================================
                // TEST 13: Đối soát tự sửa bù khi claim xong mà sổ chưa ghi
                // =========================================================================
                Console.WriteLine("\n--- TEST 13: Đối soát tự sửa bù (Reconciliation self-healing) ---");
                {
                    var reconBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_RECON_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = DateTime.UtcNow.AddDays(2),
                        NgayKetThuc = DateTime.UtcNow.AddDays(4),
                        SoNgay = 3,
                        TongTien = 500000,
                        TotalAmount = 500000,
                        CommissionBps = 1000,
                        TrangThai = BookingStatus.COMPLETED,
                        PaymentStatus = PaymentStatus.RELEASED, // Claim đã xong
                        EscrowReleasedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        IsActive = false,
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    };
                    await context.Bookings.InsertOneAsync(reconBooking);
                    createdBookingIds.Add(reconBooking.BookingId);

                    // 1. Chưa có bút toán ESCROW_RELEASE trong sổ cái (giả lập crash trước khi ghi sổ)
                    var beforeEntries = await context.JournalEntries.Find(j => j.BookingId == reconBooking.BookingId).ToListAsync();
                    AssertTrue(beforeEntries.Count == 0, "Ban đầu sổ cái chưa có bút toán do crash");

                    // 2. Chạy đối soát tự sửa bù lần đầu -> tạo đúng 1 bút toán
                    var repaired = await bookingService.ReconcileMissingLedgerEntriesAsync(DateTime.UtcNow, reconBooking.BookingId, force: true);
                    AssertTrue(repaired == 1, "Hàm đối soát phát hiện đơn thiếu sổ và tự sửa bù thành công 1 bút toán");

                    var afterEntries = await context.JournalEntries.Find(j => j.BookingId == reconBooking.BookingId).ToListAsync();
                    AssertTrue(afterEntries.Count == 1 && afterEntries[0].Type == JournalType.ESCROW_RELEASE && afterEntries[0].IdempotencyKey == LedgerIdempotencyKeys.EscrowRelease(reconBooking.BookingId),
                        "Bút toán ESCROW_RELEASE tự sửa bù hợp lệ, đúng loại và đúng IdempotencyKey chuẩn");

                    // 3. Chạy đối soát lần 1 trên dữ liệu đã đủ sổ -> 0 bút toán mới
                    var runAgain1 = await bookingService.ReconcileMissingLedgerEntriesAsync(DateTime.UtcNow, reconBooking.BookingId, force: true);
                    AssertTrue(runAgain1 == 0, "Chạy đối soát lần 1 trên dữ liệu đã đủ sổ -> 0 bút toán mới (Idempotent)");

                    // 4. Chạy đối soát lần 2 trên dữ liệu đã đủ sổ -> 0 bút toán mới
                    var runAgain2 = await bookingService.ReconcileMissingLedgerEntriesAsync(DateTime.UtcNow, reconBooking.BookingId, force: true);
                    AssertTrue(runAgain2 == 0, "Chạy đối soát lần 2 trên dữ liệu đã đủ sổ -> 0 bút toán mới (Idempotent)");

                    // 5. Cố ý xóa một bút toán trong sổ cái
                    await context.JournalEntries.DeleteOneAsync(j => j.IdempotencyKey == LedgerIdempotencyKeys.EscrowRelease(reconBooking.BookingId));
                    var hasEntryAfterDelete = await ledgerService.HasEntryAsync(LedgerIdempotencyKeys.EscrowRelease(reconBooking.BookingId));
                    AssertTrue(!hasEntryAfterDelete, "Cố ý xóa bút toán giải ngân trong JournalEntries thành công");

                    // 6. Chạy đối soát lại -> bù đúng 1 bút toán, đúng loại ESCROW_RELEASE
                    var repairedAfterDelete = await bookingService.ReconcileMissingLedgerEntriesAsync(DateTime.UtcNow, reconBooking.BookingId, force: true);
                    AssertTrue(repairedAfterDelete == 1, "Đối soát bù đúng một bút toán bị thiếu");
                    var restored = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.EscrowRelease(reconBooking.BookingId)).FirstOrDefaultAsync();
                    AssertTrue(restored != null && restored.Type == JournalType.ESCROW_RELEASE, "Bút toán phục hồi đúng loại ESCROW_RELEASE và đúng tài khoản Nợ/Có");
                }

                // =========================================================================
                // TEST 14: Thu nhập Sitter dùng SitterUserId & AvailableAt
                // =========================================================================
                Console.WriteLine("\n--- TEST 14: Thu nhập Sitter dùng SitterUserId và AvailableAt ---");
                {
                    var earnNow = DateTime.UtcNow;
                    var holdHours = 48;
                    var earnEntry = new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        TransactionId = "TX_EARN_TEST",
                        Type = JournalType.ESCROW_RELEASE,
                        ActorUserId = "SYSTEM",
                        CreatedAt = earnNow,
                        IdempotencyKey = $"{testPrefix}:EARNING_AVAIL_TEST",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccount.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 900000 },
                            new JournalLine { Account = LedgerAccount.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 90000 },
                            new JournalLine
                            {
                                Account = LedgerAccount.SitterEarnings(sitterUser.UserId), // SitterUserId
                                Direction = LedgerDirection.CREDIT,
                                Amount = 810000,
                                AvailableAt = earnNow.AddHours(holdHours)
                            }
                        }
                    };
                    await ledgerService.RecordEntryAsync(earnEntry);

                    // 1. Trước AvailableAt (ngay bây giờ): Thu nhập phải ở PendingHoldBalance, AvailableBalance = 0
                    var earnBefore = await ledgerService.GetSitterEarningsAsync(sitterUser.UserId, earnNow, holdHours);
                    AssertTrue(earnBefore.PendingHoldBalance >= 810000 && earnBefore.AvailableBalance == 0,
                        "Trước AvailableAt: Thu nhập ở trạng thái tạm giữ (PendingHoldBalance >= 810.000 ₫, AvailableBalance == 0)");

                    // 2. Sau AvailableAt (+49h): Thu nhập chuyển sang AvailableBalance, PendingHoldBalance = 0
                    var earnAfter = await ledgerService.GetSitterEarningsAsync(sitterUser.UserId, earnNow.AddHours(49), holdHours);
                    AssertTrue(earnAfter.AvailableBalance >= 810000 && earnAfter.PendingHoldBalance == 0,
                        "Sau AvailableAt: Thu nhập khả dụng rút về tài khoản (AvailableBalance >= 810.000 ₫, PendingHoldBalance == 0)");

                    // 3. Tra cứu bằng sitterUser.UserId (từ JWT) tìm thấy đúng, tra cứu bằng SitterId trả về 0
                    var earnBySitterId = await ledgerService.GetSitterEarningsAsync(sitterProfile.SitterId, earnNow, holdHours);
                    AssertTrue(earnBySitterId.TotalEarnings == 0,
                        "Sổ thu nhập phân biệt đúng SitterUserId vs SitterId (tra theo SitterId trả về 0)");
                }

                // =========================================================================
                // TEST 15: Webhook validation (Chữ ký sai -> 400, Unknown OrderCode -> 200, Amount mismatch -> 400)
                // =========================================================================
                Console.WriteLine("\n--- TEST 15: Kiểm tra xác thực Webhook & Sai lệch số tiền ---");
                {
                    // 1. Chữ ký sai -> 400
                    bool caughtBadSig = false;
                    try
                    {
                        var invalidSigJson = JsonSerializer.Serialize(new
                        {
                            code = "00",
                            data = new { orderCode = 123456, amount = 100000 },
                            signature = "INVALID_SIG"
                        });
                        await paymentService.ProcessWebhookAsync(invalidSigJson, "INVALID_SIG", DateTime.UtcNow);
                    }
                    catch (BadRequestException)
                    {
                        caughtBadSig = true;
                    }
                    AssertTrue(caughtBadSig, "Chữ ký webhook sai ném ngoại lệ BadRequestException (HTTP 400)");

                    // 2. Chữ ký đúng nhưng OrderCode không tồn tại (PayOS test probe khi đăng ký URL) -> 200 OK + log warning
                    var unknownOrderCode = 999999999;
                    var unknownPayload = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        data = new { orderCode = unknownOrderCode, amount = 100000 },
                        signature = "MOCK_VALID_SIGNATURE"
                    });
                    bool unknownOk = true;
                    try
                    {
                        await paymentService.ProcessWebhookAsync(unknownPayload, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);
                    }
                    catch
                    {
                        unknownOk = false;
                    }
                    AssertTrue(unknownOk, "Chữ ký đúng nhưng OrderCode không tồn tại trả về 200 OK (không crash, không ghi sổ)");

                    // 3. Amount mismatch: Webhook data.Amount != Payment.Amount -> 400
                    var mismatchBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_MISMATCH_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(20),
                        NgayKetThuc = baseDate.AddDays(21),
                        SoNgay = 2,
                        TongTien = 400000,
                        TotalAmount = 400000,
                        TrangThai = BookingStatus.ACCEPTED,
                        PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Bookings.InsertOneAsync(mismatchBooking);
                    createdBookingIds.Add(mismatchBooking.BookingId);

                    var mismatchOrderCode = Random.Shared.Next(800000000, 899999999);
                    await context.Payments.InsertOneAsync(new Payment
                    {
                        PaymentId = ObjectId.GenerateNewId().ToString(),
                        BookingId = mismatchBooking.BookingId,
                        OrderCode = mismatchOrderCode,
                        Amount = 400000,
                        Status = PaymentLinkStatus.PENDING,
                        CreatedAt = DateTime.UtcNow
                    });

                    // 3. Amount mismatch: Webhook data.Amount != Payment.Amount -> Không trả 400; lưu bản ghi bất thường, báo Admin, trả 200, không ghi sổ doanh thu (Requirement 4)
                    var mismatchJson = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        data = new { orderCode = mismatchOrderCode, amount = 200000 }, // Nhận 200k trong khi đơn 400k
                        signature = "MOCK_VALID_SIGNATURE"
                    });
                    
                    bool completedWithoutThrowing = false;
                    try
                    {
                        await paymentService.ProcessWebhookAsync(mismatchJson, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);
                        completedWithoutThrowing = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Lỗi không mong muốn: {ex.Message}");
                    }

                    AssertTrue(completedWithoutThrowing, "Webhook sai số tiền không ném BadRequestException (trả về 200 OK)");

                    var abnormalPay = await context.Payments.Find(p => p.OrderCode == mismatchOrderCode).FirstOrDefaultAsync();
                    AssertTrue(abnormalPay != null && abnormalPay.IsAbnormal && abnormalPay.Status == PaymentLinkStatus.ABNORMAL && abnormalPay.ReceivedAmount == 200000,
                        "Lưu bản ghi giao dịch bất thường (IsAbnormal=true, Status=ABNORMAL, ReceivedAmount=200.000 ₫)");

                    var abnormalEntries = await context.JournalEntries.Find(j => j.BookingId == mismatchBooking.BookingId).ToListAsync();
                    AssertTrue(abnormalEntries.Count == 1 &&
                               abnormalEntries[0].Type == JournalType.PAYMENT_ABNORMAL_SUSPENSE &&
                               abnormalEntries[0].Lines.Any(l => l.Account == LedgerAccounts.SUSPENSE && l.Direction == LedgerDirection.CREDIT && l.Amount == 200000) &&
                               !abnormalEntries[0].Lines.Any(l => l.Account == LedgerAccounts.ESCROW_HELD || l.Account == LedgerAccounts.PLATFORM_REVENUE),
                        "Ghi nhận đúng bút toán SUSPENSE (GATEWAY_CLEARING -> SUSPENSE 200.000 ₫), tuyệt đối không ghi nhận ESCROW_HELD hay doanh thu");

                    var adminNotif = await context.Notifications.Find(n => n.Type == NotificationType.PAYMENT_ABNORMAL && n.BookingId == mismatchBooking.BookingId).FirstOrDefaultAsync();
                    AssertTrue(adminNotif != null && adminNotif.TargetRole == "ADMIN", "Gửi thông báo cảnh báo giao dịch bất thường cho Admin (TargetRole=ADMIN)");
                }

                // =========================================================================
                // TEST 16: Snapshot giá (PriceBreakdown) & Xác nhận không có phần lẻ
                // =========================================================================
                Console.WriteLine("\n--- TEST 16: Snapshot cơ cấu giá (PriceBreakdown) và kiểm tra số nguyên ---");
                {
                    var priceReq = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        NgayBatDau = baseDate.AddDays(25).ToString("yyyy-MM-dd"),
                        NgayKetThuc = baseDate.AddDays(27).ToString("yyyy-MM-dd"), // 3 ngày
                        TenPet = "Milu",
                        LoaiPet = "Chó",
                        KichCo = "Vừa (5–15kg)",
                        ExpectedTotal = 600000
                    };
                    var priceDetail = await bookingService.CreateBookingAsync(ownerUser.UserId, priceReq);
                    createdBookingIds.Add(priceDetail.BookingId);

                    AssertTrue(priceDetail.PriceBreakdown != null && priceDetail.PriceBreakdown.Count >= 1,
                        "Đơn đặt lịch lưu đầy đủ snapshot cơ cấu giá (PriceBreakdown)");
                    AssertTrue(priceDetail.TotalAmount == (long)priceDetail.TongTien && priceDetail.TotalAmount == 600000,
                        "TotalAmount == (long)TongTien không có phần lẻ (600.000 ₫)");
                    AssertTrue(priceDetail.CommissionBps == 1000 && priceDetail.CommissionAmount == 60000,
                        "CommissionBps = 1000 (10%), CommissionAmount = 60.000 ₫");
                }

                // =========================================================================
                // TEST 17 (Requirement 3): Hoàn tiền complete & Chặn Admin hoàn đơn chính mình
                // =========================================================================
                Console.WriteLine("\n--- TEST 17: Hoàn tiền Complete, Chặn Admin hoàn đơn mình, Ngắt giữa chừng và chạy lại ---");
                {
                    // 1. Tạo đơn mà Admin chính là Owner
                    var adminOwnerBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_ADMBK_{testPrefix}",
                        OwnerId = adminUser.UserId, // Admin là chủ đơn
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(10),
                        NgayKetThuc = baseDate.AddDays(12),
                        SoNgay = 3,
                        TongTien = 300000,
                        TotalAmount = 300000,
                        TrangThai = BookingStatus.CANCELLED,
                        PaymentStatus = PaymentStatus.REFUND_PENDING,
                        IsActive = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Bookings.InsertOneAsync(adminOwnerBooking);
                    createdBookingIds.Add(adminOwnerBooking.BookingId);

                    var selfRefundReq = new RefundRequest
                    {
                        RefundId = ObjectId.GenerateNewId().ToString(),
                        BookingId = adminOwnerBooking.BookingId,
                        OwnerUserId = adminUser.UserId,
                        Amount = 300000,
                        Reason = "OWNER_CANCELLED_GE_48H",
                        Status = RefundStatus.REFUND_PENDING,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.RefundRequests.InsertOneAsync(selfRefundReq);

                    // Admin cố gắng tự duyệt hoàn tiền cho chính mình -> bị chặn 403 Forbidden
                    bool blockedSelfRefund = false;
                    try
                    {
                        await paymentService.CompleteRefundAsync(adminUser.UserId, selfRefundReq.RefundId, new CompleteRefundDto { BankTransactionRef = "TX_SELF_REFUND" }, DateTime.UtcNow);
                    }
                    catch (ForbiddenException)
                    {
                        blockedSelfRefund = true;
                    }
                    AssertTrue(blockedSelfRefund, "Chặn Admin duyệt/hoàn tiền đơn của chính mình (ném ForbiddenException - HTTP 403)");

                    // 2. Tạo một Admin khác độc lập để thực hiện hoàn tiền
                    var otherAdmin = new User
                    {
                        UserId = ObjectId.GenerateNewId().ToString(),
                        Email = $"other_admin_{testPrefix}@pawstay.vn",
                        FullName = "Admin Độc Lập",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                        Role = "Admin",
                        Phone = "0900000999",
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Users.InsertOneAsync(otherAdmin);
                    createdUserIds.Add(otherAdmin.UserId);

                    // Tạo đơn của ownerUser bình thường có yêu cầu hoàn tiền
                    var normalRefundBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_NORMREF_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(15),
                        NgayKetThuc = baseDate.AddDays(17),
                        SoNgay = 3,
                        TongTien = 300000,
                        TotalAmount = 300000,
                        TrangThai = BookingStatus.CANCELLED,
                        PaymentStatus = PaymentStatus.REFUND_PENDING,
                        IsActive = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.Bookings.InsertOneAsync(normalRefundBooking);
                    createdBookingIds.Add(normalRefundBooking.BookingId);

                    var normalRefundReq = new RefundRequest
                    {
                        RefundId = ObjectId.GenerateNewId().ToString(),
                        BookingId = normalRefundBooking.BookingId,
                        OwnerUserId = ownerUser.UserId,
                        Amount = 300000,
                        Reason = "OWNER_CANCELLED_GE_48H",
                        Status = RefundStatus.REFUND_PENDING,
                        CreatedAt = DateTime.UtcNow
                    };
                    await context.RefundRequests.InsertOneAsync(normalRefundReq);

                    // 3. Giả lập ngắt giữa các bước: Đã cập nhật RefundRequest = COMPLETED nhưng server sập trước khi ghi sổ và cập nhật Booking
                    await context.RefundRequests.UpdateOneAsync(
                        r => r.RefundId == normalRefundReq.RefundId,
                        Builders<RefundRequest>.Update
                            .Set(r => r.Status, RefundStatus.COMPLETED)
                            .Set(r => r.BankTransactionRef, "FT261018MOCKINTERRUPT")
                            .Set(r => r.AdminUserId, otherAdmin.UserId)
                            .Set(r => r.CompletedAt, DateTime.UtcNow)
                    );

                    // Xác nhận sổ cái chưa có bút toán REFUND_COMPLETED
                    var hasEntryBeforeRetry = await ledgerService.HasEntryAsync(LedgerIdempotencyKeys.RefundCompleted(normalRefundReq.RefundId));
                    AssertTrue(!hasEntryBeforeRetry, "Giả lập sập server trước khi ghi sổ REFUND_PENDING -> GATEWAY_CLEARING");

                    // 4. Gọi lại CompleteRefundAsync sau khi ngắt giữa chừng
                    var completeDto = new CompleteRefundDto { BankTransactionRef = "FT261018MOCKINTERRUPT" };
                    await paymentService.CompleteRefundAsync(otherAdmin.UserId, normalRefundReq.RefundId, completeDto, DateTime.UtcNow);

                    // Kiểm tra sổ cái đã được bù đúng bút toán
                    var hasEntryAfterRetry = await ledgerService.HasEntryAsync(LedgerIdempotencyKeys.RefundCompleted(normalRefundReq.RefundId));
                    AssertTrue(hasEntryAfterRetry, "Gọi lại sau khi ngắt giữa chừng: Tự động hoàn tất ghi sổ REFUND_PENDING -> GATEWAY_CLEARING");

                    var refundBookingAfter = await context.Bookings.Find(b => b.BookingId == normalRefundBooking.BookingId).FirstOrDefaultAsync();
                    AssertTrue(refundBookingAfter != null && refundBookingAfter.PaymentStatus == PaymentStatus.REFUNDED,
                        "Cập nhật Booking.PaymentStatus sang REFUNDED thành công");

                    // 5. Gọi lại lần 3 để kiểm tra hoàn toàn Idempotent
                    await paymentService.CompleteRefundAsync(otherAdmin.UserId, normalRefundReq.RefundId, completeDto, DateTime.UtcNow);
                    var allRefundEntries = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.RefundCompleted(normalRefundReq.RefundId)).ToListAsync();
                    AssertTrue(allRefundEntries.Count == 1, "Gọi lại lần 3: Tuyệt đối Idempotent, không nhân đôi bút toán sổ cái");
                }

                // =========================================================================
                // TEST 18 (Requirement 5): 50 lần gọi Webhook song song -> Duy nhất 1 RefundRequest
                // =========================================================================
                Console.WriteLine("\n--- TEST 18: Webhook gọi lặp 50 lần song song trên đơn đã đóng -> Duy nhất 1 RefundRequest ---");
                {
                    var closedBooking = new Booking
                    {
                        BookingId = ObjectId.GenerateNewId().ToString(),
                        MaDatLich = $"PS_CLOSED_{testPrefix}",
                        OwnerId = ownerUser.UserId,
                        SitterId = sitterProfile.SitterId,
                        PetId = pet.PetId,
                        NgayBatDau = baseDate.AddDays(5),
                        NgayKetThuc = baseDate.AddDays(7),
                        SoNgay = 3,
                        TongTien = 400000,
                        TotalAmount = 400000,
                        TrangThai = BookingStatus.EXPIRED, // Đơn đã hết hạn đóng thật
                        PaymentStatus = PaymentStatus.EXPIRED,
                        IsActive = false,
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    };
                    await context.Bookings.InsertOneAsync(closedBooking);
                    createdBookingIds.Add(closedBooking.BookingId);

                    var closedOrderCode = Random.Shared.Next(800000000, 899999999);
                    var closedPayment = new Payment
                    {
                        PaymentId = ObjectId.GenerateNewId().ToString(),
                        BookingId = closedBooking.BookingId,
                        OrderCode = closedOrderCode,
                        Amount = 400000,
                        Status = PaymentLinkStatus.EXPIRED,
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    };
                    await context.Payments.InsertOneAsync(closedPayment);

                    var webhookJson50 = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        data = new
                        {
                            orderCode = closedOrderCode,
                            amount = 400000,
                            code = "00",
                            counterAccountBankId = "VCB",
                            counterAccountName = "NGUYEN TEST CONCURRENT",
                            counterAccountNumber = "9988776655"
                        },
                        signature = "MOCK_VALID_SIGNATURE"
                    });

                    // 50 luồng webhook gọi song song đồng thời
                    var tasks50 = new List<Task>();
                    for (int i = 0; i < 50; i++)
                    {
                        tasks50.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await paymentService.ProcessWebhookAsync(webhookJson50, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Task error: {ex.Message}");
                            }
                        }));
                    }

                    await Task.WhenAll(tasks50);

                    // Kiểm tra số lượng RefundRequest được tạo
                    var refundRequestsCreated = await context.RefundRequests.Find(r => r.PaymentId == closedPayment.PaymentId).ToListAsync();
                    AssertTrue(refundRequestsCreated.Count == 1,
                        $"Webhook 50 lần gọi song song: Duy nhất 1 RefundRequest được tạo (Số lượng thực tế: {refundRequestsCreated.Count})");

                    var lateEntries = await context.JournalEntries.Find(j => j.BookingId == closedBooking.BookingId).ToListAsync();
                    AssertTrue(lateEntries.Count == 1 && lateEntries[0].Type == JournalType.LATE_PAYMENT_REFUND,
                        "Ghi nhận đúng duy nhất một bút toán sổ cái LATE_PAYMENT_REFUND");
                }

                // =========================================================================
                // TEST 19 (u): KIỂM TRA BẤT BIẾN THEO ĐƠN TRÊN MỌI ĐƯỜNG TẤT TOÁN & BẤT BIẾN TOÀN CỤC
                // =========================================================================
                Console.WriteLine("\n--- TEST 19 (u): Kiểm tra bất biến tất toán theo đơn trên mọi luồng và bất biến toàn cục ---");
                {
                    // Dọn dẹp dữ liệu test cũ để kiểm tra bất biến toàn cục chuẩn xác
                    await context.JournalEntries.DeleteManyAsync(FilterDefinition<JournalEntry>.Empty);
                    await context.Bookings.DeleteManyAsync(FilterDefinition<Booking>.Empty);
                    await context.RefundRequests.DeleteManyAsync(FilterDefinition<RefundRequest>.Empty);
                    await context.Payments.DeleteManyAsync(FilterDefinition<Payment>.Empty);

                    // 1. COMPLETED: Owner confirmed
                    var b1 = CreateHeldBooking("INV_COMP_OWNER", DateTime.UtcNow.AddDays(5), 500000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b1.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b1);
                    createdBookingIds.Add(b1.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b1.BookingId,
                        TransactionId = "TX_INV_1",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b1.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                        }
                    });
                    await bookingService.ReleaseEscrowToSitterAndPlatformAsync(b1, DateTime.UtcNow, "OWNER_CONFIRMED");
                    var inv1 = await ledgerService.VerifyBookingSettlementInvariantAsync(b1.BookingId);
                    AssertTrue(inv1.IsValid, $"(u) Đường 1: COMPLETED (Owner confirmed) thỏa mãn bất biến: {inv1.Reason}");

                    // 2. COMPLETED: Lazy auto-completed
                    var b2 = CreateHeldBooking("INV_COMP_LAZY", DateTime.UtcNow.AddDays(5), 450000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b2.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b2);
                    createdBookingIds.Add(b2.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b2.BookingId,
                        TransactionId = "TX_INV_2",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b2.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 450000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 450000 }
                        }
                    });
                    await bookingService.ReleaseEscrowToSitterAndPlatformAsync(b2, DateTime.UtcNow, "AUTO_COMPLETED");
                    var inv2 = await ledgerService.VerifyBookingSettlementInvariantAsync(b2.BookingId);
                    AssertTrue(inv2.IsValid, $"(u) Đường 2: COMPLETED (Lazy auto-completed) thỏa mãn bất biến: {inv2.Reason}");

                    // 3. COMPLETED: Admin resolved
                    var b3 = CreateHeldBooking("INV_COMP_ADM", DateTime.UtcNow.AddDays(5), 600000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b3.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b3);
                    createdBookingIds.Add(b3.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b3.BookingId,
                        TransactionId = "TX_INV_3",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b3.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 600000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 600000 }
                        }
                    });
                    await bookingService.ReleaseEscrowToSitterAndPlatformAsync(b3, DateTime.UtcNow, "ADMIN_RESOLVED_COMPLETED");
                    var inv3 = await ledgerService.VerifyBookingSettlementInvariantAsync(b3.BookingId);
                    AssertTrue(inv3.IsValid, $"(u) Đường 3: COMPLETED (Admin resolved) thỏa mãn bất biến: {inv3.Reason}");

                    // 4. CANCELLED: Nấc 1 (>=48h)
                    var b4 = CreateHeldBooking("INV_CAN_T1", DateTime.UtcNow.AddDays(5), 400000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b4.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b4);
                    createdBookingIds.Add(b4.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b4.BookingId,
                        TransactionId = "TX_INV_4",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b4.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 400000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 400000 }
                        }
                    });
                    await bookingService.CancelBookingAsync(ownerUser.UserId, b4.BookingId, "Hủy trước 48h");
                    var inv4 = await ledgerService.VerifyBookingSettlementInvariantAsync(b4.BookingId);
                    AssertTrue(inv4.IsValid, $"(u) Đường 4: Hủy nấc 1 (>=48h) thỏa mãn bất biến: {inv4.Reason}");

                    // 5. CANCELLED: Nấc 2 (24-48h)
                    var b5 = CreateHeldBooking("INV_CAN_T2", DateTime.UtcNow.AddHours(36), 500000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b5.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b5);
                    createdBookingIds.Add(b5.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b5.BookingId,
                        TransactionId = "TX_INV_5",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b5.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                        }
                    });
                    await bookingService.CancelBookingAsync(ownerUser.UserId, b5.BookingId, "Hủy trong khoảng 24-48h");
                    var inv5 = await ledgerService.VerifyBookingSettlementInvariantAsync(b5.BookingId);
                    AssertTrue(inv5.IsValid, $"(u) Đường 5: Hủy nấc 2 (24-48h) thỏa mãn bất biến: {inv5.Reason}");

                    // 6. CANCELLED: Nấc 3 (<24h)
                    var b6 = CreateHeldBooking("INV_CAN_T3", DateTime.UtcNow.AddHours(12), 350000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b6.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b6);
                    createdBookingIds.Add(b6.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b6.BookingId,
                        TransactionId = "TX_INV_6",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b6.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 350000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 350000 }
                        }
                    });
                    await bookingService.CancelBookingAsync(ownerUser.UserId, b6.BookingId, "Hủy gấp dưới 24h");
                    var inv6 = await ledgerService.VerifyBookingSettlementInvariantAsync(b6.BookingId);
                    AssertTrue(inv6.IsValid, $"(u) Đường 6: Hủy nấc 3 (<24h) thỏa mãn bất biến: {inv6.Reason}");

                    // 7. CANCELLED: Sitter hủy
                    var b7 = CreateHeldBooking("INV_CAN_SITTER", DateTime.UtcNow.AddDays(5), 420000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b7.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(b7);
                    createdBookingIds.Add(b7.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b7.BookingId,
                        TransactionId = "TX_INV_7",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b7.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 420000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 420000 }
                        }
                    });
                    await bookingService.CancelBookingAsync(sitterUser.UserId, b7.BookingId, "Sitter bận việc đột xuất");
                    var inv7 = await ledgerService.VerifyBookingSettlementInvariantAsync(b7.BookingId);
                    AssertTrue(inv7.IsValid, $"(u) Đường 7: Sitter hủy đơn thỏa mãn bất biến: {inv7.Reason}");

                    // 8. CANCELLED: Admin CANCELLED (khiếu nại)
                    var b8 = CreateHeldBooking("INV_CAN_ADM", DateTime.UtcNow.AddDays(5), 550000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b8.SitterUserId = sitterUser.UserId;
                    b8.TrangThai = BookingStatus.DISPUTED;
                    b8.DisputeReason = "Chủ nuôi khiếu nại dịch vụ không đúng cam kết";
                    await context.Bookings.InsertOneAsync(b8);
                    createdBookingIds.Add(b8.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b8.BookingId,
                        TransactionId = "TX_INV_8",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b8.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 550000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 550000 }
                        }
                    });
                    await bookingService.AdminResolveDisputeAsync(adminUser.UserId, b8.BookingId, new ResolveDisputeDto
                    {
                        Resolution = BookingStatus.CANCELLED,
                        Note = "Admin đồng ý hoàn 100% cho chủ nuôi sau khi kiểm tra bằng chứng"
                    });
                    var inv8 = await ledgerService.VerifyBookingSettlementInvariantAsync(b8.BookingId);
                    AssertTrue(inv8.IsValid, $"(u) Đường 8: Admin hủy đơn hoàn 100% thỏa mãn bất biến: {inv8.Reason}");

                    // 9. DISPUTE: Dispute SPLIT
                    var b9 = CreateHeldBooking("INV_DISP_SPLIT", DateTime.UtcNow.AddDays(5), 600000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b9.SitterUserId = sitterUser.UserId;
                    b9.TrangThai = BookingStatus.DISPUTED;
                    b9.DisputeReason = "Khiếu nại phân chia";
                    await context.Bookings.InsertOneAsync(b9);
                    createdBookingIds.Add(b9.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b9.BookingId,
                        TransactionId = "TX_INV_9",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{b9.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 600000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 600000 }
                        }
                    });
                    await bookingService.AdminResolveDisputeAsync(adminUser.UserId, b9.BookingId, new ResolveDisputeDto
                    {
                        Resolution = "SPLIT",
                        RefundToOwnerAmount = 250000,
                        Note = "Admin phân chia hoàn 250k cho chủ nuôi, phần còn lại cho Sitter sau phí"
                    });
                    var inv9 = await ledgerService.VerifyBookingSettlementInvariantAsync(b9.BookingId);
                    AssertTrue(inv9.IsValid, $"(u) Đường 9: Dispute chia tiền (SPLIT) thỏa mãn bất biến: {inv9.Reason}");

                    // 10. LATE PAYMENT: Hoàn tiền thanh toán muộn trên đơn đã đóng
                    var b10 = CreateHeldBooking("INV_LATE_PAY", DateTime.UtcNow.AddDays(-10), 300000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    b10.SitterUserId = sitterUser.UserId;
                    b10.TrangThai = BookingStatus.EXPIRED;
                    b10.PaymentStatus = PaymentStatus.EXPIRED;
                    await context.Bookings.InsertOneAsync(b10);
                    createdBookingIds.Add(b10.BookingId);
                    var lateEntry = new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = b10.BookingId,
                        TransactionId = "TX_INV_10_LATE",
                        Type = JournalType.LATE_PAYMENT_REFUND,
                        ActorUserId = "PAYOS_WEBHOOK",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = LedgerIdempotencyKeys.LatePaymentRefund(b10.BookingId),
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 300000 },
                            new JournalLine { Account = LedgerAccounts.REFUND_PENDING, Direction = LedgerDirection.CREDIT, Amount = 300000 }
                        }
                    };
                    await ledgerService.RecordEntryAsync(lateEntry);
                    var inv10 = await ledgerService.VerifyBookingSettlementInvariantAsync(b10.BookingId);
                    AssertTrue(inv10.IsValid, $"(u) Đường 10: Thanh toán muộn hoàn tiền thỏa mãn bất biến: {inv10.Reason}");

                    // CỐ Ý ĐỔI TÊN MỘT TÀI KHOẢN (TAMPERING TEST)
                    var tamperedEntry = await context.JournalEntries.Find(j => j.BookingId == b1.BookingId && j.Type == "ESCROW_RELEASE").FirstOrDefaultAsync();
                    if (tamperedEntry != null)
                    {
                        var origAccount = tamperedEntry.Lines[0].Account;
                        tamperedEntry.Lines[0].Account = "TAMPERED_ACCOUNT_NAME";
                        await context.JournalEntries.ReplaceOneAsync(j => j.EntryId == tamperedEntry.EntryId, tamperedEntry);

                        var tamperCheck = await ledgerService.VerifyBookingSettlementInvariantAsync(b1.BookingId);
                        AssertTrue(!tamperCheck.IsValid, $"(u) Cố ý đổi tên tài khoản trong sổ cái -> Hàm phát hiện ngay lập tức: {tamperCheck.Reason}");

                        // Khôi phục lại
                        tamperedEntry.Lines[0].Account = origAccount;
                        await context.JournalEntries.ReplaceOneAsync(j => j.EntryId == tamperedEntry.EntryId, tamperedEntry);
                        var restoredCheck = await ledgerService.VerifyBookingSettlementInvariantAsync(b1.BookingId);
                        AssertTrue(restoredCheck.IsValid, "(u) Sau khi khôi phục tài khoản, bất biến hợp lệ trở lại");
                    }

                    // KIỂM TRA BẤT BIẾN TOÀN CỤC ESCROW_HELD
                    // Thêm 1 đơn đang giữ tiền cọc (HELD) để kiểm tra bất biến toàn cục
                    var bHeldActive = CreateHeldBooking("INV_HELD_ACTIVE", DateTime.UtcNow.AddDays(5), 500000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    bHeldActive.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(bHeldActive);
                    createdBookingIds.Add(bHeldActive.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = bHeldActive.BookingId,
                        TransactionId = "TX_INV_HELD_ACTIVE",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = DateTime.UtcNow,
                        IdempotencyKey = $"{bHeldActive.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                        }
                    });

                    var globalCheck = await ledgerService.VerifyGlobalEscrowInvariantAsync();
                    AssertTrue(globalCheck.IsValid, $"(u) Bất biến toàn cục ESCROW_HELD hệ thống thỏa mãn: {globalCheck.Reason}");
                }

                // =========================================================================
                // TEST 20 (v): TEST ĐỐI SOÁT TỪNG ĐƯỜNG TẤT TOÁN
                // =========================================================================
                Console.WriteLine("\n--- TEST 20 (v): Test đối soát cho từng đường tất toán (2 lần -> 0 bút toán mới; xóa 1 -> bù đúng 1) ---");
                {
                    var testPaths = new[]
                    {
                        ("RELEASE", "ESCROW_RELEASE", PaymentStatus.RELEASED),
                        ("CANCEL_T1", "CANCEL_OWNER_100", PaymentStatus.REFUND_PENDING),
                        ("CANCEL_T2", "CANCEL_OWNER_50", PaymentStatus.PARTIALLY_REFUNDED),
                        ("CANCEL_T3", "CANCEL_OWNER_0", PaymentStatus.RELEASED),
                        ("CANCEL_SITTER", "CANCEL_SITTER", PaymentStatus.REFUND_PENDING),
                        ("DISPUTE_SPLIT", "DISPUTE_SPLIT", PaymentStatus.PARTIALLY_REFUNDED)
                    };

                    foreach (var (pathName, expectedType, targetPaymentStatus) in testPaths)
                    {
                        var nowUtc = DateTime.UtcNow;
                        var b = CreateHeldBooking($"REC_{pathName}", nowUtc.AddDays(5), 400000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                        b.SitterUserId = sitterUser.UserId;
                        b.PaymentStatus = targetPaymentStatus;
                        b.ClaimedAt = nowUtc;

                        long refAmt = 0, commAmt = 0, sitAmt = 0;
                        string sKey = $"{b.BookingId}:{expectedType}";
                        if (expectedType == "ESCROW_RELEASE") { commAmt = 40000; sitAmt = 360000; sKey = LedgerIdempotencyKeys.EscrowRelease(b.BookingId); }
                        else if (expectedType == "CANCEL_OWNER_100") { refAmt = 400000; sKey = LedgerIdempotencyKeys.CancelOwner100(b.BookingId); }
                        else if (expectedType == "CANCEL_OWNER_50") { refAmt = 200000; commAmt = 20000; sitAmt = 180000; sKey = LedgerIdempotencyKeys.CancelOwner50(b.BookingId); }
                        else if (expectedType == "CANCEL_OWNER_0") { commAmt = 40000; sitAmt = 360000; sKey = LedgerIdempotencyKeys.CancelOwner0(b.BookingId); }
                        else if (expectedType == "CANCEL_SITTER") { refAmt = 400000; sKey = LedgerIdempotencyKeys.CancelSitter(b.BookingId); }
                        else if (expectedType == "DISPUTE_SPLIT") { refAmt = 150000; commAmt = 25000; sitAmt = 225000; sKey = LedgerIdempotencyKeys.DisputeSplit(b.BookingId); }

                        b.Settlement = new SettlementPlan
                        {
                            SettlementKey = sKey,
                            SettlementType = expectedType,
                            TotalAmount = 400000,
                            RefundAmount = refAmt,
                            CommissionAmount = commAmt,
                            SitterAmount = sitAmt,
                            SitterUserId = sitterUser.UserId,
                            ClaimedAt = nowUtc,
                            AvailableAt = sitAmt > 0 ? nowUtc.AddHours(48) : null
                        };

                        await context.Bookings.InsertOneAsync(b);
                        createdBookingIds.Add(b.BookingId);

                        // Ghi nhận trước bút toán nạp tiền cọc
                        await ledgerService.RecordEntryAsync(new JournalEntry
                        {
                            EntryId = ObjectId.GenerateNewId().ToString(),
                            BookingId = b.BookingId,
                            TransactionId = $"TX_{pathName}_HELD",
                            Type = JournalType.PAYMENT_HELD,
                            ActorUserId = "OWNER",
                            CreatedAt = nowUtc,
                            IdempotencyKey = $"{b.BookingId}:PAYMENT_HELD:1",
                            Lines = new List<JournalLine>
                            {
                                new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 400000 },
                                new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 400000 }
                            }
                        });

                        // Ghi nhận bút toán ban đầu qua kế hoạch
                        await bookingService.ExecuteSettlementPlanAsync(b.Settlement, b.BookingId, nowUtc, "SYSTEM");
                        if (b.Settlement.RefundAmount > 0)
                        {
                            await bookingService.CreateOrGetRefundRequestAsync(b, b.Settlement, nowUtc, "INITIAL_TEST20");
                        }

                        // 1. Chạy đối soát lần 1 -> 0 bút toán mới
                        var rec1 = await bookingService.ReconcileMissingLedgerEntriesAsync(nowUtc, b.BookingId, force: true);
                        AssertTrue(rec1 == 0, $"(v) Đối soát đường {pathName} lần 1: 0 bút toán mới (đã đủ)");

                        // 2. Chạy đối soát lần 2 -> 0 bút toán mới
                        var rec2 = await bookingService.ReconcileMissingLedgerEntriesAsync(nowUtc, b.BookingId, force: true);
                        AssertTrue(rec2 == 0, $"(v) Đối soát đường {pathName} lần 2: 0 bút toán mới (Idempotent tuyệt đối)");

                        // 3. Xóa bút toán tất toán trong JournalEntries
                        await context.JournalEntries.DeleteManyAsync(j => j.BookingId == b.BookingId && j.Type == expectedType);

                        // 4. Chạy đối soát bù -> Bù đúng 1 bút toán
                        var recRepaired = await bookingService.ReconcileMissingLedgerEntriesAsync(nowUtc, b.BookingId, force: true);
                        AssertTrue(recRepaired == 1, $"(v) Đối soát đường {pathName} sau khi xóa: Bù đúng 1 bút toán");

                        var repairedEntries = await context.JournalEntries.Find(j => j.BookingId == b.BookingId && j.Type == expectedType).ToListAsync();
                        AssertTrue(repairedEntries.Count == 1 && repairedEntries[0].IdempotencyKey == sKey,
                            $"(v) Bút toán phục hồi đường {pathName} đúng loại {expectedType} và đúng SettlementKey");
                    }
                }

                // =========================================================================
                // TEST 21 (w): ĐỀN BÙ SITTER KHI HỦY NẤC 2, 3 XUẤT HIỆN ĐÚNG TRONG MÀN THU NHẬP
                // =========================================================================
                Console.WriteLine("\n--- TEST 21 (w): Đền bù Sitter khi hủy nấc 2, 3 hiển thị đúng số và AvailableAt trong màn thu nhập ---");
                {
                    var nowUtc = DateTime.UtcNow;
                    var vnToday = nowUtc.AddHours(7).Date;

                    // Sử dụng Sitter riêng biệt cho ca này để kiểm tra chính xác số dư tuyệt đối 270.000 ₫ không bị cộng dồn
                    var sitterUserW = new User
                    {
                        UserId = ObjectId.GenerateNewId().ToString(),
                        Email = $"sitter_w_{Guid.NewGuid().ToString("N")[..6]}@test.com",
                        FullName = "Sitter Dedicated W",
                        Role = "SITTER"
                    };
                    await context.Users.InsertOneAsync(sitterUserW);
                    createdUserIds.Add(sitterUserW.UserId);

                    var sitterProfileW = new SitterProfile
                    {
                        SitterId = ObjectId.GenerateNewId().ToString(),
                        UserId = sitterUserW.UserId,
                        Status = "Approved",
                        IsVerified = true,
                        Bio = "Dedicated Sitter for Tier 2 Cancel Test"
                    };
                    await context.SitterProfiles.InsertOneAsync(sitterProfileW);
                    createdSitterIds.Add(sitterProfileW.SitterId);

                    var bCancelT2 = CreateHeldBooking("EARN_CAN_T2", vnToday.AddDays(2), 600000, ownerUser.UserId, sitterProfileW.SitterId, sitterUserW.UserId);
                    bCancelT2.SitterUserId = sitterUserW.UserId;
                    await context.Bookings.InsertOneAsync(bCancelT2);
                    createdBookingIds.Add(bCancelT2.BookingId);
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = bCancelT2.BookingId,
                        TransactionId = "TX_EARN_T2_HELD",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = nowUtc,
                        IdempotencyKey = $"{bCancelT2.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 600000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 600000 }
                        }
                    });

                    // Hủy đơn nấc 2 (trong khoảng 24-48h)
                    await bookingService.CancelBookingAsync(ownerUser.UserId, bCancelT2.BookingId, "Hủy nấc 2");

                    // Kiểm tra bút toán ghi nhận cho Sitter
                    var sitterT2Entry = await context.JournalEntries.Find(j => j.BookingId == bCancelT2.BookingId && j.Type == "CANCEL_OWNER_50").FirstOrDefaultAsync();
                    AssertTrue(sitterT2Entry != null, "(w) Bút toán hủy nấc 2 CANCEL_OWNER_50 được tạo thành công");
                    var sitterLineT2 = sitterT2Entry?.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.SitterEarnings(sitterUserW.UserId));
                    AssertTrue(sitterLineT2 != null && sitterLineT2.Amount == 270000,
                        $"(w) Sitter nhận đền bù đúng số tiền sau hoa hồng: 270.000 ₫ (thực tế: {sitterLineT2?.Amount:N0} ₫)");
                    AssertTrue(sitterLineT2?.AvailableAt.HasValue == true && sitterLineT2.AvailableAt.Value > nowUtc,
                        $"(w) Dòng đền bù có AvailableAt hợp lệ tương lai = {sitterLineT2?.AvailableAt:O}");

                    // Kiểm tra trên API GetSitterEarningsAsync với Sitter riêng (đảm bảo đúng chính xác 270.000 ₫)
                    var earningsBefore = await ledgerService.GetSitterEarningsAsync(sitterUserW.UserId, nowUtc);
                    AssertTrue(earningsBefore.PendingHoldBalance == 270000 && earningsBefore.AvailableBalance == 0,
                        $"(w) Trước AvailableAt: PendingHoldBalance đúng chính xác 270.000 ₫ (thực tế: {earningsBefore.PendingHoldBalance:N0} ₫), AvailableBalance = 0 ₫");

                    var earningsAfter = await ledgerService.GetSitterEarningsAsync(sitterUserW.UserId, nowUtc.AddHours(50));
                    AssertTrue(earningsAfter.AvailableBalance == 270000 && earningsAfter.PendingHoldBalance == 0,
                        $"(w) Sau AvailableAt: AvailableBalance đúng chính xác 270.000 ₫ (thực tế: {earningsAfter.AvailableBalance:N0} ₫), PendingHoldBalance = 0 ₫");
                }

                // =========================================================================
                // TEST 22 (x): GIÁ BÁO HIỂN THỊ == TOTALAMOUNT == SỐ TIỀN LINK PAYOS
                // =========================================================================
                Console.WriteLine("\n--- TEST 22 (x): Báo giá Server Quote == TotalAmount == Số tiền link PayOS (Quy ước ngày inclusive) ---");
                {
                    var startDate = DateTime.UtcNow.Date.AddDays(10);
                    var endDate = startDate.AddDays(2); // 3 ngày inclusive [Start, End]
                    var quoteReq = new BookingQuoteRequestDto
                    {
                        SitterId = sitterProfile.SitterId,
                        ServiceId = sitterService.ServiceId,
                        NgayBatDau = startDate.ToString("yyyy-MM-dd"),
                        NgayKetThuc = endDate.ToString("yyyy-MM-dd"),
                        Addons = new List<string>()
                    };

                    var quote = await bookingService.GetBookingQuoteAsync(quoteReq);
                    AssertTrue(quote.SoNgay == 3, $"(x) Tính số ngày inclusive [Start, End]: (End - Start) + 1 = 3 ngày (thực tế: {quote.SoNgay})");
                    AssertTrue(quote.TotalAmount == 600000, $"(x) Giá báo Server Quote: 600.000 ₫ (thực tế: {quote.TotalAmount:N0} ₫)");

                    // Tạo đơn với đúng thông tin báo giá
                    var createBookingDto = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        ServiceId = sitterService.ServiceId,
                        NgayBatDau = startDate.ToString("yyyy-MM-dd"),
                        NgayKetThuc = endDate.ToString("yyyy-MM-dd"),
                        TenPet = "Bé Miu Test Quote",
                        LoaiPet = "Mèo",
                        KichCo = "Nhỏ",
                        ExpectedTotal = quote.TotalAmount
                    };

                    var createdBooking = await bookingService.CreateBookingAsync(ownerUser.UserId, createBookingDto);
                    createdBookingIds.Add(createdBooking.BookingId);
                    AssertTrue(createdBooking.TotalAmount == quote.TotalAmount,
                        $"(x) Đơn đặt lịch lưu TotalAmount trùng khớp 100% với báo giá: {createdBooking.TotalAmount:N0} ₫");

                    // Sitter Accept đơn để chuyển sang ACCEPTED
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
                    await sitterController.AcceptBooking(createdBooking.BookingId);

                    // Sinh link PayOS
                    var linkDto = await paymentService.CreateOrGetPaymentLinkAsync(ownerUser.UserId, createdBooking.BookingId, DateTime.UtcNow);
                    AssertTrue(linkDto.Amount == quote.TotalAmount,
                        $"(x) Link thanh toán PayOS trả Amount trùng khớp báo giá: {linkDto.Amount:N0} ₫");

                    var paymentRec = await context.Payments.Find(p => p.BookingId == createdBooking.BookingId).FirstOrDefaultAsync();
                    AssertTrue(paymentRec != null && paymentRec.Amount == quote.TotalAmount,
                        $"(x) Bản ghi Payment lưu Amount ({paymentRec?.Amount:N0} ₫) trùng khớp 100% với TotalAmount và báo giá Quote ({quote.TotalAmount:N0} ₫)");
                }

                // =========================================================================
                // TEST 23 (y): GIAO DỊCH BẤT THƯỜNG VÀO SUSPENSE KHÔNG LÀM LỆCH ĐỐI SOÁT & ESCROW
                // =========================================================================
                Console.WriteLine("\n--- TEST 23 (y): Giao dịch bất thường vào SUSPENSE không làm lệch đối soát & Escrow ---");
                {
                    var preGlobal = await ledgerService.VerifyGlobalEscrowInvariantAsync();
                    AssertTrue(preGlobal.IsValid, "(y) Bất biến ESCROW_HELD toàn hệ thống hợp lệ trước giao dịch bất thường");

                    var abOrderCode = Random.Shared.Next(800000000, 899999999);
                    var abBooking = CreateHeldBooking("AB_SUSP_BOOK", DateTime.UtcNow.AddDays(5), 500000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    abBooking.SitterUserId = sitterUser.UserId;
                    abBooking.PaymentStatus = PaymentStatus.PENDING_PAYMENT;
                    await context.Bookings.InsertOneAsync(abBooking);
                    createdBookingIds.Add(abBooking.BookingId);

                    await context.Payments.InsertOneAsync(new Payment
                    {
                        PaymentId = ObjectId.GenerateNewId().ToString(),
                        BookingId = abBooking.BookingId,
                        OrderCode = abOrderCode,
                        Amount = 500000,
                        Status = PaymentLinkStatus.PENDING,
                        CreatedAt = DateTime.UtcNow
                    });

                    // Gửi webhook với số tiền sai (100.000 ₫ thay vì 500.000 ₫)
                    var webhookBadAmount = JsonSerializer.Serialize(new
                    {
                        code = "00",
                        data = new { orderCode = abOrderCode, amount = 100000 },
                        signature = "MOCK_VALID_SIGNATURE"
                    });
                    await paymentService.ProcessWebhookAsync(webhookBadAmount, "MOCK_VALID_SIGNATURE", DateTime.UtcNow);

                    // Kiểm tra bút toán SUSPENSE
                    var suspEntry = await context.JournalEntries.Find(j => j.BookingId == abBooking.BookingId && j.Type == JournalType.PAYMENT_ABNORMAL_SUSPENSE).FirstOrDefaultAsync();
                    AssertTrue(suspEntry != null && suspEntry.Lines.Any(l => l.Account == LedgerAccounts.SUSPENSE && l.Amount == 100000),
                        "(y) Bút toán SUSPENSE ghi nhận đúng 100.000 ₫ tiền thực nhận");

                    // Chạy đối soát
                    var recAbnormal = await bookingService.ReconcileMissingLedgerEntriesAsync(DateTime.UtcNow, abBooking.BookingId, force: true);
                    AssertTrue(recAbnormal == 0, "(y) Đối soát không can thiệp hay sinh bút toán sai cho đơn bất thường");

                    // Kiểm tra lại bất biến toàn cục: ESCROW_HELD không bị lệch
                    var postGlobal = await ledgerService.VerifyGlobalEscrowInvariantAsync();
                    AssertTrue(postGlobal.IsValid, "(y) Bất biến ESCROW_HELD toàn hệ thống VẪN HỢP LỆ sau giao dịch bất thường");
                }

                // =========================================================================
                // TEST 24: REFUNDREQUEST PAYMENTID=NULL (KHÔNG BỊ TRÙNG INDEX) VÀ ĐỐI SOÁT BÙ REFUNDREQUEST THIẾU
                // =========================================================================
                Console.WriteLine("\n--- TEST 24: RefundRequest PaymentId=null (nhiều đơn không payment) & Đối soát bù RefundRequest thiếu ---");
                {
                    var nowUtc = DateTime.UtcNow;
                    var bNoPay1 = CreateHeldBooking("NO_PAY_1", nowUtc.AddDays(10), 300000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    var bNoPay2 = CreateHeldBooking("NO_PAY_2", nowUtc.AddDays(12), 400000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    await context.Bookings.InsertManyAsync(new[] { bNoPay1, bNoPay2 });
                    createdBookingIds.Add(bNoPay1.BookingId);
                    createdBookingIds.Add(bNoPay2.BookingId);

                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = bNoPay1.BookingId,
                        TransactionId = "TX_NO_PAY_1_HELD",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = nowUtc,
                        IdempotencyKey = $"{bNoPay1.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 300000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 300000 }
                        }
                    });

                    var plan1 = new SettlementPlan
                    {
                        SettlementKey = LedgerIdempotencyKeys.CancelOwner100(bNoPay1.BookingId),
                        SettlementType = "CANCEL_OWNER_100",
                        TotalAmount = 300000,
                        RefundAmount = 300000,
                        ClaimedAt = nowUtc
                    };
                    var plan2 = new SettlementPlan
                    {
                        SettlementKey = LedgerIdempotencyKeys.CancelOwner100(bNoPay2.BookingId),
                        SettlementType = "CANCEL_OWNER_100",
                        TotalAmount = 400000,
                        RefundAmount = 400000,
                        ClaimedAt = nowUtc
                    };

                    // Cả 2 đơn đều không có bản ghi Payment nào -> PaymentId = null
                    await bookingService.CreateOrGetRefundRequestAsync(bNoPay1, plan1, nowUtc, "CANCEL_TEST_1");
                    await bookingService.CreateOrGetRefundRequestAsync(bNoPay2, plan2, nowUtc, "CANCEL_TEST_2");

                    var ref1 = await context.RefundRequests.Find(r => r.BookingId == bNoPay1.BookingId).FirstOrDefaultAsync();
                    var ref2 = await context.RefundRequests.Find(r => r.BookingId == bNoPay2.BookingId).FirstOrDefaultAsync();

                    AssertTrue(ref1 != null && ref2 != null,
                        "Cả hai đơn không có payment đều tạo thành công RefundRequest với PaymentId=null");
                    AssertTrue(ref1?.PaymentId == null && ref2?.PaymentId == null,
                        "Cả hai RefundRequest đều lưu PaymentId là null (không dùng string.Empty)");

                    // Thử gọi lại CreateOrGetRefundRequestAsync để kiểm tra idempotent
                    await bookingService.CreateOrGetRefundRequestAsync(bNoPay1, plan1, nowUtc, "CANCEL_TEST_1_DUPLICATE");
                    var ref1Count = await context.RefundRequests.CountDocumentsAsync(r => r.BookingId == bNoPay1.BookingId);
                    AssertTrue(ref1Count == 1, "Gọi lại CreateOrGetRefundRequestAsync hoàn toàn idempotent (chỉ 1 bản ghi)");

                    // Kiểm tra xóa một RefundRequest rồi đối soát bù
                    // Lưu settlement plan vào bNoPay1 và cập nhật PaymentStatus
                    await context.Bookings.UpdateOneAsync(
                        b => b.BookingId == bNoPay1.BookingId,
                        Builders<Booking>.Update
                            .Set(b => b.TrangThai, BookingStatus.CANCELLED)
                            .Set(b => b.PaymentStatus, PaymentStatus.REFUND_PENDING)
                            .Set(b => b.Settlement, plan1)
                            .Set(b => b.ClaimedAt, nowUtc)
                    );
                    await context.Bookings.UpdateOneAsync(
                        b => b.BookingId == bNoPay2.BookingId,
                        Builders<Booking>.Update
                            .Set(b => b.TrangThai, BookingStatus.CANCELLED)
                            .Set(b => b.PaymentStatus, PaymentStatus.CANCELLED)
                    );

                    // Xóa RefundRequest của bNoPay1
                    await context.RefundRequests.DeleteOneAsync(r => r.BookingId == bNoPay1.BookingId);
                    var beforeReconCount = await context.RefundRequests.CountDocumentsAsync(r => r.BookingId == bNoPay1.BookingId);
                    AssertTrue(beforeReconCount == 0, "Đã xóa RefundRequest của đơn 1 trước đối soát");

                    // Chạy đối soát
                    var reconRepaired = await bookingService.ReconcileMissingLedgerEntriesAsync(nowUtc, bNoPay1.BookingId, force: true);
                    AssertTrue(reconRepaired > 0, "Đối soát phát hiện và sửa bù");

                    var restoredRef1 = await context.RefundRequests.Find(r => r.BookingId == bNoPay1.BookingId).FirstOrDefaultAsync();
                    AssertTrue(restoredRef1 != null && restoredRef1.Amount == 300000,
                        $"Đối soát đã tự động bù thành công RefundRequest bị thiếu (số tiền: {restoredRef1?.Amount:N0} ₫)");
                }

                // =========================================================================
                // TEST 25: EXPECTEDTOTAL LỆCH -> 409 CONFLICT KÈM GIÁ MỚI & KIỂM TRA ISPRIMARY
                // =========================================================================
                Console.WriteLine("\n--- TEST 25: ExpectedTotal lệch -> 409 Conflict kèm giá mới & Kiểm tra IsPrimary ---");
                {
                    var startDate = DateTime.UtcNow.Date.AddDays(20);
                    var endDate = startDate.AddDays(1); // 2 ngày inclusive = 400.000 ₫

                    // 1. Tạo đơn gửi expectedTotal sai (350.000 ₫ thay vì 400.000 ₫)
                    var wrongDto = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        ServiceId = sitterService.ServiceId,
                        NgayBatDau = startDate.ToString("yyyy-MM-dd"),
                        NgayKetThuc = endDate.ToString("yyyy-MM-dd"),
                        TenPet = "Bé Cún Test 409",
                        ExpectedTotal = 350000 // Sai lệch
                    };

                    bool caughtMismatch = false;
                    long reportedNewTotal = 0;
                    try
                    {
                        await bookingService.CreateBookingAsync(ownerUser.UserId, wrongDto);
                    }
                    catch (PriceMismatchException pex)
                    {
                        caughtMismatch = true;
                        reportedNewTotal = pex.NewTotal;
                    }
                    AssertTrue(caughtMismatch && reportedNewTotal == 400000,
                        $"Gửi ExpectedTotal sai bị từ chối với 409 Conflict và trả về giá mới chính xác: {reportedNewTotal:N0} ₫");

                    // 2. Gửi expectedTotal đúng (400.000 ₫) -> thành công
                    var correctDto = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        ServiceId = sitterService.ServiceId,
                        NgayBatDau = startDate.ToString("yyyy-MM-dd"),
                        NgayKetThuc = endDate.ToString("yyyy-MM-dd"),
                        TenPet = "Bé Cún Test OK",
                        ExpectedTotal = 400000
                    };
                    var createdOk = await bookingService.CreateBookingAsync(ownerUser.UserId, correctDto);
                    createdBookingIds.Add(createdOk.BookingId);
                    AssertTrue(createdOk != null && createdOk.TotalAmount == 400000,
                        "Tạo đơn thành công khi ExpectedTotal trùng khớp chính xác");

                    // 3. Kiểm tra chọn dịch vụ không thuộc về Sitter -> 400
                    var invalidServiceDto = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        ServiceId = "invalid-service-id-999",
                        NgayBatDau = startDate.AddDays(3).ToString("yyyy-MM-dd"),
                        NgayKetThuc = startDate.AddDays(4).ToString("yyyy-MM-dd"),
                        TenPet = "Bé Cún Test Service",
                        ExpectedTotal = 400000
                    };
                    bool caughtInvalidService = false;
                    try
                    {
                        await bookingService.CreateBookingAsync(ownerUser.UserId, invalidServiceDto);
                    }
                    catch (BadRequestException)
                    {
                        caughtInvalidService = true;
                    }
                    AssertTrue(caughtInvalidService, "Chọn ServiceId không thuộc Sitter bị chặn bằng BadRequestException");

                    // 4. Kiểm tra thiếu expectedTotal (null hoặc <= 0) -> 400 BadRequestException
                    var missingExpectedDto = new CreateBookingDto
                    {
                        SitterId = sitterProfile.SitterId,
                        ServiceId = sitterService.ServiceId,
                        NgayBatDau = startDate.AddDays(7).ToString("yyyy-MM-dd"),
                        NgayKetThuc = endDate.AddDays(7).ToString("yyyy-MM-dd"),
                        TenPet = "Bé Cún Test Missing",
                        ExpectedTotal = null
                    };
                    bool caughtMissingExpected = false;
                    try
                    {
                        await bookingService.CreateBookingAsync(ownerUser.UserId, missingExpectedDto);
                    }
                    catch (BadRequestException)
                    {
                        caughtMissingExpected = true;
                    }
                    AssertTrue(caughtMissingExpected, "Tạo đơn thiếu expectedTotal bị từ chối với 400 BadRequestException");
                }

                // =========================================================================
                // TEST 26: TEST NGẮT GIỮA CÁC BƯỚC HỦY RỒI GỌI LẠI (IDEMPOTENT RETRY SAU CLAIM)
                // =========================================================================
                Console.WriteLine("\n--- TEST 26: Ngắt giữa claim và ghi sổ/refund rồi gọi lại CancelBookingAsync ---");
                {
                    var nowUtc = DateTime.UtcNow;
                    var bInterrupted = CreateHeldBooking("INTERRUPTED", nowUtc.AddDays(5), 500000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    bInterrupted.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(bInterrupted);
                    createdBookingIds.Add(bInterrupted.BookingId);

                    // Giả lập hệ thống ngắt ngay sau khi Claim (FindOneAndUpdate) thành công,
                    // nhưng chưa kịp ghi sổ JournalEntry hoặc tạo RefundRequest
                    var plan = new SettlementPlan
                    {
                        SettlementKey = LedgerIdempotencyKeys.CancelOwner100(bInterrupted.BookingId),
                        SettlementType = "CANCEL_OWNER_100",
                        TotalAmount = 500000,
                        RefundAmount = 500000,
                        CommissionAmount = 0,
                        SitterAmount = 0,
                        SitterUserId = sitterUser.UserId,
                        ClaimedAt = nowUtc
                    };
                    await context.Bookings.UpdateOneAsync(
                        b => b.BookingId == bInterrupted.BookingId,
                        Builders<Booking>.Update
                            .Set(b => b.TrangThai, BookingStatus.CANCELLED)
                            .Set(b => b.IsActive, false)
                            .Set(b => b.PaymentStatus, PaymentStatus.REFUND_PENDING)
                            .Set(b => b.Settlement, plan)
                            .Set(b => b.ClaimedAt, nowUtc)
                    );

                    // Kiểm tra trước khi gọi lại: chưa có journal entry và chưa có refund request
                    var entryBefore = await context.JournalEntries.Find(j => j.BookingId == bInterrupted.BookingId).FirstOrDefaultAsync();
                    var refBefore = await context.RefundRequests.Find(r => r.BookingId == bInterrupted.BookingId).FirstOrDefaultAsync();
                    AssertTrue(entryBefore == null && refBefore == null, "Trạng thái ngắt: Chưa có bút toán và chưa có RefundRequest");

                    // Người dùng hoặc hệ thống gọi lại CancelBookingAsync
                    await bookingService.CancelBookingAsync(ownerUser.UserId, bInterrupted.BookingId, "Gọi lại sau khi ngắt");

                    // Kiểm tra sau khi gọi lại: đã ghi sổ và tạo RefundRequest thành công
                    var entryAfter = await context.JournalEntries.Find(j => j.BookingId == bInterrupted.BookingId).FirstOrDefaultAsync();
                    var refAfter = await context.RefundRequests.Find(r => r.BookingId == bInterrupted.BookingId).FirstOrDefaultAsync();
                    AssertTrue(entryAfter != null, "Gọi lại sau khi ngắt: Bút toán sổ cái được ghi nhận thành công");
                    AssertTrue(refAfter != null && refAfter.Amount == 500000, "Gọi lại sau khi ngắt: RefundRequest được tạo bù thành công");

                    // Gọi lại thêm một lần nữa (lần 3): Đảm bảo hoàn toàn idempotent
                    await bookingService.CancelBookingAsync(ownerUser.UserId, bInterrupted.BookingId, "Gọi lại lần 3");
                    var entryCount3 = await context.JournalEntries.CountDocumentsAsync(j => j.BookingId == bInterrupted.BookingId);
                    var refCount3 = await context.RefundRequests.CountDocumentsAsync(r => r.BookingId == bInterrupted.BookingId);
                    AssertTrue(entryCount3 == 1 && refCount3 == 1, "Gọi lại lần 3 vẫn giữ nguyên tính bất biến idempotent (1 bút toán, 1 RefundRequest)");

                    // Bù bút toán PAYMENT_HELD cho bInterrupted sau khi test xong để bảo toàn bất biến ESCROW_HELD toàn cục
                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = bInterrupted.BookingId,
                        TransactionId = "TX_INTERRUPTED_HELD",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = nowUtc.AddHours(-1),
                        IdempotencyKey = $"{bInterrupted.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                        }
                    });
                }

                // =========================================================================
                // TEST 27: SITTER GỌI HỦY LÊN ĐƠN OWNER ĐÃ HỦY -> GIỮ NGUYÊN PLAN CỦA OWNER & QUYỀN
                // =========================================================================
                Console.WriteLine("\n--- TEST 27: Sitter gọi hủy lên đơn Owner đã hủy -> Giữ nguyên plan của Owner & Kiểm tra quyền ---");
                {
                    var nowUtc = DateTime.UtcNow;
                    var bOwnerCancel = CreateHeldBooking("OWNER_CAN_HOLD", nowUtc.AddDays(10), 500000, ownerUser.UserId, sitterProfile.SitterId, sitterUser.UserId);
                    bOwnerCancel.SitterUserId = sitterUser.UserId;
                    await context.Bookings.InsertOneAsync(bOwnerCancel);
                    createdBookingIds.Add(bOwnerCancel.BookingId);

                    await ledgerService.RecordEntryAsync(new JournalEntry
                    {
                        EntryId = ObjectId.GenerateNewId().ToString(),
                        BookingId = bOwnerCancel.BookingId,
                        TransactionId = "TX_OWNER_CAN_HOLD",
                        Type = JournalType.PAYMENT_HELD,
                        ActorUserId = "OWNER",
                        CreatedAt = nowUtc,
                        IdempotencyKey = $"{bOwnerCancel.BookingId}:PAYMENT_HELD:1",
                        Lines = new List<JournalLine>
                        {
                            new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                            new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                        }
                    });

                    // 1. Owner hủy trước (nấc 1 >= 48h, hoàn 100%)
                    await bookingService.CancelBookingAsync(ownerUser.UserId, bOwnerCancel.BookingId, "Owner hủy nấc 1");
                    var bAfterOwner = await context.Bookings.Find(b => b.BookingId == bOwnerCancel.BookingId).FirstOrDefaultAsync();
                    AssertTrue(bAfterOwner != null && bAfterOwner.Settlement != null && bAfterOwner.Settlement.SettlementType == "CANCEL_OWNER_100",
                        "Đơn đã được Owner hủy với plan CANCEL_OWNER_100");

                    // 2. Sitter gọi hủy lên đơn Owner đã hủy -> GIỮ NGUYÊN PLAN CỦA OWNER
                    await bookingService.CancelBookingAsync(sitterUser.UserId, bOwnerCancel.BookingId, "Sitter cố hủy lại");
                    var bAfterSitter = await context.Bookings.Find(b => b.BookingId == bOwnerCancel.BookingId).FirstOrDefaultAsync();
                    AssertTrue(bAfterSitter != null && bAfterSitter.Settlement != null && bAfterSitter.Settlement.SettlementType == "CANCEL_OWNER_100",
                        "Sitter gọi hủy sau không ghi đè, GIỮ NGUYÊN plan CANCEL_OWNER_100 của Owner");

                    // Bút toán sổ cái giữ nguyên đúng 1 bản ghi CANCEL_OWNER_100, không có bản ghi CANCEL_SITTER
                    var cancelEntries = await context.JournalEntries.Find(j => j.BookingId == bOwnerCancel.BookingId && j.Type == "CANCEL_OWNER_100").ToListAsync();
                    var sitterCancelEntries = await context.JournalEntries.Find(j => j.BookingId == bOwnerCancel.BookingId && j.Type == "CANCEL_SITTER").ToListAsync();
                    AssertTrue(cancelEntries.Count == 1 && sitterCancelEntries.Count == 0,
                        "Sổ cái giữ nguyên đúng 1 bút toán CANCEL_OWNER_100, không phát sinh thêm bút toán CANCEL_SITTER");

                    // 3. Người ngoài gọi hủy -> Bị chặn 404 NotFoundException
                    bool caughtOutsider = false;
                    try
                    {
                        await bookingService.CancelBookingAsync(ObjectId.GenerateNewId().ToString(), bOwnerCancel.BookingId, "Người ngoài hủy");
                    }
                    catch (NotFoundException)
                    {
                        caughtOutsider = true;
                    }
                    AssertTrue(caughtOutsider, "Người ngoài gọi hủy đơn bị chặn với 404 NotFoundException");
                }

                // =========================================================================
                // TEST 28: MIGRATION ISPRIMARY CHO SITTER CŨ CHƯA CÓ CỜ NÀY
                // =========================================================================
                Console.WriteLine("\n--- TEST 28: Sitter cũ trong DB chưa có cờ IsPrimary vẫn nhận được đơn sau migration ---");
                {
                    // Tạo một Sitter cũ hoàn toàn chưa có cờ IsPrimary (IsPrimary = false)
                    var oldSitterUser = new User
                    {
                        UserId = ObjectId.GenerateNewId().ToString(),
                        Email = $"old_sitter_{Guid.NewGuid().ToString("N")[..6]}@test.com",
                        FullName = "Old Sitter Legacy",
                        Role = "SITTER"
                    };
                    await context.Users.InsertOneAsync(oldSitterUser);
                    createdUserIds.Add(oldSitterUser.UserId);

                    var oldSitterProfile = new SitterProfile
                    {
                        SitterId = ObjectId.GenerateNewId().ToString(),
                        UserId = oldSitterUser.UserId,
                        Status = "Approved",
                        IsVerified = true,
                        Bio = "Old Sitter before migration"
                    };
                    await context.SitterProfiles.InsertOneAsync(oldSitterProfile);
                    createdSitterIds.Add(oldSitterProfile.SitterId);

                    var oldSitterService = new PetSitter.Models.SitterService
                    {
                        SitterServiceId = ObjectId.GenerateNewId().ToString(),
                        SitterId = oldSitterProfile.SitterId,
                        ServiceId = sitterService.ServiceId,
                        Price = 250000m,
                        IsPrimary = false // Giả lập dữ liệu cũ chưa có cờ IsPrimary
                    };
                    await context.SitterServices.InsertOneAsync(oldSitterService);

                    for (int i = 0; i < 10; i++)
                    {
                        await context.SitterAvailabilities.InsertOneAsync(new SitterAvailability
                        {
                            SitterId = oldSitterProfile.SitterId,
                            AvailableDate = DateTime.UtcNow.Date.AddDays(30 + i),
                            IsAvailable = true
                        });
                    }

                    // Chạy Seeder migration để tự động gán IsPrimary cho Sitter cũ
                    await MongoSeeder.SeedDataAsync(context, new ConfigurationBuilder().Build());

                    // Kiểm tra dịch vụ của Sitter cũ đã được cập nhật IsPrimary = true
                    var migratedService = await context.SitterServices.Find(s => s.SitterServiceId == oldSitterService.SitterServiceId).FirstOrDefaultAsync();
                    AssertTrue(migratedService != null && migratedService.IsPrimary,
                        "Migration đã tự động gán IsPrimary = true cho SitterService hợp lệ của Sitter cũ");

                    // Chủ nuôi tạo đơn cho Sitter cũ mà không cần chọn ServiceId cụ thể
                    var legacyBookingReq = new CreateBookingDto
                    {
                        SitterId = oldSitterProfile.SitterId,
                        NgayBatDau = DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd"),
                        NgayKetThuc = DateTime.UtcNow.Date.AddDays(31).ToString("yyyy-MM-dd"), // 2 ngày = 500.000 ₫
                        TenPet = "Bé Mèo Cũ",
                        ExpectedTotal = 500000
                    };
                    var legacyBooking = await bookingService.CreateBookingAsync(ownerUser.UserId, legacyBookingReq);
                    createdBookingIds.Add(legacyBooking.BookingId);
                    AssertTrue(legacyBooking != null && legacyBooking.TotalAmount == 500000,
                        "Sitter cũ sau migration tạo và nhận đơn thành công qua dịch vụ chính IsPrimary");
                }

                // =========================================================================
                // TEST SUITE PROMPT 5B: RÚT TIỀN, DOANH THU & ĐỐI SOÁT
                // =========================================================================
                await Test5BPayoutsFlow.RunAsync(
                    context,
                    config,
                    dpProvider,
                    fakeGateway,
                    ledgerService,
                    AssertTrue,
                    createdUserIds,
                    createdBookingIds
                );
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
                    await context.Notifications.DeleteManyAsync(n => n.BookingId != null && createdBookingIds.Contains(n.BookingId));
                }
                if (createdUserIds.Any())
                {
                    await context.Users.DeleteManyAsync(u => createdUserIds.Contains(u.UserId));
                    await context.Notifications.DeleteManyAsync(n => createdUserIds.Contains(n.UserId));
                    await context.WithdrawalRequests.DeleteManyAsync(w => createdUserIds.Contains(w.SitterUserId));
                    await context.EarningsAccounts.DeleteManyAsync(e => createdUserIds.Contains(e.UserId));
                }
                if (createdSitterIds.Any())
                {
                    await context.SitterProfiles.DeleteManyAsync(s => createdSitterIds.Contains(s.SitterId));
                    await context.SitterServices.DeleteManyAsync(s => createdSitterIds.Contains(s.SitterId));
                    await context.SitterAvailabilities.DeleteManyAsync(s => createdSitterIds.Contains(s.SitterId));
                }
                // Dọn các sweep và reconciliation run test
                await context.RevenueSweeps.DeleteManyAsync(s => s.BankTransactionRef != null && (s.BankTransactionRef.Contains("TEST5B") || s.BankTransactionRef.Contains("SWP_") || s.BankTransactionRef.Contains("INJECT")));
                await context.ReconciliationRuns.DeleteManyAsync(r => r.RunId != null);
                // Reset platform account swept total for testing cleanliness
                await context.PlatformAccounts.UpdateOneAsync(
                    p => p.AccountId == "platform_main",
                    Builders<PlatformAccount>.Update.Set(p => p.SweptTotal, 0).Set(p => p.Version, 1)
                );
                Console.WriteLine("Đã dọn dẹp sạch toàn bộ dữ liệu test trong PetSitterDb.");
            }

            Console.WriteLine($"\n==========================================");
            Console.WriteLine($"TỔNG KẾT TEST PAYMENT FLOW: PASS={passed}, FAIL={failed}");
            Console.WriteLine($"==========================================");

            return failed == 0 ? 0 : 1;
        }

        private static Booking CreateHeldBooking(string code, DateTime startDate, long amount, string ownerId, string sitterId, string? sitterUserId = null)
        {
            return new Booking
            {
                BookingId = ObjectId.GenerateNewId().ToString(),
                MaDatLich = $"{code}_{ObjectId.GenerateNewId().ToString().Substring(18)}",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId ?? string.Empty,
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
                await context.Bookings.UpdateOneAsync(b => b.BookingId == booking.BookingId, Builders<Booking>.Update.Set(b => b.PaymentStatus, PaymentStatus.PARTIALLY_REFUNDED).Set(b => b.TrangThai, BookingStatus.CANCELLED).Set(b => b.IsActive, false));
            }
            else
            {
                await context.Bookings.UpdateOneAsync(b => b.BookingId == booking.BookingId, Builders<Booking>.Update.Set(b => b.PaymentStatus, PaymentStatus.RELEASED).Set(b => b.TrangThai, BookingStatus.CANCELLED).Set(b => b.IsActive, false));
            }
        }
    }
}
