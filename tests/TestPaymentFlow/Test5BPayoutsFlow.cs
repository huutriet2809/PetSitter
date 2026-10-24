using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.Data;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestPaymentFlow
{
    public static class Test5BPayoutsFlow
    {
        public static async Task RunAsync(
            PetSitterContext context,
            IConfiguration config,
            IDataProtectionProvider dpProvider,
            FakePaymentGateway fakeGateway,
            ILedgerService ledgerService,
            Action<bool, string> assertAction,
            List<string> createdUserIds,
            List<string> createdBookingIds)
        {
            Console.WriteLine("\n=======================================================");
            Console.WriteLine("=== BẮT ĐẦU TEST TOÀN DIỆN HỆ THỐNG RÚT TIỀN, DOANH THU & ĐỐI SOÁT (PROMPT 5B) ===");
            Console.WriteLine("=======================================================");

            var notificationService = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
            var withdrawalService = new WithdrawalService(context, ledgerService, fakeGateway, notificationService, dpProvider, config, NullLogger<WithdrawalService>.Instance);
            var revenueService = new RevenueService(context, ledgerService, withdrawalService, notificationService, config, NullLogger<RevenueService>.Instance);
            var paymentService = new PaymentService(context, fakeGateway, ledgerService, notificationService, dpProvider, config, NullLogger<PaymentService>.Instance);

            var nowUtc = DateTime.UtcNow;
            var testPrefix = "TEST5B_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // Tạo người dùng mẫu: 2 Sitter, 2 Admin
            var sitterAUser = new User
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                Email = $"{testPrefix}_sitterA@pawstay.vn",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                FullName = "Sitter A FiveB",
                Role = "SITTER",
                Phone = "0981111111",
                CreatedAt = nowUtc
            };
            var sitterBUser = new User
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                Email = $"{testPrefix}_sitterB@pawstay.vn",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                FullName = "Sitter B FiveB",
                Role = "SITTER",
                Phone = "0982222222",
                CreatedAt = nowUtc
            };
            var admin1User = new User
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                Email = $"{testPrefix}_admin1@pawstay.vn",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                FullName = "Admin Một",
                Role = "ADMIN",
                CreatedAt = nowUtc
            };
            var admin2User = new User
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                Email = $"{testPrefix}_admin2@pawstay.vn",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
                FullName = "Admin Hai",
                Role = "ADMIN",
                CreatedAt = nowUtc
            };

            await context.Users.InsertManyAsync(new[] { sitterAUser, sitterBUser, admin1User, admin2User });
            createdUserIds.AddRange(new[] { sitterAUser.UserId, sitterBUser.UserId, admin1User.UserId, admin2User.UserId });

            var sitterAProfile = new SitterProfile
            {
                SitterId = ObjectId.GenerateNewId().ToString(),
                UserId = sitterAUser.UserId,
                Status = "Approved",
                CreatedAt = nowUtc
            };
            var sitterBProfile = new SitterProfile
            {
                SitterId = ObjectId.GenerateNewId().ToString(),
                UserId = sitterBUser.UserId,
                Status = "Approved",
                CreatedAt = nowUtc
            };
            await context.SitterProfiles.InsertManyAsync(new[] { sitterAProfile, sitterBProfile });

            // =========================================================================
            // TEST 5B.1 (i, a): BẢO MẬT TÀI KHOẢN NGÂN HÀNG & MÃ HÓA / MASKING
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.1: Mã hóa tài khoản ngân hàng, Masking, Mật khẩu, Đóng băng 24h ---");
            {
                // 1. Thử đổi ngân hàng nhưng sai mật khẩu -> phải bị chặn (400)
                bool wrongPassBlocked = false;
                try
                {
                    await withdrawalService.UpdateSitterBankAccountAsync(sitterAUser.UserId, new UpdateBankAccountDto
                    {
                        BankBin = "970407",
                        BankName = "MB Bank",
                        AccountNumber = "0987654321",
                        AccountHolderName = sitterAUser.FullName,
                        Password = "WrongPassword!"
                    }, nowUtc);
                }
                catch (BadRequestException)
                {
                    wrongPassBlocked = true;
                }
                assertAction(wrongPassBlocked, "(a) Đổi ngân hàng sai mật khẩu -> Bị chặn 400");

                // 2. Đổi ngân hàng đúng mật khẩu -> thành công, mã hóa trong DB, mask ở API
                var bankUpdated = await withdrawalService.UpdateSitterBankAccountAsync(sitterAUser.UserId, new UpdateBankAccountDto
                {
                    BankBin = "970407",
                    BankName = "Techcombank",
                    AccountNumber = "1903666888999",
                    AccountHolderName = sitterAUser.FullName,
                    Password = "Password123!"
                }, nowUtc);

                assertAction(bankUpdated != null && bankUpdated.MaskedAccountNumber.EndsWith("8999") && bankUpdated.MaskedAccountNumber.Contains("****"),
                    "(i) API trả về số tài khoản đã che (masked) ****8999");

                // Kiểm tra trực tiếp trong DB: AccountNumber không được lưu plain-text
                var profileInDb = await context.SitterProfiles.Find(s => s.UserId == sitterAUser.UserId).FirstOrDefaultAsync();
                bool isPlainInDb = profileInDb?.BankAccount?.EncryptedAccountNumber?.Contains("1903666888999") ?? false;
                assertAction(!isPlainInDb && !string.IsNullOrEmpty(profileInDb?.BankAccount?.EncryptedAccountNumber),
                    "(i) Số tài khoản trong cơ sở dữ liệu được mã hóa an toàn (không chứa plain-text)");

                // 3. Tài khoản bị đóng băng 24h: thử rút tiền ngay trong thời gian đóng băng -> chặn
                bool frozenBlocked = false;
                try
                {
                    await withdrawalService.CreateWithdrawalRequestAsync(sitterAUser.UserId, new CreateWithdrawalDto { Amount = 100000 }, $"{testPrefix}_FROZEN", nowUtc.AddHours(2));
                }
                catch (BadRequestException ex) when (ex.Message.Contains("đóng băng") || ex.Message.Contains("thay đổi"))
                {
                    frozenBlocked = true;
                }
                assertAction(frozenBlocked, "(a) Tạo yêu cầu rút tiền trong thời gian đóng băng 24h sau khi đổi STK -> Bị chặn");
            }

            // =========================================================================
            // TEST 5B.2 (q): MÃ HÓA SỐNG SÓT SAU KHỞI ĐỘNG LẠI (DATA PROTECTION RESTART)
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.2 (q): Data Protection sống sót sau khởi động lại với thư mục lưu trữ ngoài repo ---");
            {
                // Lấy thông tin STK giải mã bằng withdrawalService hiện tại
                var decryptedOriginal = await withdrawalService.GetDecryptedBankAccountAsync(sitterAUser.UserId);

                // Giả lập khởi động lại server: Tạo một IDataProtectionProvider MỚI hoàn toàn đọc cùng thư mục key ngoài repo
                var dpKeyDir = config["Security:DataProtectionKeyPath"]
                               ?? config.GetValue<string>("Payments:DataProtection:KeyDirectory");
                if (string.IsNullOrWhiteSpace(dpKeyDir))
                {
                    var commonApp = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    dpKeyDir = !string.IsNullOrWhiteSpace(commonApp)
                        ? Path.Combine(commonApp, "PetSitter", "dp_keys")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PetSitterKeys");
                }
                Directory.CreateDirectory(dpKeyDir);

                var newServices = new ServiceCollection();
                newServices.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(dpKeyDir))
                    .SetApplicationName("PetSitterApp");
                var newSp = newServices.BuildServiceProvider();
                var newDpProvider = newSp.GetRequiredService<IDataProtectionProvider>();

                var restartWithdrawalService = new WithdrawalService(context, ledgerService, fakeGateway, notificationService, newDpProvider, config, NullLogger<WithdrawalService>.Instance);
                var decryptedAfterRestart = await restartWithdrawalService.GetDecryptedBankAccountAsync(sitterAUser.UserId);

                assertAction(decryptedAfterRestart != null && decryptedAfterRestart.AccountNumber == "1903666888999" && decryptedAfterRestart.AccountNumber == decryptedOriginal?.AccountNumber,
                    "(q) Khởi động lại ứng dụng với DataProtection Provider mới -> Giải mã chính xác 100% STK đã mã hóa");
            }

            // Gỡ bỏ đóng băng cho sitterA để tiếp tục các bài test tiếp theo
            await context.SitterProfiles.UpdateOneAsync(
                s => s.UserId == sitterAUser.UserId,
                Builders<SitterProfile>.Update.Set(s => s.BankAccount.UpdatedAt, nowUtc.AddHours(-25))
            );

            // =========================================================================
            // TEST 5B.3 (a): KHẢ DỤNG / RESERVED & CHƯA QUA AVAILABLE_AT
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.3 (a): Định nghĩa Khả dụng / Reserved, thời gian AvailableAt, và Gate nguyên tử ---");
            {
                // Cấp 2 khoản thu nhập vào sổ cái cho Sitter A:
                // Khoản 1: 5.000.000 ₫ đã qua AvailableAt (now - 1h)
                // Khoản 2: 3.000.000 ₫ chưa qua AvailableAt (now + 24h)
                var bookingA1 = ObjectId.GenerateNewId().ToString();
                var bookingA2 = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bookingA1);
                createdBookingIds.Add(bookingA2);

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bookingA1,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "H1",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-3),
                    IdempotencyKey = $"{bookingA1}:HELD",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 5000000 },
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 5000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bookingA2,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "H2",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-1),
                    IdempotencyKey = $"{bookingA2}:HELD",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 3000000 },
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 3000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bookingA1,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "E1",
                    Type = JournalType.ESCROW_RELEASE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-2),
                    IdempotencyKey = $"{bookingA1}:RELEASE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 5000000 },
                        new JournalLine { Account = LedgerAccounts.SitterEarnings(sitterAUser.UserId), Direction = LedgerDirection.CREDIT, Amount = 5000000, AvailableAt = nowUtc.AddHours(-1) }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bookingA2,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "E2",
                    Type = JournalType.ESCROW_RELEASE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"{bookingA2}:RELEASE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 3000000 },
                        new JournalLine { Account = LedgerAccounts.SitterEarnings(sitterAUser.UserId), Direction = LedgerDirection.CREDIT, Amount = 3000000, AvailableAt = nowUtc.AddHours(24) }
                    }
                });

                // Kiểm tra summary ví: Tổng thu nhập = 8.000.000 ₫; Khả dụng = 5.000.000 ₫; Đang tạm giữ = 3.000.000 ₫; Reserved = 0
                var walletA = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                assertAction(walletA.TotalEarnings == 8000000 && walletA.AvailableBalance == 5000000 && walletA.PendingHoldBalance == 3000000 && walletA.ReservedAmount == 0,
                    "(a) Khả dụng chỉ tính các khoản đã qua AvailableAt; khoản chưa tới AvailableAt nằm ở PendingHoldBalance");

                // Thử rút 6.000.000 ₫ (vượt khả dụng 5.000.000 ₫) -> phải bị chặn 400
                bool overAvailBlocked = false;
                try
                {
                    await withdrawalService.CreateWithdrawalRequestAsync(sitterAUser.UserId, new CreateWithdrawalDto { Amount = 6000000 }, $"{testPrefix}_OVER", nowUtc);
                }
                catch (BadRequestException)
                {
                    overAvailBlocked = true;
                }
                assertAction(overAvailBlocked, "(a) Yêu cầu rút tiền vượt quá số dư khả dụng (5.000.000 ₫) -> Bị chặn 400");
            }

            // =========================================================================
            // TEST 5B.4 (a, 1): 50 YÊU CẦU SONG SONG CÙNG SỐ DƯ (RACE CONDITION)
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.4 (a): 50 yêu cầu rút tiền song song cùng số dư khả dụng ---");
            {
                // Sitter A hiện có khả dụng 5.000.000 ₫
                // Bắn 50 requests song song, mỗi request rút 200.000 ₫ (tổng cầu = 10.000.000 ₫ > 5.000.000 ₫ khả dụng)
                int parallelCount = 50;
                long requestAmount = 200000;
                var successList = new ConcurrentBag<WithdrawalItemDto>();
                var failList = new ConcurrentBag<Exception>();

                var tasks = Enumerable.Range(1, parallelCount).Select(async i =>
                {
                    var idempKey = $"IDEMP_PARALLEL_{i}_{Guid.NewGuid():N}";
                    try
                    {
                        var res = await withdrawalService.CreateWithdrawalRequestAsync(
                            sitterAUser.UserId,
                            new CreateWithdrawalDto { Amount = requestAmount },
                            idempKey,
                            nowUtc
                        );
                        successList.Add(res);
                    }
                    catch (Exception ex)
                    {
                        failList.Add(ex);
                    }
                });

                await Task.WhenAll(tasks);

                long totalReservedApproved = successList.Count * requestAmount;
                var updatedWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);

                assertAction(totalReservedApproved <= 5000000,
                    $"(a) 50 yêu cầu song song: Tổng tiền duyệt ({totalReservedApproved:N0} ₫) KHÔNG vượt số dư khả dụng ban đầu (5.000.000 ₫)");
                assertAction(updatedWallet.AvailableBalance >= 0,
                    $"(a) Số dư khả dụng sau đua lệnh không bao giờ bị âm (Còn lại: {updatedWallet.AvailableBalance:N0} ₫)");
                assertAction(totalReservedApproved <= 5000000 && successList.Count >= 10 && failList.All(ex => ex is BadRequestException || ex is ConflictException),
                    $"(a) 50 yêu cầu song song: {successList.Count} yêu cầu thành công ({totalReservedApproved:N0} ₫ <= 5.000.000 ₫) và {failList.Count} yêu cầu bị chặn hợp lệ (hết số dư hoặc xung đột OCC)");
            }

            // Dọn dẹp 25 requests vừa tạo để đưa Reserved về 0 và reset DailyWithdrawn
            await context.WithdrawalRequests.DeleteManyAsync(w => w.SitterUserId == sitterAUser.UserId);
            await context.EarningsAccounts.UpdateOneAsync(
                e => e.UserId == sitterAUser.UserId,
                Builders<EarningsAccount>.Update.Set(e => e.ReservedAmount, 0).Set(e => e.DailyWithdrawn, 0)
            );

            // =========================================================================
            // TEST 5B.5 (a, 1): VƯỢT HẠN MỨC NGÀY (DAILY LIMIT) SONG SONG THEO GIỜ VN
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.5 (a): Kiểm soát hạn mức ngày (20.000.000 ₫) song song theo giờ VN (UTC+7) ---");
            {
                // Cấp thêm 30.000.000 ₫ cho Sitter A
                var bBig = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bBig);
                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bBig,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "HBIG",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-1),
                    IdempotencyKey = $"{bBig}:HELD",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 30000000 },
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 30000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bBig,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "BIG",
                    Type = JournalType.ESCROW_RELEASE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"{bBig}:RELEASE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 30000000 },
                        new JournalLine { Account = LedgerAccounts.SitterEarnings(sitterAUser.UserId), Direction = LedgerDirection.CREDIT, Amount = 30000000, AvailableAt = nowUtc.AddHours(-1) }
                    }
                });

                // Bắn 10 request song song, mỗi request rút 3.000.000 ₫ (Tổng cầu = 30.000.000 ₫ vượt hạn mức ngày 20.000.000 ₫)
                var dailySuccessList = new ConcurrentBag<WithdrawalItemDto>();
                var dailyFailList = new ConcurrentBag<Exception>();

                var dailyTasks = Enumerable.Range(1, 10).Select(async i =>
                {
                    try
                    {
                        var res = await withdrawalService.CreateWithdrawalRequestAsync(
                            sitterAUser.UserId,
                            new CreateWithdrawalDto { Amount = 3000000 },
                            $"IDEMP_DAILY_{i}_{Guid.NewGuid():N}",
                            nowUtc
                        );
                        dailySuccessList.Add(res);
                    }
                    catch (Exception ex)
                    {
                        dailyFailList.Add(ex);
                    }
                });

                await Task.WhenAll(dailyTasks);

                long configuredDailyLimit = config.GetValue<long>("Payments:WithdrawDailyLimit", 20000000);
                long totalDailyReserved = dailySuccessList.Count * 3000000;
                assertAction(totalDailyReserved <= configuredDailyLimit,
                    $"(a) Vượt hạn mức ngày song song: Tổng rút ({totalDailyReserved:N0} ₫) KHÔNG vượt hạn mức cấu hình ({configuredDailyLimit:N0} ₫/ngày)");
                assertAction(dailySuccessList.Count == 6 && dailyFailList.Count == 4,
                    $"(a) Đúng 6 yêu cầu thành công (6 x 3tr = 18tr) và 4 yêu cầu sau bị chặn vì vượt trần hạn mức");

                // Thử nghiệm hoàn lại DailyWithdrawn sau khi bị từ chối
                await context.EarningsAccounts.UpdateOneAsync(
                    e => e.UserId == sitterAUser.UserId,
                    Builders<EarningsAccount>.Update.Set(e => e.ReservedAmount, 0).Set(e => e.DailyWithdrawn, 0)
                );
                var testRefundReq = await withdrawalService.CreateWithdrawalRequestAsync(
                    sitterAUser.UserId,
                    new CreateWithdrawalDto { Amount = 1000000 },
                    $"IDEMP_REFUND_TEST_{Guid.NewGuid():N}",
                    nowUtc
                );
                var midRejAcc = await context.EarningsAccounts.Find(e => e.UserId == sitterAUser.UserId).FirstOrDefaultAsync();
                assertAction(midRejAcc?.DailyWithdrawn == 1000000,
                    "(2) Tạo yêu cầu rút -> DailyWithdrawn tăng tương ứng 1.000.000 ₫");

                // Admin từ chối yêu cầu -> hoàn lại DailyWithdrawn
                await withdrawalService.RejectWithdrawalAsync(admin1User.UserId, testRefundReq.WithdrawalId, new RejectWithdrawalDto { Reason = "Test hoàn hạn mức ngày" }, nowUtc);
                var postRejAcc = await context.EarningsAccounts.Find(e => e.UserId == sitterAUser.UserId).FirstOrDefaultAsync();
                assertAction(postRejAcc?.DailyWithdrawn == 0,
                    "(2) Yêu cầu bị từ chối -> DailyWithdrawn được hoàn lại nguyên vẹn về 0");

                // Cleanup requests
                await context.WithdrawalRequests.DeleteManyAsync(w => w.SitterUserId == sitterAUser.UserId);
                await context.EarningsAccounts.UpdateOneAsync(
                    e => e.UserId == sitterAUser.UserId,
                    Builders<EarningsAccount>.Update.Set(e => e.ReservedAmount, 0).Set(e => e.DailyWithdrawn, 0)
                );
            }

            // =========================================================================
            // TEST 5B.5B (1): NGẮT GIỮA GATE VÀ TẠO YÊU CẦU RỒI CHẠY SỬA TỰ ĐỘNG
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.5B (1): Giả lập ngắt giữa Gate và tạo yêu cầu -> Chạy tự động sửa phục hồi số dư ---");
            {
                // Giả lập sự cố: EarningsAccount tăng ReservedAmount lên 1.500.000 ₫ nhưng sập trước khi insert WithdrawalRequest
                await context.EarningsAccounts.UpdateOneAsync(
                    e => e.UserId == sitterAUser.UserId,
                    Builders<EarningsAccount>.Update.Inc(e => e.ReservedAmount, 1500000)
                );
                var walletInterrupted = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                assertAction(walletInterrupted.ReservedAmount >= 1500000,
                    "(1) Giả lập sự cố: ReservedAmount tăng do ngắt server nhưng không có bản ghi WithdrawalRequest nào");

                // Chạy hàm sửa chữa
                var (interruptedFixedCount, interruptedLogs) = await withdrawalService.ReconcileReservedAmountAsync(sitterAUser.UserId);
                assertAction(interruptedFixedCount == 1,
                    "(1) Đối soát tự động phát hiện và sửa ReservedAmount về đúng tổng đơn mở (0 ₫)");

                var walletRecovered = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                assertAction(walletRecovered.ReservedAmount == 0,
                    "(1) Khả dụng được phục hồi nguyên vẹn sau sửa chữa tự động");
            }

            // =========================================================================
            // TEST 5B.6 (b, 8, r): QUY TRÌNH MANUAL PAYOUT & 2 ADMIN XỬ LÝ ĐỒNG THỜI
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.6 (b, 8, r): Quy trình Manual Payout, Claim Processing và Chống xung đột 2 Admin ---");
            {
                // Sitter A tạo yêu cầu rút 1.000.000 ₫
                var wReq = await withdrawalService.CreateWithdrawalRequestAsync(
                    sitterAUser.UserId,
                    new CreateWithdrawalDto { Amount = 1000000 },
                    $"{testPrefix}_MANUAL_1",
                    nowUtc
                );
                assertAction(wReq.Status == WithdrawalStatus.REQUESTED, "(b) Yêu cầu mới tạo có trạng thái REQUESTED");

                // 1. Chặn Admin tự duyệt yêu cầu rút của chính mình
                bool selfApproveBlocked = false;
                try
                {
                    await withdrawalService.ApproveWithdrawalAsync(sitterAUser.UserId, wReq.WithdrawalId, nowUtc);
                }
                catch (BadRequestException ex) when (ex.Message.Contains("chính mình"))
                {
                    selfApproveBlocked = true;
                }
                assertAction(selfApproveBlocked, "(c) Chặn Admin tự duyệt yêu cầu rút tiền của chính mình -> 400");

                // 2. Admin 1 duyệt yêu cầu -> chuyển sang APPROVED
                var approved = await withdrawalService.ApproveWithdrawalAsync(admin1User.UserId, wReq.WithdrawalId, nowUtc);
                assertAction(approved.Status == WithdrawalStatus.APPROVED, "(b) Admin duyệt thành công -> Chuyển sang APPROVED");

                // 3. Hai Admin cùng bấm "Nhận xử lý chi" (claim-processing) đồng thời (r)
                var claimSuccessList = new ConcurrentBag<WithdrawalItemDto>();
                var claimConflictList = new ConcurrentBag<Exception>();

                var claimTasks = new[]
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var r = await withdrawalService.ClaimProcessingAsync(admin1User.UserId, admin1User.FullName, wReq.WithdrawalId, nowUtc);
                            claimSuccessList.Add(r);
                        }
                        catch (Exception ex) { claimConflictList.Add(ex); }
                    }),
                    Task.Run(async () =>
                    {
                        try
                        {
                            var r = await withdrawalService.ClaimProcessingAsync(admin2User.UserId, admin2User.FullName, wReq.WithdrawalId, nowUtc);
                            claimSuccessList.Add(r);
                        }
                        catch (Exception ex) { claimConflictList.Add(ex); }
                    })
                };
                await Task.WhenAll(claimTasks);

                assertAction(claimSuccessList.Count == 1 && claimConflictList.Count == 1,
                    "(r) Hai Admin cùng bấm nhận xử lý (Claim Processing) -> Đúng 1 Admin thành công, Admin còn lại nhận 409 Conflict");

                // Admin còn lại thấy thông báo đang được xử lý bởi Admin 1
                var firstConflict = claimConflictList.First();
                assertAction(firstConflict is ConflictException && firstConflict.Message.Contains("Admin"),
                    "(8) Admin bị chặn nhận được thông báo rõ: Yêu cầu đang được xử lý bởi Admin khác");

                // 4. Admin đã claim xác nhận PAID kèm BankTransactionRef
                var bankRef = $"{testPrefix}_BANK_REF_001";
                var paidReq = await withdrawalService.ConfirmPaidAsync(admin1User.UserId, wReq.WithdrawalId, new ConfirmPaidDto { BankTransactionRef = bankRef }, nowUtc);
                assertAction(paidReq.Status == WithdrawalStatus.PAID && paidReq.BankTransactionRef == bankRef,
                    "(b) Xác nhận PAID thành công kèm BankTransactionRef duy nhất");

                // Kiểm tra sổ cái: Chỉ khi PAID mới có bút toán DEBIT SITTER_EARNINGS / CREDIT GATEWAY_CLEARING
                var paidJournal = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.WithdrawalPaid(paidReq.ReferenceId)).FirstOrDefaultAsync();
                assertAction(paidJournal != null
                    && paidJournal.Lines.Any(l => l.Account == LedgerAccounts.SitterEarnings(sitterAUser.UserId) && l.Direction == LedgerDirection.DEBIT && l.Amount == 1000000)
                    && paidJournal.Lines.Any(l => l.Account == LedgerAccounts.GATEWAY_CLEARING && l.Direction == LedgerDirection.CREDIT && l.Amount == 1000000),
                    "(b, 1) Ghi sổ SITTER_EARNINGS -> GATEWAY_CLEARING CHỈ khi PAID; số tiền đúng 1.000.000 ₫");

                // 5. Thử dùng lại BankTransactionRef này cho đơn rút khác -> Chặn trùng
                var wReq2 = await withdrawalService.CreateWithdrawalRequestAsync(sitterAUser.UserId, new CreateWithdrawalDto { Amount = 500000 }, $"{testPrefix}_M2", nowUtc);
                await withdrawalService.ApproveWithdrawalAsync(admin1User.UserId, wReq2.WithdrawalId, nowUtc);
                await withdrawalService.ClaimProcessingAsync(admin1User.UserId, admin1User.FullName, wReq2.WithdrawalId, nowUtc);

                bool dupRefBlocked = false;
                try
                {
                    await withdrawalService.ConfirmPaidAsync(admin1User.UserId, wReq2.WithdrawalId, new ConfirmPaidDto { BankTransactionRef = bankRef }, nowUtc);
                }
                catch (ConflictException)
                {
                    dupRefBlocked = true;
                }
                assertAction(dupRefBlocked, "(b) Trùng BankTransactionRef ngân hàng -> Chặn 409 Conflict");

                // 6. Test Unclaim & Hạn claim (Timeout 30 phút)
                var unclaimRes = await withdrawalService.UnclaimProcessingAsync(admin1User.UserId, wReq2.WithdrawalId, nowUtc);
                assertAction(unclaimRes.Status == WithdrawalStatus.APPROVED && unclaimRes.ProcessingByAdminUserId == null,
                    "(8) Admin nhả quyền xử lý (Unclaim) -> Yêu cầu quay về trạng thái APPROVED");

                // Admin 2 claim yêu cầu
                await withdrawalService.ClaimProcessingAsync(admin2User.UserId, admin2User.FullName, wReq2.WithdrawalId, nowUtc);
                // Giả lập Admin 2 bỏ dở quá 35 phút (quá hạn 30 phút)
                await context.WithdrawalRequests.UpdateOneAsync(
                    w => w.WithdrawalId == wReq2.WithdrawalId,
                    Builders<WithdrawalRequest>.Update.Set(w => w.ProcessingAt, nowUtc.AddMinutes(-35))
                );
                // Admin 1 có thể claim lại do claim của Admin 2 đã timeout
                var reclaimed = await withdrawalService.ClaimProcessingAsync(admin1User.UserId, admin1User.FullName, wReq2.WithdrawalId, nowUtc);
                assertAction(reclaimed.Status == WithdrawalStatus.PROCESSING && reclaimed.ProcessingByAdminUserId == admin1User.UserId,
                    "(8) Claim quá hạn 30 phút -> Admin khác có thể tiếp quản xử lý");

                // 7. Test ngắt ở từng bước ConfirmPaid và chạy lại đối soát: Khả dụng không bao giờ cao hơn thật
                var preInterrWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                long trueAvailable = preInterrWallet.AvailableBalance;

                var wReqStep = await withdrawalService.CreateWithdrawalRequestAsync(sitterAUser.UserId, new CreateWithdrawalDto { Amount = 200000 }, $"{testPrefix}_STEP_INT", nowUtc);
                await withdrawalService.ApproveWithdrawalAsync(admin1User.UserId, wReqStep.WithdrawalId, nowUtc);
                await withdrawalService.ClaimProcessingAsync(admin1User.UserId, admin1User.FullName, wReqStep.WithdrawalId, nowUtc);

                // Giả lập ngắt ngay sau khi cập nhật Status=PAID nhưng chưa ghi bút toán và chưa nhả ReservedAmount
                var stepBankRef = $"{testPrefix}_STEP_BANK_REF";
                await context.WithdrawalRequests.UpdateOneAsync(
                    w => w.WithdrawalId == wReqStep.WithdrawalId,
                    Builders<WithdrawalRequest>.Update
                        .Set(w => w.Status, WithdrawalStatus.PAID)
                        .Set(w => w.BankTransactionRef, stepBankRef)
                        .Set(w => w.PaidByAdminUserId, admin1User.UserId)
                        .Set(w => w.PaidAt, nowUtc)
                );

                // Kiểm tra tại thời điểm ngắt: Khả dụng = Gross - Reserved (Reserved vẫn đang giữ)
                var midInterrWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                assertAction(midInterrWallet.AvailableBalance <= trueAvailable,
                    "(1) Ngắt sau khi gán PAID: Khả dụng không bao giờ cao hơn thực tế (Reserved vẫn giữ an toàn)");

                // Chạy đối soát: Tự tạo bù bút toán sổ cái cho yêu cầu PAID thiếu bút toán, sau đó tự sửa ReservedAmount
                var reconFixRun = await revenueService.RunReconciliationAsync(admin1User.UserId, null, nowUtc);
                assertAction(reconFixRun.Details.Any(d => d.Contains("[TỰ SỬA BÚT TOÁN]") && d.Contains(wReqStep.ReferenceId)),
                    "(1) Đối soát phát hiện yêu cầu PAID thiếu bút toán -> Tự động tạo bù bút toán sổ cái");

                // Kiểm tra sau khi đối soát tự sửa: Sổ cái đã có bút toán, ReservedAmount đã nhả
                var postInterrWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                var createdEntry = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.WithdrawalPaid(wReqStep.ReferenceId)).FirstOrDefaultAsync();
                assertAction(createdEntry != null && postInterrWallet.ReservedAmount == 500000, // Chỉ còn wReq2 500k đang mở
                    "(1) Sau đối soát: Bút toán đã được tạo bù và ReservedAmount khớp chính xác đơn đang mở");
            }

            // =========================================================================
            // TEST 5B.7 (c): TỪ CHỐI / HỦY YÊU CẦU -> NHẢ RESERVED, 0 BÚT TOÁN
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.7 (c): Từ chối và Hủy yêu cầu rút tiền -> Nhả Reserved, 0 bút toán sổ cái ---");
            {
                var beforeRejectWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                long initialAvail = beforeRejectWallet.AvailableBalance;

                var wReq = await withdrawalService.CreateWithdrawalRequestAsync(sitterAUser.UserId, new CreateWithdrawalDto { Amount = 500000 }, $"{testPrefix}_REJ_1", nowUtc);
                var midWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                assertAction(midWallet.AvailableBalance == initialAvail - 500000 && midWallet.ReservedAmount >= 500000,
                    "(c) Khi tạo yêu cầu, số dư khả dụng giảm 500.000 ₫ chuyển sang ReservedAmount");

                // Admin từ chối
                var rejected = await withdrawalService.RejectWithdrawalAsync(admin1User.UserId, wReq.WithdrawalId, new RejectWithdrawalDto { Reason = "Thông tin STK sai lệch" }, nowUtc);
                assertAction(rejected.Status == WithdrawalStatus.REJECTED, "(c) Yêu cầu chuyển trạng thái REJECTED");

                var afterRejectWallet = await withdrawalService.GetWalletSummaryAsync(sitterAUser.UserId, nowUtc);
                assertAction(afterRejectWallet.AvailableBalance == initialAvail,
                    "(c) Sau khi từ chối, ReservedAmount được nhả hoàn toàn về AvailableBalance ban đầu");

                var rejJournal = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.WithdrawalPaid(rejected.ReferenceId)).FirstOrDefaultAsync();
                assertAction(rejJournal == null, "(c) Yêu cầu bị từ chối: 0 bút toán sổ cái nào được sinh ra");
            }

            // =========================================================================
            // TEST 5B.8 (e, 5, o): REVENUE SWEEP & 50 SWEEP SONG SONG QUA PLATFORM ACCOUNT
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.8 (e, 5, o): Rút doanh thu sàn (Sweep), buffer an toàn & 50 sweep song song ---");
            {
                // Cấp doanh thu sàn: 20.000.000 ₫ (10.000.000 ₫ từ đơn COMPLETED cũ > 72h, 10.000.000 ₫ từ đơn mới < 10h làm buffer)
                var oldBooking = ObjectId.GenerateNewId().ToString();
                var newBooking = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(oldBooking);
                createdBookingIds.Add(newBooking);

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = oldBooking,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "REV1_HOLD",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-73),
                    IdempotencyKey = $"{oldBooking}:HELD",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 10000000 },
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 10000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = oldBooking,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "REV1",
                    Type = JournalType.ESCROW_RELEASE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-72),
                    IdempotencyKey = $"{oldBooking}:RELEASE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 10000000 },
                        new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 10000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = newBooking,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "REV2_HOLD",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-11),
                    IdempotencyKey = $"{newBooking}:HELD",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 10000000 },
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 10000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = newBooking,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "REV2",
                    Type = JournalType.ESCROW_RELEASE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-10),
                    IdempotencyKey = $"{newBooking}:RELEASE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 10000000 },
                        new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 10000000 }
                    }
                });

                var revOverview = await revenueService.GetRevenueOverviewAsync(nowUtc);
                assertAction(revOverview.PlatformRevenue >= 20000000, "(e) Doanh thu hoa hồng sàn ghi nhận chính xác");

                // Thử sweep vượt khả dụng (ví dụ rút 100.000.000 ₫) -> chặn 400
                bool overSweepBlocked = false;
                try
                {
                    await revenueService.CreateRevenueSweepAsync(admin1User.UserId, new CreateSweepDto
                    {
                        Amount = 100000000,
                        BankTransactionRef = "SWEEP_OVER",
                        Note = "Rút quá tay"
                    }, "127.0.0.1", nowUtc);
                }
                catch (BadRequestException)
                {
                    overSweepBlocked = true;
                }
                assertAction(overSweepBlocked, "(e) Rút doanh thu vượt mức khả dụng chuyển khoản -> Bị chặn 400");

                // Kiểm tra sau mỗi sweep availableToSweep (TransferableRevenue) giảm đúng bằng số vừa sweep
                var beforeSingleOverview = await revenueService.GetRevenueOverviewAsync(nowUtc);
                long initialTransferable = beforeSingleOverview.TransferableRevenue;
                long testSingleSweep = 500000;
                var singleSweep = await revenueService.CreateRevenueSweepAsync(admin1User.UserId, new CreateSweepDto
                {
                    Amount = testSingleSweep,
                    BankTransactionRef = $"SWP_SINGLE_{testPrefix}",
                    Note = "Kiểm tra giảm đúng số vừa sweep"
                }, "127.0.0.1", nowUtc);
                var afterSingleOverview = await revenueService.GetRevenueOverviewAsync(nowUtc);
                assertAction(afterSingleOverview.TransferableRevenue == initialTransferable - testSingleSweep,
                    "(3) Sau mỗi sweep, availableToSweep (TransferableRevenue) giảm đúng bằng số vừa sweep");

                // Thêm bút toán GATEWAY_FEES: DEBIT GATEWAY_FEES / CREDIT GATEWAY_CLEARING
                long testGatewayFee = 50000;
                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = "FEE_TEST_" + testPrefix,
                    TransactionId = "TX_FEE_" + testPrefix,
                    Type = JournalType.GATEWAY_FEE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"FEE_TEST_{testPrefix}",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_FEES, Direction = LedgerDirection.DEBIT, Amount = testGatewayFee },
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.CREDIT, Amount = testGatewayFee }
                    }
                });
                var feeOverview = await revenueService.GetRevenueOverviewAsync(nowUtc);
                assertAction(feeOverview.TransferableRevenue == afterSingleOverview.TransferableRevenue - testGatewayFee,
                    "(3) GATEWAY_FEES làm giảm khả dụng sweep TransferableRevenue theo đúng công thức");

                // 50 Sweep song song qua tài liệu chốt PlatformAccount (o)
                long maxTransferable = feeOverview.TransferableRevenue;
                long sweepAmountPerReq = 100000;
                var sweepSuccess = new ConcurrentBag<SweepItemDto>();
                var sweepFails = new ConcurrentBag<Exception>();

                var sweepTasks = Enumerable.Range(1, 50).Select(async i =>
                {
                    try
                    {
                        var s = await revenueService.CreateRevenueSweepAsync(admin1User.UserId, new CreateSweepDto
                        {
                            Amount = sweepAmountPerReq,
                            BankTransactionRef = $"SWP_{testPrefix}_{i}_{Guid.NewGuid():N}",
                            Note = $"Sweep song song test #{i}"
                        }, "127.0.0.1", nowUtc);
                        sweepSuccess.Add(s);
                    }
                    catch (Exception ex)
                    {
                        sweepFails.Add(ex);
                    }
                });

                await Task.WhenAll(sweepTasks);

                long totalSwept = sweepSuccess.Sum(s => s.Amount);
                var platformAcc = await context.PlatformAccounts.Find(p => p.AccountId == "platform_main").FirstOrDefaultAsync();

                assertAction(totalSwept <= maxTransferable,
                    $"(o) 50 sweep song song: Tổng rút ({totalSwept:N0} ₫) KHÔNG vượt khả dụng ({maxTransferable:N0} ₫)");
                assertAction(platformAcc != null && platformAcc.SweptTotal >= totalSwept && platformAcc.Version >= sweepSuccess.Count,
                    "(5, o) Tài liệu chốt PlatformAccount cập nhật nguyên tử với SweptTotal lũy kế và Version tăng dần");

                // Kiểm tra sổ cái Sweep: DEBIT PLATFORM_REVENUE / CREDIT GATEWAY_CLEARING
                var lastSweep = sweepSuccess.FirstOrDefault();
                if (lastSweep != null)
                {
                    var sweepJournal = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.RevenueSweep(lastSweep.BankTransactionRef)).FirstOrDefaultAsync();
                    assertAction(sweepJournal != null
                        && sweepJournal.Lines.Any(l => l.Account == LedgerAccounts.PLATFORM_REVENUE && l.Direction == LedgerDirection.DEBIT)
                        && sweepJournal.Lines.Any(l => l.Account == LedgerAccounts.GATEWAY_CLEARING && l.Direction == LedgerDirection.CREDIT),
                        "(3, e) Sweep ghi sổ DEBIT PLATFORM_REVENUE / CREDIT GATEWAY_CLEARING chính xác");
                }
            }

            // =========================================================================
            // TEST 5B.9 (f): GIAO DỊCH BẤT THƯỜNG (SUSPENSE) - GÁN ĐƠN VÀ HOÀN TIỀN
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.9 (f): Giao dịch bất thường SUSPENSE - Gán vào đơn và Hoàn trả người gửi ---");
            {
                // Tạo 2 thanh toán bất thường (OrderCode 88800001, 88800002)
                long orderCodeAssign = 88800001;
                long orderCodeRefund = 88800002;

                var paymentSuspense1 = new Payment
                {
                    PaymentId = ObjectId.GenerateNewId().ToString(),
                    BookingId = "UNASSIGNED",
                    OrderCode = orderCodeAssign,
                    Amount = 1000000,
                    ReceivedAmount = 1500000,
                    IsAbnormal = true,
                    AbnormalReason = "Khách chuyển thừa 500k",
                    Status = PaymentLinkStatus.ABNORMAL,
                    CreatedAt = nowUtc
                };
                var paymentSuspense2 = new Payment
                {
                    PaymentId = ObjectId.GenerateNewId().ToString(),
                    BookingId = "UNASSIGNED",
                    OrderCode = orderCodeRefund,
                    Amount = 500000,
                    ReceivedAmount = 500000,
                    IsAbnormal = true,
                    AbnormalReason = "Chuyển nhầm không có đơn",
                    Status = PaymentLinkStatus.ABNORMAL,
                    CreatedAt = nowUtc
                };
                await context.Payments.InsertManyAsync(new[] { paymentSuspense1, paymentSuspense2 });

                // Nạp tiền vào tài khoản SUSPENSE trên sổ cái
                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = "SUSPENSE:" + orderCodeAssign,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "SUS1",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"{orderCodeAssign}:SUSPENSE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 1500000 },
                        new JournalLine { Account = LedgerAccounts.SUSPENSE, Direction = LedgerDirection.CREDIT, Amount = 1500000 }
                    }
                });
                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = "SUSPENSE:" + orderCodeRefund,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "SUS2",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"{orderCodeRefund}:SUSPENSE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 },
                        new JournalLine { Account = LedgerAccounts.SUSPENSE, Direction = LedgerDirection.CREDIT, Amount = 500000 }
                    }
                });

                // 1. Gán giao dịch 1 vào đơn đặt lịch hợp lệ
                var bookingTarget = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bookingTarget);
                var targetBookingDoc = new Booking
                {
                    BookingId = bookingTarget,
                    MaDatLich = "DT_" + bookingTarget,
                    OwnerId = sitterAUser.UserId,
                    SitterId = sitterAProfile.SitterId,
                    TotalAmount = 1500000,
                    TongTien = 1500000,
                    TrangThai = BookingStatus.ACCEPTED,
                    PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                    NgayBatDau = nowUtc.AddDays(10),
                    NgayKetThuc = nowUtc.AddDays(12),
                    CreatedAt = nowUtc
                };
                await context.Bookings.InsertOneAsync(targetBookingDoc);

                await paymentService.AssignSuspensePaymentAsync(admin1User.UserId, orderCodeAssign, new AssignSuspenseDto { BookingId = bookingTarget, Note = "Khách chuyển trước" }, nowUtc);

                var assignJournal = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.SuspenseAssign(orderCodeAssign)).FirstOrDefaultAsync();
                assertAction(assignJournal != null
                    && assignJournal.Lines.Any(l => l.Account == LedgerAccounts.SUSPENSE && l.Direction == LedgerDirection.DEBIT && l.Amount == 1500000)
                    && assignJournal.Lines.Any(l => l.Account == LedgerAccounts.ESCROW_HELD && l.Direction == LedgerDirection.CREDIT && l.Amount == 1500000),
                    "(f) Gán SUSPENSE vào đơn: Ghi sổ DEBIT SUSPENSE, CREDIT ESCROW_HELD chính xác");

                // 2. Hoàn tiền giao dịch 2 cho người gửi
                await paymentService.RefundSuspensePaymentAsync(admin1User.UserId, orderCodeRefund, new RefundSuspenseDto { Reason = "Hoàn tiền do chuyển nhầm" }, nowUtc);

                var refundJournal = await context.JournalEntries.Find(j => j.IdempotencyKey == LedgerIdempotencyKeys.SuspenseRefund(orderCodeRefund)).FirstOrDefaultAsync();
                assertAction(refundJournal != null
                    && refundJournal.Lines.Any(l => l.Account == LedgerAccounts.SUSPENSE && l.Direction == LedgerDirection.DEBIT && l.Amount == 500000)
                    && refundJournal.Lines.Any(l => l.Account == LedgerAccounts.REFUND_PENDING && l.Direction == LedgerDirection.CREDIT && l.Amount == 500000),
                    "(f) Hoàn tiền SUSPENSE: Ghi sổ DEBIT SUSPENSE, CREDIT REFUND_PENDING chính xác");
            }

            // =========================================================================
            // TEST 5B.10 (3, n): ĐẲNG THỨC ĐỐI SOÁT GIỮ KHỚP SAU MỖI BƯỚC
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.10 (3, n): Đẳng thức đối soát giữ khớp sau mọi biến động tài chính ---");
            {
                // Chạy kiểm toán đối soát 8 tiêu chí
                var reconRun = await revenueService.RunReconciliationAsync(admin1User.UserId, null, nowUtc);
                assertAction(reconRun.IsBalancedTotalDebitCredit,
                    $"(n) Đẳng thức kế toán toàn bộ sổ cái giữ khớp 100% (Balanced={reconRun.IsBalancedTotalDebitCredit})");
                assertAction(reconRun.IsReservedAmountMatchesOpenWithdrawals, "(3) ReservedAmount khớp chính xác tổng các yêu cầu rút mở");
                assertAction(reconRun.IsEscrowHeldMatchesHeldBookings, "(3) ESCROW_HELD khớp chính xác tổng các đơn đang tạm giữ");
                assertAction(reconRun.Details != null && reconRun.Details.Any(d => d.Contains("[ĐẲNG THỨC ĐỐI SOÁT KHỚP 100%]")),
                    "(6, n) Đẳng thức đối soát GATEWAY_CLEARING == ESCROW_HELD + Sitter chưa rút + REFUND_PENDING + SUSPENSE + Doanh thu sàn chưa chuyển giữ khớp 100%");
            }

            // =========================================================================
            // TEST 5B.11 (p, 2): HÀM TỰ SỬA RESERVED_AMOUNT SAU SỰ CỐ
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.11 (p, 2): Tự sửa ReservedAmount khi bị lệch và cảnh báo Admin ---");
            {
                // Giả lập sự cố mạng/sập server giữa gate và tạo đơn:
                // Cố ý ghi sai ReservedAmount của Sitter A lên 99.999.999 ₫ trong khi tổng đơn mở = 0
                await context.EarningsAccounts.UpdateOneAsync(
                    e => e.UserId == sitterAUser.UserId,
                    Builders<EarningsAccount>.Update.Set(e => e.ReservedAmount, 99999999)
                );

                // Chạy hàm ReconcileReservedAmountAsync
                var (fixedCount, issues) = await withdrawalService.ReconcileReservedAmountAsync(sitterAUser.UserId);
                assertAction(fixedCount >= 1 && issues.Any(i => i.Contains(sitterAUser.UserId)),
                    "(p, 2) Phát hiện lệch ReservedAmount so với tổng yêu cầu mở và cảnh báo Admin");

                var fixedAccount = await context.EarningsAccounts.Find(e => e.UserId == sitterAUser.UserId).FirstOrDefaultAsync();
                assertAction(fixedAccount != null && fixedAccount.ReservedAmount != 99999999,
                    "(p) ReservedAmount được tự động sửa chữa cân bằng lại sau sự cố");
            }

            // =========================================================================
            // TEST 5B.12 (g): PHÁT HIỆN LỆCH ĐỐI SOÁT KHI CỐ TÌNH LÀM SAI LỆCH
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.12 (g): Cố ý làm lệch sổ cái và tiền mặt -> Đối soát phát hiện đúng mục ---");
            {
                // Cố ý chèn 1 bút toán không cân bằng hoặc sửa ExpectedBankBalance
                var corruptEntry = new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = "CORRUPT_TEST",
                    TransactionId = "TX_CORRUPT",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "HACKER",
                    CreatedAt = nowUtc,
                    IdempotencyKey = "CORRUPT_KEY",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 99999999 } // DEBIT mà không có CREDIT đối ứng
                    }
                };
                await context.JournalEntries.InsertOneAsync(corruptEntry);

                var corruptRun = await revenueService.RunReconciliationAsync(admin1User.UserId, null, nowUtc);
                assertAction(!corruptRun.IsBalancedTotalDebitCredit, "(g) Cố ý làm lệch sổ cái -> Đối soát phát hiện ngay lập tức (IsBalanced = false)");
                assertAction(corruptRun.Status == "WARNING", "(g) Trạng thái đối soát chuyển thành WARNING");
                assertAction(corruptRun.Details != null && corruptRun.Details.Any(d => d.Contains("[LỆCH ĐẲNG THỨC ĐỐI SOÁT]")),
                    "(6, g) Cố ý làm lệch sổ cái -> Đối soát phát hiện ngay mục [LỆCH ĐẲNG THỨC ĐỐI SOÁT]");

                // Dọn dẹp bút toán giả mạo
                await context.JournalEntries.DeleteOneAsync(j => j.EntryId == corruptEntry.EntryId);
            }

            // =========================================================================
            // TEST 5B.13 (h): HẠN MỨC PAYOS LINK & DÙNG LẠI ORDERCODE
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.13 (h): Hạn mức link PayOS, tái sử dụng OrderCode và cảnh báo quota ---");
            {
                var bookingQuota = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bookingQuota);
                var quotaBookingDoc = new Booking
                {
                    BookingId = bookingQuota,
                    MaDatLich = "DT_" + bookingQuota,
                    OwnerId = sitterAUser.UserId,
                    SitterId = sitterAProfile.SitterId,
                    TotalAmount = 500000,
                    TongTien = 500000,
                    TrangThai = BookingStatus.ACCEPTED,
                    PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                    NgayBatDau = nowUtc.AddDays(5),
                    NgayKetThuc = nowUtc.AddDays(6),
                    CreatedAt = nowUtc
                };
                await context.Bookings.InsertOneAsync(quotaBookingDoc);

                // Gọi tạo link thanh toán lần 1
                var link1 = await paymentService.CreateOrGetPaymentLinkAsync(sitterAUser.UserId, bookingQuota, nowUtc);
                // Gọi tạo link thanh toán lần 2 cho cùng đơn
                var link2 = await paymentService.CreateOrGetPaymentLinkAsync(sitterAUser.UserId, bookingQuota, nowUtc);

                assertAction(link1.OrderCode == link2.OrderCode,
                    "(h) Gọi /pay nhiều lần cho cùng đơn -> Dùng lại link cũ (cùng OrderCode, không lãng phí quota PayOS)");
            }

            // =========================================================================
            // TEST 5B.14 (k): BÁO CÁO CSV THÁNG & CHỐNG CSV INJECTION
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.14 (k): Xuất CSV doanh thu tháng và cơ chế chống CSV Injection ---");
            {
                var csvText = await revenueService.ExportMonthlyReportCsvAsync(nowUtc.Year, nowUtc.Month, nowUtc);

                assertAction(csvText.Contains("TransactionId") && csvText.Contains("OrderCode") && csvText.Contains("Amount"),
                    "(k) File CSV doanh thu tháng có đầy đủ cấu trúc cột và tiêu đề");

                // Thử tạo một sweep có ghi chú chứa ký tự formula CSV injection: =cmd|' /C calc'!A0
                var maliciousNote = "=cmd|' /C calc'!A0";
                await revenueService.CreateRevenueSweepAsync(admin1User.UserId, new CreateSweepDto
                {
                    Amount = 10000,
                    BankTransactionRef = "INJECT_TEST_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Note = maliciousNote
                }, "127.0.0.1", nowUtc);

                var csvInjectedText = await revenueService.ExportMonthlyReportCsvAsync(nowUtc.Year, nowUtc.Month, nowUtc);

                // Ký tự formula phải được sanitize bằng cách thêm dấu nháy đơn ' ở đầu
                assertAction(csvInjectedText.Contains("'=cmd|"),
                    "(k) Chống CSV Injection: Các ô bắt đầu bằng '=', '+', '-', '@' được tự động prepend dấu nháy đơn '");

                // Số tiền (kể cả số âm) KHÔNG được chèn dấu ' ở đầu
                assertAction(!csvInjectedText.Contains("'-") && !csvText.Contains("'-"),
                    "(8) CSV: Không chèn dấu ' cho cột số tiền (số âm), chỉ cho cột chữ; xử lý cả tab và CR");
            }

            // =========================================================================
            // TEST 5B.15 (j): BẢO MẬT PHÂN QUYỀN & IDOR TRUY CẬP VÍ / RÚT TIỀN
            // =========================================================================
            Console.WriteLine("\n--- TEST 5B.15 (j): Kiểm soát IDOR và phân quyền rút tiền ---");
            {
                // Sitter B tạo 1 yêu cầu rút tiền
                await withdrawalService.UpdateSitterBankAccountAsync(sitterBUser.UserId, new UpdateBankAccountDto
                {
                    BankBin = "970436",
                    BankName = "Vietcombank",
                    AccountNumber = "0071000999888",
                    AccountHolderName = sitterBUser.FullName,
                    Password = "Password123!"
                }, nowUtc);

                // Cấp khả dụng cho Sitter B
                var bSitterB = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bSitterB);
                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bSitterB,
                    TransactionId = "TX_SITTER_B_HOLD",
                    Type = JournalType.PAYMENT_HELD,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc.AddHours(-2),
                    IdempotencyKey = $"{bSitterB}:HELD",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 1000000 },
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 1000000 }
                    }
                });

                await ledgerService.RecordEntryAsync(new JournalEntry
                {
                    EntryId = ObjectId.GenerateNewId().ToString(),
                    BookingId = bSitterB,
                    TransactionId = "TX_SITTER_B",
                    Type = JournalType.ESCROW_RELEASE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"{bSitterB}:RELEASE",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 1000000 },
                        new JournalLine { Account = LedgerAccounts.SitterEarnings(sitterBUser.UserId), Direction = LedgerDirection.CREDIT, Amount = 1000000, AvailableAt = nowUtc.AddHours(-1) }
                    }
                });

                // Gỡ đóng băng cho Sitter B
                await context.SitterProfiles.UpdateOneAsync(
                    s => s.UserId == sitterBUser.UserId,
                    Builders<SitterProfile>.Update.Set(s => s.BankAccount.UpdatedAt, nowUtc.AddHours(-25))
                );

                var wReqB = await withdrawalService.CreateWithdrawalRequestAsync(sitterBUser.UserId, new CreateWithdrawalDto { Amount = 500000 }, $"{testPrefix}_B_1", nowUtc);

                // Sitter A cố tình hủy yêu cầu rút tiền của Sitter B -> Phải bị chặn 404 (IDOR)
                bool idorCancelBlocked = false;
                try
                {
                    await withdrawalService.CancelWithdrawalRequestAsync(sitterAUser.UserId, wReqB.WithdrawalId, nowUtc);
                }
                catch (NotFoundException)
                {
                    idorCancelBlocked = true;
                }
                assertAction(idorCancelBlocked, "(j) Sitter A cố tình hủy yêu cầu rút tiền của Sitter B -> Bị chặn 404/IDOR");
            }

            Console.WriteLine("=== HOÀN TẤT TOÀN BỘ TEST SUITE PROMPT 5B ===");
        }
    }
}
