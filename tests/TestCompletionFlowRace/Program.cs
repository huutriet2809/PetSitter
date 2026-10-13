using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Services;

namespace TestCompletionFlowRace
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("   BẮT ĐẦU KIỂM THỬ TỰ ĐỘNG CHU TRÌNH HOÀN TẤT & ĐUA LỆNH (PROMPT 3 TESTS)       ");
            Console.WriteLine("================================================================================");

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddUserSecrets<Program>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connStr = config["ConnectionStrings:MongoDb"] ?? config.GetConnectionString("MongoDb");
            var dbName = config["MongoDb:Database"] ?? config["ConnectionStrings:Database"] ?? "PetSitterDb";

            if (string.IsNullOrEmpty(connStr))
            {
                Console.WriteLine("LỖI: Không tìm thấy chuỗi kết nối MongoDB trong cấu hình!");
                return;
            }

            var mongoClient = new MongoClient(connStr);
            var db = mongoClient.GetDatabase(dbName);
            var context = new PetSitterContext(db);
            var bookingService = new BookingService(context, config);

            // ObjectIds cho các thực thể test
            var ownerUserId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterProfileId = ObjectId.GenerateNewId().ToString();
            var outsiderUserId = ObjectId.GenerateNewId().ToString();
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var petId = ObjectId.GenerateNewId().ToString();

            var ownerUser = new User
            {
                UserId = ownerUserId,
                FullName = "Owner Test Completion",
                Email = $"owner_{ownerUserId}@test.local",
                PasswordHash = "hash",
                Role = "USER",
                CreatedAt = DateTime.UtcNow
            };
            var sitterUser = new User
            {
                UserId = sitterUserId,
                FullName = "Sitter Test Completion",
                Email = $"sitter_{sitterUserId}@test.local",
                PasswordHash = "hash",
                Role = "SITTER",
                CreatedAt = DateTime.UtcNow
            };
            var outsiderUser = new User
            {
                UserId = outsiderUserId,
                FullName = "Outsider Test Completion",
                Email = $"outsider_{outsiderUserId}@test.local",
                PasswordHash = "hash",
                Role = "USER",
                CreatedAt = DateTime.UtcNow
            };
            var adminUser = new User
            {
                UserId = adminUserId,
                FullName = "Admin Test Completion",
                Email = $"admin_{adminUserId}@test.local",
                PasswordHash = "hash",
                Role = "ADMIN",
                CreatedAt = DateTime.UtcNow
            };

            var sitterProfile = new SitterProfile
            {
                SitterId = sitterProfileId,
                UserId = sitterUserId,
                Status = "Approved",
                IsVerified = true,
                CreatedAt = DateTime.UtcNow
            };

            var pet = new Pet
            {
                PetId = petId,
                OwnerId = ownerUserId,
                Name = "Cún Bông",
                Species = "Chó",
                CreatedAt = DateTime.UtcNow
            };

            var createdBookingIds = new List<string>();

            try
            {
                await context.Users.InsertManyAsync(new[] { ownerUser, sitterUser, outsiderUser, adminUser });
                await context.SitterProfiles.InsertOneAsync(sitterProfile);
                await context.Pets.InsertOneAsync(pet);

                var vnToday = DateTime.UtcNow.AddHours(7).Date;

                // -----------------------------------------------------------------------------------------
                // (m) TEST BIÊN NGÀY COMPLETE CỦA SITTER (NgayKetThuc == vnToday -> 400; vnToday - 1 -> 200)
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (m) Kiểm thử biên ngày Complete của Sitter:");
                var b1Id = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(b1Id);
                var b1 = new Booking
                {
                    BookingId = b1Id,
                    MaDatLich = "BK_M1",
                    OwnerId = ownerUserId,
                    SitterId = sitterProfileId,
                    PetId = petId,
                    NgayBatDau = vnToday.AddDays(-2),
                    NgayKetThuc = vnToday, // NgayKetThuc == vnToday -> chưa qua ngày kết thúc!
                    TrangThai = BookingStatus.ACCEPTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(b1);

                bool m1FailedAsExpected = false;
                try
                {
                    await bookingService.SitterCompleteBookingAsync(sitterUserId, b1Id);
                }
                catch (BadRequestException)
                {
                    m1FailedAsExpected = true;
                }
                Console.WriteLine($"   - NgayKetThuc == vnToday: Bị từ chối (400): {(m1FailedAsExpected ? "PASS" : "FAIL")}");

                // Đổi NgayKetThuc = vnToday - 1 ngày -> được!
                await context.Bookings.UpdateOneAsync(b => b.BookingId == b1Id, Builders<Booking>.Update.Set(b => b.NgayKetThuc, vnToday.AddDays(-1)));
                var resM2 = await bookingService.SitterCompleteBookingAsync(sitterUserId, b1Id);
                Console.WriteLine($"   - NgayKetThuc == vnToday - 1: Thành công (200), chuyển {resM2.TrangThai}: {(resM2.TrangThai == BookingStatus.COMPLETION_PENDING ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (a) Khi ở COMPLETION_PENDING, review chưa mở (400)
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (a) Review khi ở COMPLETION_PENDING:");
                bool aReviewBlocked = false;
                try
                {
                    await bookingService.AddReviewAsync(ownerUserId, b1Id, new CreateReviewDto { Rating = 5, Comment = "Rất tốt" });
                }
                catch (BadRequestException)
                {
                    aReviewBlocked = true;
                }
                Console.WriteLine($"   - Đơn COMPLETION_PENDING bị chặn review: {(aReviewBlocked ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (b) Owner confirm -> COMPLETED, review mở, review lần 2 chặn (400/409)
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (b) Owner confirm và Review:");
                var resB = await bookingService.OwnerConfirmCompletionAsync(ownerUserId, b1Id);
                Console.WriteLine($"   - Owner confirm chuyển {resB.TrangThai}: {(resB.TrangThai == BookingStatus.COMPLETED ? "PASS" : "FAIL")}");

                var rev1 = await bookingService.AddReviewAsync(ownerUserId, b1Id, new CreateReviewDto { Rating = 5, Comment = "Tuyệt vời" });
                Console.WriteLine($"   - Review lần 1 thành công: {(rev1 != null ? "PASS" : "FAIL")}");

                bool rev2Blocked = false;
                try
                {
                    await bookingService.AddReviewAsync(ownerUserId, b1Id, new CreateReviewDto { Rating = 4, Comment = "Đánh giá lại" });
                }
                catch (Exception)
                {
                    rev2Blocked = true;
                }
                Console.WriteLine($"   - Review lần 2 bị chặn: {(rev2Blocked ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (c) Owner dispute thiếu lý do -> 400; có lý do (>=10 ký tự) -> DISPUTED, review bị chặn
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (c) Owner dispute:");
                var b2Id = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(b2Id);
                var b2 = new Booking
                {
                    BookingId = b2Id,
                    MaDatLich = "BK_C1",
                    OwnerId = ownerUserId,
                    SitterId = sitterProfileId,
                    PetId = petId,
                    NgayBatDau = vnToday.AddDays(-3),
                    NgayKetThuc = vnToday.AddDays(-1),
                    TrangThai = BookingStatus.COMPLETION_PENDING,
                    CompletionDeadline = DateTime.UtcNow.AddDays(3),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(b2);

                bool disputeShortBlocked = false;
                try
                {
                    await bookingService.OwnerDisputeBookingAsync(ownerUserId, b2Id, "Ngắn");
                }
                catch (BadRequestException)
                {
                    disputeShortBlocked = true;
                }
                Console.WriteLine($"   - Dispute lý do ngắn < 10 ký tự bị chặn (400): {(disputeShortBlocked ? "PASS" : "FAIL")}");

                var resDispute = await bookingService.OwnerDisputeBookingAsync(ownerUserId, b2Id, "Thú cưng bị trầy xước không rõ nguyên nhân khi nhận lại.");
                Console.WriteLine($"   - Dispute hợp lệ chuyển {resDispute.TrangThai}: {(resDispute.TrangThai == BookingStatus.DISPUTED ? "PASS" : "FAIL")}");

                bool disputeReviewBlocked = false;
                try
                {
                    await bookingService.AddReviewAsync(ownerUserId, b2Id, new CreateReviewDto { Rating = 1, Comment = "Tệ" });
                }
                catch (BadRequestException)
                {
                    disputeReviewBlocked = true;
                }
                Console.WriteLine($"   - Đơn DISPUTED bị chặn review: {(disputeReviewBlocked ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (d) Quá deadline -> lần đọc đầu tiên lazy auto-complete sang COMPLETED, actor SYSTEM, AutoCompleted=true
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (d) Lazy auto-complete khi quá hạn CompletionDeadline:");
                var b3Id = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(b3Id);
                var b3 = new Booking
                {
                    BookingId = b3Id,
                    MaDatLich = "BK_D1",
                    OwnerId = ownerUserId,
                    SitterId = sitterProfileId,
                    PetId = petId,
                    NgayBatDau = vnToday.AddDays(-5),
                    NgayKetThuc = vnToday.AddDays(-4),
                    TrangThai = BookingStatus.COMPLETION_PENDING,
                    CompletionDeadline = DateTime.UtcNow.AddHours(-1), // Quá hạn 1 giờ
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(b3);

                // Khi Owner đọc chi tiết -> kích hoạt lazy update
                var dtoB3 = await bookingService.GetBookingByIdAsync(ownerUserId, b3Id);
                Console.WriteLine($"   - Sau khi đọc, trạng thái chuyển {dtoB3.TrangThai}: {(dtoB3.TrangThai == BookingStatus.COMPLETED ? "PASS" : "FAIL")}");

                var b3Db = await context.Bookings.Find(b => b.BookingId == b3Id).FirstOrDefaultAsync();
                var b3AutoCompleted = b3Db.AutoCompleted == true;
                var b3LastHistory = b3Db.History.LastOrDefault();
                var b3SystemRole = b3LastHistory?.NguoiThucHien == "SYSTEM" && b3LastHistory?.ActorUserId == null && b3LastHistory?.HanhDong == "AUTO_COMPLETED";
                Console.WriteLine($"   - AutoCompleted = true: {(b3AutoCompleted ? "PASS" : "FAIL")}");
                Console.WriteLine($"   - History: Role=SYSTEM, ActorUserId=null, Action=AUTO_COMPLETED: {(b3SystemRole ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (e) Dispute sau deadline -> 409 Conflict
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (e) Dispute sau deadline:");
                bool disputeAfterDeadlineConflict = false;
                try
                {
                    await bookingService.OwnerDisputeBookingAsync(ownerUserId, b3Id, "Khiếu nại sau khi đã auto completed");
                }
                catch (ConflictException)
                {
                    disputeAfterDeadlineConflict = true;
                }
                Console.WriteLine($"   - Dispute sau khi quá deadline bị 409 Conflict: {(disputeAfterDeadlineConflict ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (f) Admin resolve: thiếu note -> 400; Admin tự xử lý đơn mình -> 400; CANCELLED nhả IsActive
                // (n) Admin resolve COMPLETED -> phân biệt rõ trong History với Owner confirm và Auto-completed
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (f) & (n) Admin resolve tranh chấp:");
                var bAdminSelfId = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bAdminSelfId);
                var bAdminSelf = new Booking
                {
                    BookingId = bAdminSelfId,
                    MaDatLich = "BK_ADMIN_SELF",
                    OwnerId = adminUserId, // Admin là Owner
                    SitterId = sitterProfileId,
                    PetId = petId,
                    NgayBatDau = vnToday.AddDays(-3),
                    NgayKetThuc = vnToday.AddDays(-1),
                    TrangThai = BookingStatus.DISPUTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(bAdminSelf);

                bool adminSelfResolveBlocked = false;
                try
                {
                    await bookingService.AdminResolveDisputeAsync(adminUserId, bAdminSelfId, new ResolveDisputeDto { Resolution = "COMPLETED", Note = "Admin tự duyệt đơn của mình 12345" });
                }
                catch (BadRequestException)
                {
                    adminSelfResolveBlocked = true;
                }
                Console.WriteLine($"   - Admin tự xử lý đơn tranh chấp của chính mình bị chặn (400): {(adminSelfResolveBlocked ? "PASS" : "FAIL")}");

                // Admin resolve đơn b2 (Owner là ownerUser)
                var resAdminResolve = await bookingService.AdminResolveDisputeAsync(adminUserId, b2Id, new ResolveDisputeDto { Resolution = "COMPLETED", Note = "Hai bên đã thỏa thuận bồi thường thỏa đáng." });
                var b2Db = await context.Bookings.Find(b => b.BookingId == b2Id).FirstOrDefaultAsync();
                var b2History = b2Db.History.LastOrDefault();
                var b2ResolvedAdmin = b2Db.ResolvedByAdmin == true
                                      && b2History?.NguoiThucHien == "ADMIN"
                                      && b2History?.ActorUserId == adminUserId
                                      && b2History?.HanhDong == "ADMIN_RESOLVED_COMPLETED"
                                      && b2History?.LyDo == "Hai bên đã thỏa thuận bồi thường thỏa đáng.";
                Console.WriteLine($"   - Admin resolve COMPLETED thành công: {(resAdminResolve.TrangThai == BookingStatus.COMPLETED ? "PASS" : "FAIL")}");
                Console.WriteLine($"   - Phân biệt History (Role=ADMIN, ActorUserId=admin, Action=ADMIN_RESOLVED_COMPLETED): {(b2ResolvedAdmin ? "PASS" : "FAIL")}");

                // Test Admin resolve CANCELLED nhả IsActive
                var bCancelId = ObjectId.GenerateNewId().ToString();
                createdBookingIds.Add(bCancelId);
                var bCancelTest = new Booking
                {
                    BookingId = bCancelId,
                    MaDatLich = "BK_CANCEL_TEST",
                    OwnerId = ownerUserId,
                    SitterId = sitterProfileId,
                    PetId = petId,
                    NgayBatDau = vnToday.AddDays(-3),
                    NgayKetThuc = vnToday.AddDays(-1),
                    TrangThai = BookingStatus.DISPUTED,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Bookings.InsertOneAsync(bCancelTest);
                await bookingService.AdminResolveDisputeAsync(adminUserId, bCancelId, new ResolveDisputeDto { Resolution = "CANCELLED", Note = "Hủy bỏ dịch vụ do vi phạm nghiêm trọng quy chuẩn." });
                var bCancelDb = await context.Bookings.Find(b => b.BookingId == bCancelId).FirstOrDefaultAsync();
                Console.WriteLine($"   - Admin resolve CANCELLED nhả IsActive=false: {(bCancelDb.IsActive == false ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (g) Người ngoài gọi complete/confirm/dispute -> 404
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n--> (g) Người ngoài gọi endpoint:");
                bool outsiderComplete404 = false;
                bool outsiderConfirm404 = false;
                bool outsiderDispute404 = false;
                try { await bookingService.SitterCompleteBookingAsync(outsiderUserId, b1Id); } catch (NotFoundException) { outsiderComplete404 = true; }
                try { await bookingService.OwnerConfirmCompletionAsync(outsiderUserId, b1Id); } catch (NotFoundException) { outsiderConfirm404 = true; }
                try { await bookingService.OwnerDisputeBookingAsync(outsiderUserId, b1Id, "Người ngoài phá hoại 12345"); } catch (NotFoundException) { outsiderDispute404 = true; }
                Console.WriteLine($"   - Sitter complete bởi người ngoài -> 404: {(outsiderComplete404 ? "PASS" : "FAIL")}");
                Console.WriteLine($"   - Owner confirm bởi người ngoài -> 404: {(outsiderConfirm404 ? "PASS" : "FAIL")}");
                Console.WriteLine($"   - Owner dispute bởi người ngoài -> 404: {(outsiderDispute404 ? "PASS" : "FAIL")}");

                // -----------------------------------------------------------------------------------------
                // (h) TEST ĐUA LỆNH (CONCURRENCY) 50 CẶP
                // -----------------------------------------------------------------------------------------
                Console.WriteLine("\n================================================================================");
                Console.WriteLine("--> (h) KIỂM THỬ 50 CẶP ĐUA LỆNH ĐỒNG THỜI (CONCURRENCY RACE TESTS)");
                Console.WriteLine("================================================================================");

                int raceSuccessPairs = 0;
                int doubleHistoryCount = 0;

                for (int i = 1; i <= 50; i++)
                {
                    var raceBookingId = ObjectId.GenerateNewId().ToString();
                    createdBookingIds.Add(raceBookingId);
                    var raceBooking = new Booking
                    {
                        BookingId = raceBookingId,
                        MaDatLich = $"BK_RACE_{i}",
                        OwnerId = ownerUserId,
                        SitterId = sitterProfileId,
                        PetId = petId,
                        NgayBatDau = vnToday.AddDays(-5),
                        NgayKetThuc = vnToday.AddDays(-4),
                        TrangThai = BookingStatus.COMPLETION_PENDING,
                        CompletionDeadline = DateTime.UtcNow.AddMinutes(-5), // Đã quá hạn (cho phép cả Confirm lẫn Lazy)
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        History = new List<InquiryHistory>
                        {
                            new InquiryHistory { FromStatus = BookingStatus.PENDING, ThoiGian = DateTime.UtcNow.AddDays(-4), NguoiThucHien = "SITTER", HanhDong = "ACCEPTED" },
                            new InquiryHistory { FromStatus = BookingStatus.ACCEPTED, ThoiGian = DateTime.UtcNow.AddDays(-3), NguoiThucHien = "SITTER", HanhDong = "SITTER_COMPLETED" }
                        }
                    };
                    await context.Bookings.InsertOneAsync(raceBooking);

                    // Chạy đua đồng thời: 1 task Owner Confirm, 1 task Lazy Update
                    var taskOwnerConfirm = Task.Run(async () =>
                    {
                        try
                        {
                            return await bookingService.OwnerConfirmCompletionAsync(ownerUserId, raceBookingId);
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    });

                    var taskLazyUpdate = Task.Run(async () =>
                    {
                        try
                        {
                            return await bookingService.GetBookingByIdAsync(ownerUserId, raceBookingId);
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    });

                    await Task.WhenAll(taskOwnerConfirm, taskLazyUpdate);

                    // Kiểm tra kết quả trong DB
                    var finalDoc = await context.Bookings.Find(b => b.BookingId == raceBookingId).FirstOrDefaultAsync();
                    if (finalDoc.TrangThai == BookingStatus.COMPLETED && finalDoc.IsActive == false)
                    {
                        // Kiểm tra History có bị double không
                        var transitionEntries = finalDoc.History
                            .Where(h => h.HanhDong == "OWNER_CONFIRMED" || h.HanhDong == "AUTO_COMPLETED")
                            .ToList();

                        if (transitionEntries.Count == 1)
                        {
                            raceSuccessPairs++;
                        }
                        else
                        {
                            doubleHistoryCount++;
                            Console.WriteLine($"[RACE CẶP {i}] PHÁT HIỆN DOUBLE HISTORY! Số entry COMPLETED: {transitionEntries.Count}");
                        }
                    }
                }

                Console.WriteLine($"\nKẾT QUẢ ĐUA LỆNH 50 CẶP:");
                Console.WriteLine($"   - Tổng số cặp kiểm tra: 50");
                Console.WriteLine($"   - Số cặp thành công tuyệt đối (đúng 1 transition, không double): {raceSuccessPairs}/50");
                Console.WriteLine($"   - Số cặp bị double history: {doubleHistoryCount} (Mục tiêu: 0)");

                bool allPassed = m1FailedAsExpected
                                 && resM2.TrangThai == BookingStatus.COMPLETION_PENDING
                                 && aReviewBlocked
                                 && resB.TrangThai == BookingStatus.COMPLETED
                                 && rev1 != null
                                 && rev2Blocked
                                 && disputeShortBlocked
                                 && resDispute.TrangThai == BookingStatus.DISPUTED
                                 && disputeReviewBlocked
                                 && dtoB3.TrangThai == BookingStatus.COMPLETED
                                 && b3AutoCompleted
                                 && b3SystemRole
                                 && disputeAfterDeadlineConflict
                                 && adminSelfResolveBlocked
                                 && b2ResolvedAdmin
                                 && bCancelDb.IsActive == false
                                 && outsiderComplete404
                                 && outsiderConfirm404
                                 && outsiderDispute404
                                 && raceSuccessPairs == 50
                                 && doubleHistoryCount == 0;

                Console.WriteLine("\n================================================================================");
                if (allPassed)
                {
                    Console.WriteLine(">>> TẤT CẢ TEST NGHIỆM THU (a) -> (n) ĐÃ ĐẠT 100%! <<<");
                }
                else
                {
                    Console.WriteLine(">>> CÓ BƯỚC CHƯA ĐẠT! VUI LÒNG KIỂM TRA LẠI. <<<");
                }
                Console.WriteLine("================================================================================");
            }
            finally
            {
                // Dọn dẹp triệt để dữ liệu test
                Console.WriteLine("\n--> Đang dọn sạch toàn bộ dữ liệu test trong PetSitterDb...");
                await context.Users.DeleteManyAsync(u => u.UserId == ownerUserId || u.UserId == sitterUserId || u.UserId == outsiderUserId || u.UserId == adminUserId);
                await context.SitterProfiles.DeleteManyAsync(s => s.SitterId == sitterProfileId);
                await context.Pets.DeleteManyAsync(p => p.PetId == petId);
                if (createdBookingIds.Any())
                {
                    await context.Bookings.DeleteManyAsync(b => createdBookingIds.Contains(b.BookingId));
                    await context.Reviews.DeleteManyAsync(r => createdBookingIds.Contains(r.BookingId));
                }
                Console.WriteLine("--> Đã dọn sạch toàn bộ dữ liệu test!");
            }
        }
    }
}
