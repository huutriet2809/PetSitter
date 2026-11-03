using MongoDB.Driver;
using PetSitter.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetSitter.Data
{
    public static class MongoSeeder
    {
        public static async Task SeedDataAsync(PetSitterContext context, IConfiguration config)
        {
            // Read demo accounts config
            bool seedDemo = config.GetValue<bool>("Seed:DemoAccounts", true);

            var usersCollection = context.Users;

            // Create Unique Index for User.Email
            try
            {
                var indexKeysDefinition = Builders<User>.IndexKeys.Ascending(u => u.Email);
                var indexOptions = new CreateIndexOptions { Unique = true };
                var indexModel = new CreateIndexModel<User>(indexKeysDefinition, indexOptions);
                await usersCollection.Indexes.CreateOneAsync(indexModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create User.Email index: " + ex.Message);
            }

            // --- IDEMPOTENT MIGRATION CHO BOOKINGS (BƯỚC 1B) ---
            var bookingsCollection = context.Bookings;
            try
            {
                // 1. CONFIRMED -> ACCEPTED
                var confirmedFilter = Builders<Booking>.Filter.Eq("TrangThai", "CONFIRMED");
                var confirmedUpdate = Builders<Booking>.Update.Set(b => b.TrangThai, BookingStatus.ACCEPTED);
                var migratedConfirmed = await bookingsCollection.UpdateManyAsync(confirmedFilter, confirmedUpdate);
                if (migratedConfirmed.ModifiedCount > 0)
                {
                    Console.WriteLine($"[Seeder Migration] Đã chuyển {migratedConfirmed.ModifiedCount} đơn CONFIRMED sang ACCEPTED.");
                }

                // 2. Gán ExpiresAt cho PENDING cũ nếu chưa có
                var pendingWithoutExpires = Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.PENDING)
                                          & Builders<Booking>.Filter.Eq(b => b.ExpiresAt, null);
                var oldPendings = await bookingsCollection.Find(pendingWithoutExpires).ToListAsync();
                foreach (var old in oldPendings)
                {
                    var exp = (old.CreatedAt != default ? old.CreatedAt : DateTime.UtcNow).AddHours(48);
                    await bookingsCollection.UpdateOneAsync(
                        b => b.BookingId == old.BookingId,
                        Builders<Booking>.Update.Set(b => b.ExpiresAt, exp)
                    );
                }

                // 3. Gán History = [] nếu null
                var missingHistory = Builders<Booking>.Filter.Eq(b => b.History, null);
                await bookingsCollection.UpdateManyAsync(missingHistory, Builders<Booking>.Update.Set(b => b.History, new List<InquiryHistory>()));

                // 4. Gán IsActive cho đơn cũ: true nếu PENDING/ACCEPTED, false nếu khác
                var activeFilter = Builders<Booking>.Filter.In(b => b.TrangThai, new[] { BookingStatus.PENDING, BookingStatus.ACCEPTED });
                await bookingsCollection.UpdateManyAsync(activeFilter, Builders<Booking>.Update.Set(b => b.IsActive, true));

                var inactiveFilter = Builders<Booking>.Filter.Nin(b => b.TrangThai, new[] { BookingStatus.PENDING, BookingStatus.ACCEPTED });
                await bookingsCollection.UpdateManyAsync(inactiveFilter, Builders<Booking>.Update.Set(b => b.IsActive, false));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Migration Lỗi] Không thể hoàn thành migration Bookings: " + ex.Message);
            }

            // Create Unique Index for Bookings (MaDatLich)
            try
            {
                var bkIndexKeys = Builders<Booking>.IndexKeys.Ascending(b => b.MaDatLich);
                var bkIndexOptions = new CreateIndexOptions { Unique = true };
                var bkIndexModel = new CreateIndexModel<Booking>(bkIndexKeys, bkIndexOptions);
                await bookingsCollection.Indexes.CreateOneAsync(bkIndexModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create Bookings.MaDatLich index: " + ex.Message);
            }

            // Create Index (OwnerId, TrangThai)
            try
            {
                var ownerStatusKeys = Builders<Booking>.IndexKeys.Ascending(b => b.OwnerId).Ascending(b => b.TrangThai);
                await bookingsCollection.Indexes.CreateOneAsync(new CreateIndexModel<Booking>(ownerStatusKeys));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create Bookings (OwnerId, TrangThai) index: " + ex.Message);
            }

            // Create Index (SitterId, TrangThai)
            try
            {
                var sitterStatusKeys = Builders<Booking>.IndexKeys.Ascending(b => b.SitterId).Ascending(b => b.TrangThai);
                await bookingsCollection.Indexes.CreateOneAsync(new CreateIndexModel<Booking>(sitterStatusKeys));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create Bookings (SitterId, TrangThai) index: " + ex.Message);
            }

            // Create Partial Index for ExpiresAt where TrangThai == PENDING
            try
            {
                var expiresKeys = Builders<Booking>.IndexKeys.Ascending(b => b.ExpiresAt);
                var expiresOptions = new CreateIndexOptions<Booking>
                {
                    PartialFilterExpression = Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.PENDING)
                };
                await bookingsCollection.Indexes.CreateOneAsync(new CreateIndexModel<Booking>(expiresKeys, expiresOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create Bookings ExpiresAt partial index: " + ex.Message);
            }

            // Create Partial Index for CompletionDeadline where TrangThai == COMPLETION_PENDING
            try
            {
                var completionDeadlineKeys = Builders<Booking>.IndexKeys.Ascending(b => b.CompletionDeadline);
                var completionDeadlineOptions = new CreateIndexOptions<Booking>
                {
                    PartialFilterExpression = Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.COMPLETION_PENDING)
                };
                await bookingsCollection.Indexes.CreateOneAsync(new CreateIndexModel<Booking>(completionDeadlineKeys, completionDeadlineOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create Bookings CompletionDeadline partial index: " + ex.Message);
            }

            // Create Unique Partial Index for (OwnerId, SitterId, NgayBatDau, NgayKetThuc) where IsActive == true
            try
            {
                // Kiểm tra trùng lặp trước khi tạo index
                var activeBookings = await bookingsCollection.Find(b => b.IsActive == true).ToListAsync();
                var duplicateGroups = activeBookings.GroupBy(b => new { b.OwnerId, b.SitterId, b.NgayBatDau, b.NgayKetThuc })
                                                   .Where(g => g.Count() > 1)
                                                   .ToList();
                if (duplicateGroups.Any())
                {
                    Console.WriteLine($"[CRITICAL] Phát hiện {duplicateGroups.Count} nhóm booking trùng lặp đang Active. Dừng tạo unique partial index, không tự xóa dữ liệu!");
                    throw new InvalidOperationException($"Phát hiện {duplicateGroups.Count} nhóm booking trùng lặp đang Active trong CSDL. Không thể tạo Unique Index.");
                }

                var uniqueActiveKeys = Builders<Booking>.IndexKeys
                    .Ascending(b => b.OwnerId)
                    .Ascending(b => b.SitterId)
                    .Ascending(b => b.NgayBatDau)
                    .Ascending(b => b.NgayKetThuc);

                var uniqueActiveOptions = new CreateIndexOptions<Booking>
                {
                    Unique = true,
                    PartialFilterExpression = Builders<Booking>.Filter.Eq(b => b.IsActive, true)
                };
                await bookingsCollection.Indexes.CreateOneAsync(new CreateIndexModel<Booking>(uniqueActiveKeys, uniqueActiveOptions));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo Bookings unique partial active index: " + ex.Message);
            }

            // Create Unique Index for Review.BookingId
            var reviewsCol = context.Reviews;
            try
            {
                await reviewsCol.Indexes.DropOneAsync("BookingId_1");
                Console.WriteLine("[Seeder] Đã xóa index cũ BookingId_1 trên Reviews.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder] Không thể xóa index BookingId_1 trên Reviews (có thể chưa tồn tại): " + ex.Message);
            }

            try
            {
                var rvIndexKeys = Builders<Review>.IndexKeys.Ascending(r => r.BookingId);
                var rvIndexOptions = new CreateIndexOptions<Review> 
                { 
                    Unique = true, 
                    PartialFilterExpression = Builders<Review>.Filter.Exists(r => r.BookingId)
                };
                var rvIndexModel = new CreateIndexModel<Review>(rvIndexKeys, rvIndexOptions);
                await reviewsCol.Indexes.CreateOneAsync(rvIndexModel);
                Console.WriteLine("[Seeder] Tạo thành công unique index Review.BookingId.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo Review.BookingId index: " + ex.Message);
            }

            // Create Index for StayUpdates
            var updatesCol = context.StayUpdates;
            try
            {
                var updateIndexKeys = Builders<StayUpdate>.IndexKeys.Ascending(u => u.BookingId).Descending(u => u.CreatedAt);
                var updateIndexModel = new CreateIndexModel<StayUpdate>(updateIndexKeys);
                await updatesCol.Indexes.CreateOneAsync(updateIndexModel);
            }
            catch (Exception ex) { Console.WriteLine("Could not create StayUpdates index: " + ex.Message); }

            // Create Index for PasswordResets TTL
            try
            {
                var prIndexModel = new CreateIndexModel<PasswordReset>(
                    Builders<PasswordReset>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }
                );
                await context.PasswordResets.Indexes.CreateOneAsync(prIndexModel);
            } catch (Exception ex) { Console.WriteLine("Could not create PasswordResets index: " + ex.Message); }

            // Create Index for Newsletters
            try
            {
                var nlIndexModel = new CreateIndexModel<Newsletter>(
                    Builders<Newsletter>.IndexKeys.Ascending(x => x.Email),
                    new CreateIndexOptions { Unique = true, Collation = new Collation("en", strength: CollationStrength.Primary) }
                );
                await context.Newsletters.Indexes.CreateOneAsync(nlIndexModel);
            }
            catch (Exception ex) { Console.WriteLine("Could not create Newsletters index: " + ex.Message); }

            // Create Index for RefreshTokens
            // GHI CHÚ QUAN TRỌNG:
            // 1. TTL Index đặt trên ExpiresAt với ExpireAfter = TimeSpan.Zero.
            //    TUYỆT ĐỐI KHÔNG dùng RevokedAt làm TTL vì ta cần giữ bản ghi đã revoke để phát hiện Replay Attack (token reuse).
            // 2. Unique Index trên TokenHash.
            // 3. Index trên UserId để truy vấn / thu hồi theo user.
            try
            {
                var rfTtlModel = new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }
                );
                var rfHashModel = new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(x => x.TokenHash),
                    new CreateIndexOptions { Unique = true }
                );
                var rfUserIndex = new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(x => x.UserId)
                );
                await context.RefreshTokens.Indexes.CreateManyAsync(new[] { rfTtlModel, rfHashModel, rfUserIndex });
            }
            catch (Exception ex) { Console.WriteLine("Could not create RefreshTokens indexes: " + ex.Message); }

            // Create Indexes for Notifications:
            // 1. Unique index on DedupeKey
            // 2. Compound index on (UserId, CreatedAt desc)
            // 3. Compound index on (UserId, ReadAt) for unread count queries
            // 4. TTL Index on CreatedAt based on NotificationSettings:TtlDays (default 90 days)
            try
            {
                var notifCol = context.Notifications;
                var notifDedupeModel = new CreateIndexModel<Notification>(
                    Builders<Notification>.IndexKeys.Ascending(n => n.DedupeKey),
                    new CreateIndexOptions { Unique = true }
                );
                var notifUserCreatedModel = new CreateIndexModel<Notification>(
                    Builders<Notification>.IndexKeys.Ascending(n => n.UserId).Descending(n => n.CreatedAt)
                );
                var notifUserReadModel = new CreateIndexModel<Notification>(
                    Builders<Notification>.IndexKeys.Ascending(n => n.UserId).Ascending(n => n.ReadAt)
                );

                var ttlDays = config.GetValue<int>("NotificationSettings:TtlDays", 90);
                if (ttlDays <= 0) ttlDays = 90;
                var notifTtlModel = new CreateIndexModel<Notification>(
                    Builders<Notification>.IndexKeys.Ascending(n => n.CreatedAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(ttlDays) }
                );

                await notifCol.Indexes.CreateManyAsync(new[] { notifDedupeModel, notifUserCreatedModel, notifUserReadModel, notifTtlModel });
                Console.WriteLine("[Seeder] Tạo thành công các index cho Notifications.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo Notification indexes: " + ex.Message);
            }

            // Create Indexes for Payments:
            // 1. Unique index on OrderCode
            // 2. Compound index on (BookingId, Status)
            try
            {
                var payCol = context.Payments;
                var payOrderCodeModel = new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys.Ascending(p => p.OrderCode),
                    new CreateIndexOptions { Unique = true }
                );
                var payBookingStatusModel = new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys.Ascending(p => p.BookingId).Ascending(p => p.Status)
                );
                await payCol.Indexes.CreateManyAsync(new[] { payOrderCodeModel, payBookingStatusModel });
                Console.WriteLine("[Seeder] Tạo thành công các index cho Payments.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo Payments indexes: " + ex.Message);
            }

            // Create Indexes for JournalEntries (Sổ cái):
            // 1. Unique index on IdempotencyKey
            // 2. Multikey index on Lines.Account và CreatedAt cho truy vấn số dư
            // 3. Index on (BookingId, Type)
            try
            {
                var ledgerCol = context.JournalEntries;
                var ledgerIdempModel = new CreateIndexModel<JournalEntry>(
                    Builders<JournalEntry>.IndexKeys.Ascending(j => j.IdempotencyKey),
                    new CreateIndexOptions { Unique = true }
                );
                var ledgerAccountCreatedModel = new CreateIndexModel<JournalEntry>(
                    Builders<JournalEntry>.IndexKeys.Ascending("Lines.Account").Descending(j => j.CreatedAt)
                );
                var ledgerBookingTypeModel = new CreateIndexModel<JournalEntry>(
                    Builders<JournalEntry>.IndexKeys.Ascending(j => j.BookingId).Ascending(j => j.Type)
                );
                await ledgerCol.Indexes.CreateManyAsync(new[] { ledgerIdempModel, ledgerAccountCreatedModel, ledgerBookingTypeModel });
                Console.WriteLine("[Seeder] Tạo thành công các index cho JournalEntries.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo JournalEntries indexes: " + ex.Message);
            }

            // Create Indexes for RefundRequests:
            // 1. Compound index on (Status, CreatedAt)
            // 2. Index on OwnerUserId
            // 3. Index on BookingId
            try
            {
                var refCol = context.RefundRequests;
                try
                {
                    var existingIndexes = await refCol.Indexes.ListAsync();
                    var indexList = await existingIndexes.ToListAsync();
                    foreach (var idx in indexList)
                    {
                        var name = idx.GetValue("name", "").AsString;
                        if (name == "PaymentId_1")
                        {
                            await refCol.Indexes.DropOneAsync("PaymentId_1");
                            Console.WriteLine("[Seeder] Đã xóa index cũ PaymentId_1 trên RefundRequests.");
                            break;
                        }
                    }
                }
                catch { }

                var refStatusCreatedModel = new CreateIndexModel<RefundRequest>(
                    Builders<RefundRequest>.IndexKeys.Ascending(r => r.Status).Descending(r => r.CreatedAt)
                );
                var refOwnerModel = new CreateIndexModel<RefundRequest>(
                    Builders<RefundRequest>.IndexKeys.Ascending(r => r.OwnerUserId)
                );
                var refBookingModel = new CreateIndexModel<RefundRequest>(
                    Builders<RefundRequest>.IndexKeys.Ascending(r => r.BookingId)
                );
                var refPaymentIdModel = new CreateIndexModel<RefundRequest>(
                    Builders<RefundRequest>.IndexKeys.Ascending(r => r.PaymentId),
                    new CreateIndexOptions<RefundRequest>
                    {
                        Unique = true,
                        PartialFilterExpression = Builders<RefundRequest>.Filter.And(
                            Builders<RefundRequest>.Filter.Type(r => r.PaymentId, MongoDB.Bson.BsonType.String),
                            Builders<RefundRequest>.Filter.Gt(r => r.PaymentId, "")
                        )
                    }
                );
                var refSettlementKeyModel = new CreateIndexModel<RefundRequest>(
                    Builders<RefundRequest>.IndexKeys.Ascending(r => r.SettlementKey),
                    new CreateIndexOptions<RefundRequest>
                    {
                        Unique = true,
                        PartialFilterExpression = Builders<RefundRequest>.Filter.And(
                            Builders<RefundRequest>.Filter.Type(r => r.SettlementKey, MongoDB.Bson.BsonType.String),
                            Builders<RefundRequest>.Filter.Gt(r => r.SettlementKey, "")
                        )
                    }
                );
                await refCol.Indexes.CreateManyAsync(new[] { refStatusCreatedModel, refOwnerModel, refBookingModel, refPaymentIdModel, refSettlementKeyModel });
                Console.WriteLine("[Seeder] Tạo thành công các index cho RefundRequests (bao gồm unique PaymentId và SettlementKey).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo RefundRequests indexes: " + ex.Message);
            }

            // Create Indexes for WithdrawalRequests:
            // 1. Unique index on ReferenceId
            // 2. Unique index on IdempotencyKey
            // 3. Compound index on (SitterUserId, CreatedAt desc)
            // 4. Compound index on (Status, CreatedAt desc)
            // 5. Partial unique index on BankTransactionRef (khi BankTransactionRef != null và khác rỗng)
            try
            {
                var wthCol = context.WithdrawalRequests;
                var wthRefIdModel = new CreateIndexModel<WithdrawalRequest>(
                    Builders<WithdrawalRequest>.IndexKeys.Ascending(w => w.ReferenceId),
                    new CreateIndexOptions { Unique = true }
                );
                var wthIdempModel = new CreateIndexModel<WithdrawalRequest>(
                    Builders<WithdrawalRequest>.IndexKeys.Ascending(w => w.IdempotencyKey),
                    new CreateIndexOptions { Unique = true }
                );
                var wthSitterCreatedModel = new CreateIndexModel<WithdrawalRequest>(
                    Builders<WithdrawalRequest>.IndexKeys.Ascending(w => w.SitterUserId).Descending(w => w.CreatedAt)
                );
                var wthStatusCreatedModel = new CreateIndexModel<WithdrawalRequest>(
                    Builders<WithdrawalRequest>.IndexKeys.Ascending(w => w.Status).Descending(w => w.CreatedAt)
                );
                var wthBankTxRefModel = new CreateIndexModel<WithdrawalRequest>(
                    Builders<WithdrawalRequest>.IndexKeys.Ascending(w => w.BankTransactionRef),
                    new CreateIndexOptions<WithdrawalRequest>
                    {
                        Unique = true,
                        PartialFilterExpression = Builders<WithdrawalRequest>.Filter.And(
                            Builders<WithdrawalRequest>.Filter.Type(w => w.BankTransactionRef, MongoDB.Bson.BsonType.String),
                            Builders<WithdrawalRequest>.Filter.Gt(w => w.BankTransactionRef, "")
                        )
                    }
                );
                await wthCol.Indexes.CreateManyAsync(new[] { wthRefIdModel, wthIdempModel, wthSitterCreatedModel, wthStatusCreatedModel, wthBankTxRefModel });
                Console.WriteLine("[Seeder] Tạo thành công các index cho WithdrawalRequests.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo WithdrawalRequests indexes: " + ex.Message);
            }

            // Create Indexes for EarningsAccounts:
            // 1. Unique index on UserId
            try
            {
                var earnCol = context.EarningsAccounts;
                var earnUserModel = new CreateIndexModel<EarningsAccount>(
                    Builders<EarningsAccount>.IndexKeys.Ascending(e => e.UserId),
                    new CreateIndexOptions { Unique = true }
                );
                await earnCol.Indexes.CreateOneAsync(earnUserModel);
                Console.WriteLine("[Seeder] Tạo thành công các index cho EarningsAccounts.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo EarningsAccounts indexes: " + ex.Message);
            }

            // Create Indexes for RevenueSweeps:
            // 1. Unique index on BankTransactionRef
            // 2. Index on CreatedAt desc
            try
            {
                var swpCol = context.RevenueSweeps;
                var swpTxRefModel = new CreateIndexModel<RevenueSweep>(
                    Builders<RevenueSweep>.IndexKeys.Ascending(s => s.BankTransactionRef),
                    new CreateIndexOptions { Unique = true }
                );
                var swpCreatedModel = new CreateIndexModel<RevenueSweep>(
                    Builders<RevenueSweep>.IndexKeys.Descending(s => s.CreatedAt)
                );
                await swpCol.Indexes.CreateManyAsync(new[] { swpTxRefModel, swpCreatedModel });
                Console.WriteLine("[Seeder] Tạo thành công các index cho RevenueSweeps.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo RevenueSweeps indexes: " + ex.Message);
            }

            // Create Indexes for ReconciliationRuns:
            // 1. Index on RunAt desc
            try
            {
                var recCol = context.ReconciliationRuns;
                var recRunAtModel = new CreateIndexModel<ReconciliationRun>(
                    Builders<ReconciliationRun>.IndexKeys.Descending(r => r.RunAt)
                );
                await recCol.Indexes.CreateOneAsync(recRunAtModel);
                Console.WriteLine("[Seeder] Tạo thành công các index cho ReconciliationRuns.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể tạo ReconciliationRuns indexes: " + ex.Message);
            }

            if (seedDemo)
            {
                var admin = await usersCollection.Find(u => u.Role == "ADMIN").FirstOrDefaultAsync();
                var sitter = await usersCollection.Find(u => u.Role == "SITTER").FirstOrDefaultAsync();
                var user = await usersCollection.Find(u => u.Role == "USER").FirstOrDefaultAsync();

                if (await usersCollection.CountDocumentsAsync(_ => true) == 0)
                {
                    string? adminPass = config.GetValue<string>("Seed:Passwords:Admin");
                    string? sitterPass = config.GetValue<string>("Seed:Passwords:Sitter");
                    string? userPass = config.GetValue<string>("Seed:Passwords:User");

                    var usersToInsert = new List<User>();

                    if (string.IsNullOrWhiteSpace(adminPass))
                    {
                        Console.WriteLine("[Cảnh báo Seeder] Bỏ qua tạo tài khoản Admin demo vì thiếu cấu hình khóa 'Seed:Passwords:Admin'.");
                    }
                    else
                    {
                        admin = new User { Email = "admin@pawstay.vn", PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPass), FullName = "Admin", Role = "ADMIN" };
                        usersToInsert.Add(admin);
                    }

                    if (string.IsNullOrWhiteSpace(sitterPass))
                    {
                        Console.WriteLine("[Cảnh báo Seeder] Bỏ qua tạo tài khoản Sitter demo vì thiếu cấu hình khóa 'Seed:Passwords:Sitter'.");
                    }
                    else
                    {
                        sitter = new User { Email = "sitter@pawstay.vn", PasswordHash = BCrypt.Net.BCrypt.HashPassword(sitterPass), FullName = "Tran Van Sitter", Role = "SITTER" };
                        usersToInsert.Add(sitter);
                    }

                    if (string.IsNullOrWhiteSpace(userPass))
                    {
                        Console.WriteLine("[Cảnh báo Seeder] Bỏ qua tạo tài khoản User demo vì thiếu cấu hình khóa 'Seed:Passwords:User'.");
                    }
                    else
                    {
                        user = new User { Email = "user@pawstay.vn", PasswordHash = BCrypt.Net.BCrypt.HashPassword(userPass), FullName = "Nguyen Thi User", Role = "USER" };
                        usersToInsert.Add(user);
                    }

                    if (usersToInsert.Count > 0)
                    {
                        await usersCollection.InsertManyAsync(usersToInsert);
                    }
                }

                var servicesCollection = context.Services;
                var services = await servicesCollection.Find(_ => true).ToListAsync();
                if (services.Count == 0)
                {
                    services = new List<Service>
                    {
                        new Service { ServiceName = "Chăm sóc tại nhà Sitter (Boarding)", Description = "Thú cưng ở tại nhà Sitter qua đêm" },
                        new Service { ServiceName = "Chăm sóc tại nhà chủ (House Sitting)", Description = "Sitter đến ở tại nhà bạn để chăm sóc" },
                        new Service { ServiceName = "Gửi ban ngày (Day Care)", Description = "Chăm sóc thú cưng ban ngày" },
                        new Service { ServiceName = "Dắt đi dạo (Dog Walking)", Description = "Dắt chó đi dạo 30-60 phút" },
                        new Service { ServiceName = "Ghé thăm (Drop-in Visits)", Description = "Ghé qua nhà cho ăn, chơi đùa 30 phút" }
                    };
                    await servicesCollection.InsertManyAsync(services);
                }

                var profilesCollection = context.SitterProfiles;
                if (sitter != null)
                {
                    var profile = await profilesCollection.Find(p => p.UserId == sitter.UserId).FirstOrDefaultAsync();
                    if (profile == null)
                    {
                        profile = new SitterProfile
                        {
                            UserId = sitter.UserId,
                            Bio = "Tôi rất yêu động vật và có 5 năm kinh nghiệm.",
                            Address = "Quận 1, TP. Hồ Chí Minh",
                            ExperienceYears = 5,
                            Tags = new List<string> { "Sân vườn rộng", "Có kinh nghiệm y tế", "Nhận chó lớn" },
                            Status = "Approved",
                            IsVerified = true,
                            ReviewedAt = DateTime.UtcNow,
                            Rating = 4.8m,
                            TotalReviews = 12
                        };
                        await profilesCollection.InsertOneAsync(profile);
                    }
                    else if (profile.Status != "Approved" || !profile.IsVerified)
                    {
                        // Migration nhỏ cho demo sitter đã tồn tại với Status cũ
                        var updateDemoProfile = Builders<SitterProfile>.Update
                            .Set(p => p.Status, "Approved")
                            .Set(p => p.IsVerified, true)
                            .Set(p => p.ReviewedAt, DateTime.UtcNow);
                        await profilesCollection.UpdateOneAsync(p => p.SitterId == profile.SitterId, updateDemoProfile);
                        Console.WriteLine($"[Seeder Migration] Đã cập nhật demo sitter {profile.SitterId} thành Status=Approved, IsVerified=true");
                    }

                    var sitterServicesCollection = context.SitterServices;
                    if (await sitterServicesCollection.CountDocumentsAsync(_ => true) == 0 && profile != null && services.Count >= 4)
                    {
                        var sitterServices = new List<SitterService>
                        {
                            new SitterService { SitterId = profile.SitterId, ServiceId = services[0].ServiceId, Price = 150000, IsPrimary = true },
                            new SitterService { SitterId = profile.SitterId, ServiceId = services[2].ServiceId, Price = 100000 },
                            new SitterService { SitterId = profile.SitterId, ServiceId = services[3].ServiceId, Price = 50000 }
                        };
                        await sitterServicesCollection.InsertManyAsync(sitterServices);
                    }

                    var availabilitiesCollection = context.SitterAvailabilities;
                    if (await availabilitiesCollection.CountDocumentsAsync(_ => true) == 0 && profile != null)
                    {
                        var availabilities = new List<SitterAvailability>();
                        DateTime today = DateTime.UtcNow.Date;
                        for (int i = 0; i < 14; i++)
                        {
                            DateTime d = today.AddDays(i);
                            if (d.DayOfWeek != DayOfWeek.Sunday)
                            {
                                availabilities.Add(new SitterAvailability { SitterId = profile.SitterId, AvailableDate = d, IsAvailable = true });
                            }
                        }
                        if (availabilities.Count > 0) {
                            await availabilitiesCollection.InsertManyAsync(availabilities);
                        }
                    }
                    
                    var reviewsColForSeed = context.Reviews;
                    if (await reviewsColForSeed.CountDocumentsAsync(_ => true) == 0 && profile != null && user != null)
                    {
                        var sampleReview = new Review
                        {
                            BookingId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                            OwnerId = user.UserId,
                            SitterId = profile.SitterId,
                            Rating = 5,
                            Comment = "Sitter rất nhiệt tình và yêu chó mèo.",
                            CreatedAt = DateTime.UtcNow
                        };
                        await reviewsColForSeed.InsertOneAsync(sampleReview);
                    }
                }
            }

            var petsCollection = context.Pets;
            if (seedDemo && await petsCollection.CountDocumentsAsync(_ => true) == 0 && await usersCollection.CountDocumentsAsync(_ => true) > 0)
            {
                var owner = await usersCollection.Find(u => u.Role == "USER").FirstOrDefaultAsync();
                if (owner != null)
                {
                    var pets = new List<Pet>
                    {
                        new Pet { OwnerId = owner.UserId, Name = "Milo", Species = "Chó", Breed = "Golden Retriever", Age = 3, Weight = 25.5m, HealthStatus = "Khỏe mạnh", DietaryNotes = "Ăn hạt Royal Canin" },
                        new Pet { OwnerId = owner.UserId, Name = "Luna", Species = "Mèo", Breed = "Anh lông ngắn", Age = 2, Weight = 4.2m, HealthStatus = "Tiêm phòng đầy đủ" }
                    };
                    await petsCollection.InsertManyAsync(pets);
                }
            }

            // --- SEED LỊCH SỬ GIAO DỊCH MẪU ĐỒNG BỘ 3 BÊN (USER - SITTER - ADMIN) ---
            if (seedDemo)
            {
                var adminUser = await usersCollection.Find(u => u.Role == "ADMIN").FirstOrDefaultAsync();
                var sitterUser = await usersCollection.Find(u => u.Role == "SITTER").FirstOrDefaultAsync();
                var ownerUser = await usersCollection.Find(u => u.Role == "USER").FirstOrDefaultAsync();

                if (adminUser != null && sitterUser != null && ownerUser != null)
                {
                    var existingDemo = await bookingsCollection.Find(b => b.MaDatLich == "BK-DEMO-014").FirstOrDefaultAsync();
                    if (existingDemo == null)
                    {
                        var sProfile = await context.SitterProfiles.Find(p => p.UserId == sitterUser.UserId).FirstOrDefaultAsync();
                        var ownerPets = await context.Pets.Find(p => p.OwnerId == ownerUser.UserId).ToListAsync();
                        if (sProfile != null)
                        {
                            await SeedDemoTransactionsAsync(context, adminUser, sitterUser, sProfile, ownerUser, ownerPets);
                            await SeedAdditionalSittersAndPetsAsync(context, config, ownerUser);
                        }
                    }
                }
            }

            // --- IDEMPOTENT MIGRATION CHO SITTER SERVICE ISPRIMARY (Yêu cầu 2) ---
            try
            {
                var sitters = await context.SitterProfiles.Find(_ => true).ToListAsync();
                var catalogServices = await context.Services.Find(_ => true).ToListAsync();
                int assignedCount = 0;
                var sittersWithoutServices = new List<string>();

                foreach (var sProfile in sitters)
                {
                    var sServices = await context.SitterServices.Find(s => s.SitterId == sProfile.SitterId).ToListAsync();
                    var validServices = sServices.Where(s => s.Price > 0).ToList();

                    if (validServices.Count == 0)
                    {
                        sittersWithoutServices.Add(sProfile.SitterId);
                        continue;
                    }

                    // Nếu đã có dịch vụ IsPrimary == true thì bỏ qua
                    if (validServices.Any(s => s.IsPrimary))
                    {
                        continue;
                    }

                    // Ưu tiên chọn dịch vụ trông giữ (Boarding / Trông giữ / tại nhà)
                    var primaryCandidate = validServices.FirstOrDefault(s => {
                        var cat = catalogServices.FirstOrDefault(c => c.ServiceId == s.ServiceId);
                        var name = cat?.ServiceName ?? "";
                        return name.Contains("Boarding", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Trông giữ", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("tại nhà", StringComparison.OrdinalIgnoreCase);
                    }) ?? validServices.First();

                    await context.SitterServices.UpdateOneAsync(
                        s => s.SitterServiceId == primaryCandidate.SitterServiceId,
                        Builders<SitterService>.Update.Set(s => s.IsPrimary, true)
                    );
                    assignedCount++;
                }

                if (assignedCount > 0)
                {
                    Console.WriteLine($"[Seeder Migration] Đã gán dịch vụ trông giữ chính (IsPrimary) cho {assignedCount} hồ sơ Sitter.");
                }

                if (sittersWithoutServices.Count > 0)
                {
                    Console.WriteLine($"[Seeder Migration Cảnh báo] Danh sách {sittersWithoutServices.Count} Sitter chưa có dịch vụ nào với giá hợp lệ (Price > 0): {string.Join(", ", sittersWithoutServices)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Migration Lỗi] Không thể hoàn thành migration SitterService IsPrimary: " + ex.Message);
            }
        }

        public static async Task SeedDemoTransactionsAsync(
            PetSitterContext context,
            User admin,
            User sitter,
            SitterProfile profile,
            User user,
            List<Pet> pets)
        {
            Console.WriteLine("[Seeder] Đang tạo bộ dữ liệu lịch sử giao dịch mẫu cho cả 3 bên (User, Sitter, Admin)...");

            try
            {
                // 1. Cập nhật thông tin ngân hàng cho SitterProfile nếu chưa có
                if (profile.BankAccount == null)
                {
                    var bankAccount = new SitterBankAccount
                    {
                        BankBin = "970422",
                        BankName = "MBBank (Ngân hàng Quân Đội)",
                        MaskedAccountNumber = "****6868",
                        EncryptedAccountNumber = "demo_enc_6868",
                        AccountHolderName = "TRAN VAN SITTER",
                        NameMatchesRegisteredName = true
                    };
                    await context.SitterProfiles.UpdateOneAsync(
                        p => p.SitterId == profile.SitterId,
                        Builders<SitterProfile>.Update.Set(p => p.BankAccount, bankAccount)
                    );
                }

                // 1.1 Xóa sạch dữ liệu demo cũ để tránh trùng lặp
                await context.Bookings.DeleteManyAsync(b => b.MaDatLich.StartsWith("BK-DEMO-"));
                await context.Payments.DeleteManyAsync(p => (p.OrderCode >= 100001 && p.OrderCode <= 100014) || p.OrderCode == 99887711 || p.OrderCode == 99887722 || p.OrderCode == 99887733);
                await context.JournalEntries.DeleteManyAsync(j => j.IdempotencyKey.StartsWith("PAYOS-100") || j.IdempotencyKey.StartsWith("ESCROW-RELEASE-") || j.IdempotencyKey.StartsWith("WDR-PAID-DEMO-") || j.IdempotencyKey.StartsWith("SWEEP-DEMO-"));
                await context.WithdrawalRequests.DeleteManyAsync(w => w.ReferenceId.StartsWith("WDR-DEMO-"));
                await context.RefundRequests.DeleteManyAsync(r => r.BookingId.StartsWith("BK-DEMO-") || r.BookingId == "BK-CANCEL-001" || r.BookingId == "BK-OLD-REFUND-009");
                await context.RevenueSweeps.DeleteManyAsync(s => s.BankTransactionRef.StartsWith("SWEEP-DEMO-"));

                var petMilo = pets.FirstOrDefault(p => p.Name == "Milo") ?? pets.FirstOrDefault();
                var petLuna = pets.FirstOrDefault(p => p.Name == "Luna") ?? pets.LastOrDefault();

                var now = DateTime.UtcNow;

                // 2. Tạo 14 Đơn Booking mẫu đa dạng trạng thái
                var b1Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b2Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b3Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b4Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b5Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b6Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b7Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b8Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b9Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b10Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b11Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b12Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b13Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                var b14Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

                var booking1 = new Booking
                {
                    BookingId = b1Id,
                    MaDatLich = "BK-DEMO-001",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.AddDays(-5),
                    NgayKetThuc = now.AddDays(-2),
                    SoNgay = 3,
                    GiaSitter = 450000,
                    TongAddons = 50000,
                    TongTien = 500000,
                    TotalAmount = 500000,
                    CommissionBps = 1000,
                    CommissionAmount = 50000,
                    SitterAmount = 450000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-2).AddHours(-4),
                    OwnerConfirmedAt = now.AddDays(-2).AddHours(-1),
                    EscrowReleasedAt = now.AddDays(-2).AddHours(-1),
                    GhiChu = "Bé Milo ăn hạt 2 lần/ngày, rất ngoan và thích đi dạo.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà Sitter (3 ngày)", UnitPrice = 150000, Quantity = 3, TotalAmount = 450000 },
                        new PricingItemSnapshot { ItemType = "ADDON", Name = "Dắt đi dạo công viên", UnitPrice = 50000, Quantity = 1, TotalAmount = 50000 }
                    },
                    Settlement = new SettlementPlan
                    {
                        SettlementKey = $"SETTLE-{b1Id}",
                        SettlementType = "ESCROW_RELEASE",
                        TotalAmount = 500000,
                        RefundAmount = 0,
                        CommissionAmount = 50000,
                        SitterAmount = 450000,
                        SitterUserId = sitter.UserId,
                        ClaimedAt = now.AddDays(-2),
                        AvailableAt = now.AddDays(-2)
                    },
                    History = new List<InquiryHistory>
                    {
                        new InquiryHistory { HanhDong = "Tạo yêu cầu đặt lịch", NguoiThucHien = "OWNER", ThoiGian = now.AddDays(-6) },
                        new InquiryHistory { HanhDong = "Sitter chấp nhận đặt lịch", NguoiThucHien = "SITTER", ThoiGian = now.AddDays(-5).AddHours(-6) },
                        new InquiryHistory { HanhDong = "Thanh toán thành công (Ký quỹ PayOS)", NguoiThucHien = "SYSTEM", ThoiGian = now.AddDays(-5).AddHours(-4) },
                        new InquiryHistory { HanhDong = "Sitter hoàn thành ca chăm sóc", NguoiThucHien = "SITTER", ThoiGian = now.AddDays(-2).AddHours(-4) },
                        new InquiryHistory { HanhDong = "Chủ nuôi xác nhận & tất toán", NguoiThucHien = "OWNER", ThoiGian = now.AddDays(-2).AddHours(-1) }
                    },
                    CreatedAt = now.AddDays(-6)
                };

                var booking2 = new Booking
                {
                    BookingId = b2Id,
                    MaDatLich = "BK-DEMO-002",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petLuna?.PetId,
                    Pet = new BookingPet { TenPet = petLuna?.Name ?? "Luna", LoaiPet = "Mèo", KichCo = "Nhỏ (<5kg)" },
                    NgayBatDau = now.Date,
                    NgayKetThuc = now.Date.AddDays(2),
                    SoNgay = 2,
                    GiaSitter = 300000,
                    TongAddons = 0,
                    TongTien = 300000,
                    TotalAmount = 300000,
                    CommissionBps = 1000,
                    CommissionAmount = 30000,
                    SitterAmount = 270000,
                    TrangThai = BookingStatus.ACCEPTED,
                    PaymentStatus = PaymentStatus.HELD,
                    IsActive = true,
                    GhiChu = "Luna ở tại nhà chủ, cho ăn pate buổi tối và dọn cát hàng ngày.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà chủ (2 ngày)", UnitPrice = 150000, Quantity = 2, TotalAmount = 300000 }
                    },
                    History = new List<InquiryHistory>
                    {
                        new InquiryHistory { HanhDong = "Tạo yêu cầu đặt lịch", NguoiThucHien = "OWNER", ThoiGian = now.AddDays(-1) },
                        new InquiryHistory { HanhDong = "Sitter chấp nhận đơn", NguoiThucHien = "SITTER", ThoiGian = now.AddDays(-1).AddHours(2) },
                        new InquiryHistory { HanhDong = "Thanh toán thành công (Ký quỹ an toàn)", NguoiThucHien = "SYSTEM", ThoiGian = now.AddDays(-1).AddHours(3) }
                    },
                    CreatedAt = now.AddDays(-1)
                };

                var booking3 = new Booking
                {
                    BookingId = b3Id,
                    MaDatLich = "BK-DEMO-003",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.Date.AddDays(3),
                    NgayKetThuc = now.Date.AddDays(4),
                    SoNgay = 1,
                    GiaSitter = 100000,
                    TongAddons = 0,
                    TongTien = 100000,
                    TotalAmount = 100000,
                    CommissionBps = 1000,
                    CommissionAmount = 10000,
                    SitterAmount = 90000,
                    TrangThai = BookingStatus.PENDING,
                    PaymentStatus = PaymentStatus.NONE,
                    IsActive = true,
                    ExpiresAt = now.AddHours(36),
                    GhiChu = "Gửi Milo ban ngày (Day Care) từ 8h sáng tới 18h tối.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Gửi ban ngày (Day Care)", UnitPrice = 100000, Quantity = 1, TotalAmount = 100000 }
                    },
                    History = new List<InquiryHistory>
                    {
                        new InquiryHistory { HanhDong = "Tạo yêu cầu đặt lịch", NguoiThucHien = "OWNER", ThoiGian = now.AddHours(-10) }
                    },
                    CreatedAt = now.AddHours(-10)
                };

                var booking4 = new Booking
                {
                    BookingId = b4Id,
                    MaDatLich = "BK-DEMO-004",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.Date.AddDays(-4),
                    NgayKetThuc = now.Date.AddDays(-2),
                    SoNgay = 2,
                    GiaSitter = 400000,
                    TongTien = 400000,
                    TotalAmount = 400000,
                    CommissionBps = 1000,
                    CommissionAmount = 40000,
                    SitterAmount = 360000,
                    TrangThai = BookingStatus.DISPUTED,
                    PaymentStatus = PaymentStatus.HELD,
                    IsActive = false,
                    DisputeReason = "Thú cưng bị trầy nhẹ ở móng và Sitter không cập nhật ảnh nhật ký đầy đủ.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà Sitter (2 ngày)", UnitPrice = 200000, Quantity = 2, TotalAmount = 400000 }
                    },
                    History = new List<InquiryHistory>
                    {
                        new InquiryHistory { HanhDong = "Tạo yêu cầu đặt lịch", NguoiThucHien = "OWNER", ThoiGian = now.AddDays(-5) },
                        new InquiryHistory { HanhDong = "Sitter chấp nhận", NguoiThucHien = "SITTER", ThoiGian = now.AddDays(-5).AddHours(2) },
                        new InquiryHistory { HanhDong = "Thanh toán ký quỹ thành công", NguoiThucHien = "SYSTEM", ThoiGian = now.AddDays(-5).AddHours(3) },
                        new InquiryHistory { HanhDong = "Chủ nuôi mở khiếu nại tranh chấp", NguoiThucHien = "OWNER", ThoiGian = now.AddDays(-2) }
                    },
                    CreatedAt = now.AddDays(-5)
                };

                var booking5 = new Booking
                {
                    BookingId = b5Id,
                    MaDatLich = "BK-DEMO-005",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.AddDays(-9),
                    NgayKetThuc = now.AddDays(-7),
                    SoNgay = 2,
                    GiaSitter = 300000,
                    TongTien = 300000,
                    TotalAmount = 300000,
                    CommissionBps = 1000,
                    CommissionAmount = 30000,
                    SitterAmount = 270000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-7).AddHours(-4),
                    OwnerConfirmedAt = now.AddDays(-7).AddHours(-2),
                    EscrowReleasedAt = now.AddDays(-7).AddHours(-2),
                    GhiChu = "Trông giữ Milo dịp cuối tuần.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà Sitter (2 ngày)", UnitPrice = 150000, Quantity = 2, TotalAmount = 300000 }
                    },
                    Settlement = new SettlementPlan { SettlementKey = $"SETTLE-{b5Id}", SettlementType = "ESCROW_RELEASE", TotalAmount = 300000, CommissionAmount = 30000, SitterAmount = 270000, SitterUserId = sitter.UserId, ClaimedAt = now.AddDays(-7), AvailableAt = now.AddDays(-7) },
                    CreatedAt = now.AddDays(-10)
                };

                var booking6 = new Booking
                {
                    BookingId = b6Id,
                    MaDatLich = "BK-DEMO-006",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.AddDays(-12),
                    NgayKetThuc = now.AddDays(-10),
                    SoNgay = 3,
                    GiaSitter = 150000,
                    TongTien = 150000,
                    TotalAmount = 150000,
                    CommissionBps = 1000,
                    CommissionAmount = 15000,
                    SitterAmount = 135000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-10).AddHours(-3),
                    OwnerConfirmedAt = now.AddDays(-10).AddHours(-1),
                    EscrowReleasedAt = now.AddDays(-10).AddHours(-1),
                    GhiChu = "Dắt Milo đi dạo 3 buổi chiều quanh bờ hồ.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Dắt đi dạo 45 phút (3 buổi)", UnitPrice = 50000, Quantity = 3, TotalAmount = 150000 }
                    },
                    Settlement = new SettlementPlan { SettlementKey = $"SETTLE-{b6Id}", SettlementType = "ESCROW_RELEASE", TotalAmount = 150000, CommissionAmount = 15000, SitterAmount = 135000, SitterUserId = sitter.UserId, ClaimedAt = now.AddDays(-10), AvailableAt = now.AddDays(-10) },
                    CreatedAt = now.AddDays(-13)
                };

                var booking7 = new Booking
                {
                    BookingId = b7Id,
                    MaDatLich = "BK-DEMO-007",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petLuna?.PetId,
                    Pet = new BookingPet { TenPet = petLuna?.Name ?? "Luna", LoaiPet = "Mèo", KichCo = "Nhỏ (<5kg)" },
                    NgayBatDau = now.AddDays(-14),
                    NgayKetThuc = now.AddDays(-12),
                    SoNgay = 2,
                    GiaSitter = 100000,
                    TongTien = 100000,
                    TotalAmount = 100000,
                    CommissionBps = 1000,
                    CommissionAmount = 10000,
                    SitterAmount = 90000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-12).AddHours(-4),
                    OwnerConfirmedAt = now.AddDays(-12).AddHours(-2),
                    EscrowReleasedAt = now.AddDays(-12).AddHours(-2),
                    GhiChu = "Ghé nhà cho Luna ăn và thay khay cát.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Ghé thăm cho ăn & chơi đùa (2 lần)", UnitPrice = 50000, Quantity = 2, TotalAmount = 100000 }
                    },
                    Settlement = new SettlementPlan { SettlementKey = $"SETTLE-{b7Id}", SettlementType = "ESCROW_RELEASE", TotalAmount = 100000, CommissionAmount = 10000, SitterAmount = 90000, SitterUserId = sitter.UserId, ClaimedAt = now.AddDays(-12), AvailableAt = now.AddDays(-12) },
                    CreatedAt = now.AddDays(-15)
                };

                var booking8 = new Booking
                {
                    BookingId = b8Id,
                    MaDatLich = "BK-DEMO-008",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.AddDays(-19),
                    NgayKetThuc = now.AddDays(-15),
                    SoNgay = 4,
                    GiaSitter = 600000,
                    TongTien = 600000,
                    TotalAmount = 600000,
                    CommissionBps = 1000,
                    CommissionAmount = 60000,
                    SitterAmount = 540000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-15).AddHours(-4),
                    OwnerConfirmedAt = now.AddDays(-15).AddHours(-2),
                    EscrowReleasedAt = now.AddDays(-15).AddHours(-2),
                    GhiChu = "Sitter ở lại nhà chủ 4 đêm chăm sóc chó và tưới cây.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà chủ (House Sitting - 4 ngày)", UnitPrice = 150000, Quantity = 4, TotalAmount = 600000 }
                    },
                    Settlement = new SettlementPlan { SettlementKey = $"SETTLE-{b8Id}", SettlementType = "ESCROW_RELEASE", TotalAmount = 600000, CommissionAmount = 60000, SitterAmount = 540000, SitterUserId = sitter.UserId, ClaimedAt = now.AddDays(-15), AvailableAt = now.AddDays(-15) },
                    CreatedAt = now.AddDays(-20)
                };

                var booking9 = new Booking
                {
                    BookingId = b9Id,
                    MaDatLich = "BK-DEMO-009",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petLuna?.PetId,
                    Pet = new BookingPet { TenPet = petLuna?.Name ?? "Luna", LoaiPet = "Mèo", KichCo = "Nhỏ (<5kg)" },
                    NgayBatDau = now.AddDays(-25),
                    NgayKetThuc = now.AddDays(-20),
                    SoNgay = 5,
                    GiaSitter = 750000,
                    TongTien = 750000,
                    TotalAmount = 750000,
                    CommissionBps = 1000,
                    CommissionAmount = 75000,
                    SitterAmount = 675000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-20).AddHours(-5),
                    OwnerConfirmedAt = now.AddDays(-20).AddHours(-1),
                    EscrowReleasedAt = now.AddDays(-20).AddHours(-1),
                    GhiChu = "Gửi mèo Luna 5 ngày nghỉ lễ.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà Sitter (Boarding - 5 ngày)", UnitPrice = 150000, Quantity = 5, TotalAmount = 750000 }
                    },
                    Settlement = new SettlementPlan { SettlementKey = $"SETTLE-{b9Id}", SettlementType = "ESCROW_RELEASE", TotalAmount = 750000, CommissionAmount = 75000, SitterAmount = 675000, SitterUserId = sitter.UserId, ClaimedAt = now.AddDays(-20), AvailableAt = now.AddDays(-20) },
                    CreatedAt = now.AddDays(-26)
                };

                var booking10 = new Booking
                {
                    BookingId = b10Id,
                    MaDatLich = "BK-DEMO-010",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.Date.AddDays(1),
                    NgayKetThuc = now.Date.AddDays(4),
                    SoNgay = 3,
                    GiaSitter = 300000,
                    TongTien = 300000,
                    TotalAmount = 300000,
                    CommissionBps = 1000,
                    CommissionAmount = 30000,
                    SitterAmount = 270000,
                    TrangThai = BookingStatus.ACCEPTED,
                    PaymentStatus = PaymentStatus.HELD,
                    IsActive = true,
                    GhiChu = "Gửi Milo ban ngày 3 ngày trong tuần.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Gửi ban ngày (Day Care - 3 ngày)", UnitPrice = 100000, Quantity = 3, TotalAmount = 300000 }
                    },
                    CreatedAt = now.AddDays(-2)
                };

                var booking11 = new Booking
                {
                    BookingId = b11Id,
                    MaDatLich = "BK-DEMO-011",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.Date.AddDays(-2),
                    NgayKetThuc = now.Date,
                    SoNgay = 5,
                    GiaSitter = 250000,
                    TongTien = 250000,
                    TotalAmount = 250000,
                    CommissionBps = 1000,
                    CommissionAmount = 25000,
                    SitterAmount = 225000,
                    TrangThai = BookingStatus.COMPLETION_PENDING,
                    PaymentStatus = PaymentStatus.HELD,
                    IsActive = true,
                    SitterCompletedAt = now.AddHours(-3),
                    CompletionDeadline = now.AddDays(2),
                    GhiChu = "Sitter đã dắt đủ 5 buổi, vừa ấn hoàn thành, đang chờ chủ xác nhận.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Dắt đi dạo công viên (5 buổi)", UnitPrice = 50000, Quantity = 5, TotalAmount = 250000 }
                    },
                    CreatedAt = now.AddDays(-3)
                };

                var booking12 = new Booking
                {
                    BookingId = b12Id,
                    MaDatLich = "BK-DEMO-012",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petLuna?.PetId,
                    Pet = new BookingPet { TenPet = petLuna?.Name ?? "Luna", LoaiPet = "Mèo", KichCo = "Nhỏ (<5kg)" },
                    NgayBatDau = now.Date.AddDays(-3),
                    NgayKetThuc = now.Date.AddDays(-2),
                    SoNgay = 1,
                    GiaSitter = 150000,
                    TongTien = 150000,
                    TotalAmount = 150000,
                    TrangThai = BookingStatus.CANCELLED,
                    PaymentStatus = PaymentStatus.REFUND_PENDING,
                    IsActive = false,
                    LyDoTuChoi = "Sitter bận việc gia đình đột xuất, hoàn tiền 100% cho chủ nuôi.",
                    CreatedAt = now.AddDays(-4)
                };

                var booking13 = new Booking
                {
                    BookingId = b13Id,
                    MaDatLich = "BK-DEMO-013",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.Date.AddDays(5),
                    NgayKetThuc = now.Date.AddDays(7),
                    SoNgay = 2,
                    GiaSitter = 200000,
                    TongTien = 200000,
                    TotalAmount = 200000,
                    TrangThai = BookingStatus.PENDING,
                    PaymentStatus = PaymentStatus.NONE,
                    IsActive = true,
                    ExpiresAt = now.AddHours(40),
                    GhiChu = "Gửi Milo 2 ngày cuối tuần tới.",
                    CreatedAt = now.AddHours(-8)
                };

                var booking14 = new Booking
                {
                    BookingId = b14Id,
                    MaDatLich = "BK-DEMO-014",
                    OwnerId = user.UserId,
                    SitterId = profile.SitterId,
                    SitterUserId = sitter.UserId,
                    PetId = petMilo?.PetId,
                    Pet = new BookingPet { TenPet = petMilo?.Name ?? "Milo", LoaiPet = "Chó", KichCo = "Lớn (15-25kg)" },
                    NgayBatDau = now.AddDays(-30),
                    NgayKetThuc = now.AddDays(-28),
                    SoNgay = 2,
                    GiaSitter = 300000,
                    TongAddons = 50000,
                    TongTien = 350000,
                    TotalAmount = 350000,
                    CommissionBps = 1000,
                    CommissionAmount = 35000,
                    SitterAmount = 315000,
                    TrangThai = BookingStatus.COMPLETED,
                    PaymentStatus = PaymentStatus.RELEASED,
                    IsActive = false,
                    SitterCompletedAt = now.AddDays(-28).AddHours(-4),
                    OwnerConfirmedAt = now.AddDays(-28).AddHours(-1),
                    EscrowReleasedAt = now.AddDays(-28).AddHours(-1),
                    GhiChu = "Trông giữ và tắm sấy thơm tho cho Milo.",
                    PriceBreakdown = new List<PricingItemSnapshot>
                    {
                        new PricingItemSnapshot { ItemType = "BASE", Name = "Chăm sóc tại nhà Sitter (2 ngày)", UnitPrice = 150000, Quantity = 2, TotalAmount = 300000 },
                        new PricingItemSnapshot { ItemType = "ADDON", Name = "Tắm sấy & vệ sinh tai móng", UnitPrice = 50000, Quantity = 1, TotalAmount = 50000 }
                    },
                    Settlement = new SettlementPlan { SettlementKey = $"SETTLE-{b14Id}", SettlementType = "ESCROW_RELEASE", TotalAmount = 350000, CommissionAmount = 35000, SitterAmount = 315000, SitterUserId = sitter.UserId, ClaimedAt = now.AddDays(-28), AvailableAt = now.AddDays(-28) },
                    CreatedAt = now.AddDays(-31)
                };

                await context.Bookings.InsertManyAsync(new[]
                {
                    booking1, booking2, booking3, booking4, booking5,
                    booking6, booking7, booking8, booking9, booking10,
                    booking11, booking12, booking13, booking14
                });

                // 3. Tạo các Payments (Giao dịch cổng PayOS)
                var payments = new List<Payment>
                {
                    new Payment { BookingId = b1Id, OrderCode = 100001, Amount = 500000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-5).AddHours(-4), CreatedAt = now.AddDays(-5).AddHours(-5) },
                    new Payment { BookingId = b2Id, OrderCode = 100002, Amount = 300000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-1).AddHours(3), CreatedAt = now.AddDays(-1).AddHours(2) },
                    new Payment { BookingId = b4Id, OrderCode = 100004, Amount = 400000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-5).AddHours(3), CreatedAt = now.AddDays(-5).AddHours(2) },
                    new Payment { BookingId = b5Id, OrderCode = 100005, Amount = 300000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-9).AddHours(2), CreatedAt = now.AddDays(-9).AddHours(1) },
                    new Payment { BookingId = b6Id, OrderCode = 100006, Amount = 150000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-12).AddHours(3), CreatedAt = now.AddDays(-12).AddHours(2) },
                    new Payment { BookingId = b7Id, OrderCode = 100007, Amount = 100000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-14).AddHours(2), CreatedAt = now.AddDays(-14).AddHours(1) },
                    new Payment { BookingId = b8Id, OrderCode = 100008, Amount = 600000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-19).AddHours(4), CreatedAt = now.AddDays(-19).AddHours(3) },
                    new Payment { BookingId = b9Id, OrderCode = 100009, Amount = 750000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-25).AddHours(2), CreatedAt = now.AddDays(-25).AddHours(1) },
                    new Payment { BookingId = b10Id, OrderCode = 100010, Amount = 300000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-2).AddHours(3), CreatedAt = now.AddDays(-2).AddHours(2) },
                    new Payment { BookingId = b11Id, OrderCode = 100011, Amount = 250000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-3).AddHours(2), CreatedAt = now.AddDays(-3).AddHours(1) },
                    new Payment { BookingId = b14Id, OrderCode = 100014, Amount = 350000, Status = PaymentLinkStatus.PAID, PaidAt = now.AddDays(-30).AddHours(3), CreatedAt = now.AddDays(-30).AddHours(2) },
                    // SUSPENSE Payments (Giao dịch bất thường để Admin xử lý)
                    new Payment { BookingId = "UNASSIGNED", OrderCode = 99887711, Amount = 200000, ReceivedAmount = 250000, Status = PaymentLinkStatus.ABNORMAL, IsAbnormal = true, AbnormalReason = "Khách chuyển thừa 50.000 đ so với giao dịch dự kiến", CreatedAt = now.AddDays(-1) },
                    new Payment { BookingId = "UNASSIGNED", OrderCode = 99887722, Amount = 300000, ReceivedAmount = 300000, Status = PaymentLinkStatus.ABNORMAL, IsAbnormal = true, AbnormalReason = "Nội dung chuyển khoản sai cú pháp mã đơn", CreatedAt = now.AddHours(-15) },
                    new Payment { BookingId = "ASSIGNED_DEMO", OrderCode = 99887733, Amount = 150000, ReceivedAmount = 150000, Status = "ASSIGNED", IsAbnormal = false, AbnormalReason = "Đã được Admin gán thành công vào đơn đặt lịch", CreatedAt = now.AddDays(-5) }
                };
                await context.Payments.InsertManyAsync(payments);

                // 4. Tạo Sổ Cái Kép (JournalEntries) chuẩn toán học kép
                var entries = new List<JournalEntry>
                {
                    // Đơn 1 (500k -> 450k sitter, 50k sàn)
                    new JournalEntry { BookingId = b1Id, TransactionId = "TX-PAY-001", Type = "PAYMENT", IdempotencyKey = "PAYOS-100001", CreatedAt = now.AddDays(-5).AddHours(-4), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 500000 } } },
                    new JournalEntry { BookingId = b1Id, TransactionId = "TX-ESC-001", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b1Id}", CreatedAt = now.AddDays(-2).AddHours(-1), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 500000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 50000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 450000, AvailableAt = now.AddDays(-2) } } },

                    // Đơn 2 (300k -> đang ký quỹ)
                    new JournalEntry { BookingId = b2Id, TransactionId = "TX-PAY-002", Type = "PAYMENT", IdempotencyKey = "PAYOS-100002", CreatedAt = now.AddDays(-1).AddHours(3), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 300000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 300000 } } },

                    // Đơn 4 (400k -> đang ký quỹ tranh chấp)
                    new JournalEntry { BookingId = b4Id, TransactionId = "TX-PAY-004", Type = "PAYMENT", IdempotencyKey = "PAYOS-100004", CreatedAt = now.AddDays(-5).AddHours(3), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 400000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 400000 } } },

                    // Đơn 5 (300k -> 270k sitter, 30k sàn)
                    new JournalEntry { BookingId = b5Id, TransactionId = "TX-PAY-005", Type = "PAYMENT", IdempotencyKey = "PAYOS-100005", CreatedAt = now.AddDays(-9).AddHours(2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 300000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 300000 } } },
                    new JournalEntry { BookingId = b5Id, TransactionId = "TX-ESC-005", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b5Id}", CreatedAt = now.AddDays(-7).AddHours(-2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 300000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 30000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 270000, AvailableAt = now.AddDays(-7) } } },

                    // Đơn 6 (150k -> 135k sitter, 15k sàn)
                    new JournalEntry { BookingId = b6Id, TransactionId = "TX-PAY-006", Type = "PAYMENT", IdempotencyKey = "PAYOS-100006", CreatedAt = now.AddDays(-12).AddHours(3), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 150000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 150000 } } },
                    new JournalEntry { BookingId = b6Id, TransactionId = "TX-ESC-006", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b6Id}", CreatedAt = now.AddDays(-10).AddHours(-1), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 150000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 15000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 135000, AvailableAt = now.AddDays(-10) } } },

                    // Đơn 7 (100k -> 90k sitter, 10k sàn)
                    new JournalEntry { BookingId = b7Id, TransactionId = "TX-PAY-007", Type = "PAYMENT", IdempotencyKey = "PAYOS-100007", CreatedAt = now.AddDays(-14).AddHours(2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 100000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 100000 } } },
                    new JournalEntry { BookingId = b7Id, TransactionId = "TX-ESC-007", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b7Id}", CreatedAt = now.AddDays(-12).AddHours(-2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 100000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 10000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 90000, AvailableAt = now.AddDays(-12) } } },

                    // Đơn 8 (600k -> 540k sitter, 60k sàn)
                    new JournalEntry { BookingId = b8Id, TransactionId = "TX-PAY-008", Type = "PAYMENT", IdempotencyKey = "PAYOS-100008", CreatedAt = now.AddDays(-19).AddHours(4), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 600000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 600000 } } },
                    new JournalEntry { BookingId = b8Id, TransactionId = "TX-ESC-008", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b8Id}", CreatedAt = now.AddDays(-15).AddHours(-2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 600000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 60000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 540000, AvailableAt = now.AddDays(-15) } } },

                    // Đơn 9 (750k -> 675k sitter, 75k sàn)
                    new JournalEntry { BookingId = b9Id, TransactionId = "TX-PAY-009", Type = "PAYMENT", IdempotencyKey = "PAYOS-100009", CreatedAt = now.AddDays(-25).AddHours(2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 750000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 750000 } } },
                    new JournalEntry { BookingId = b9Id, TransactionId = "TX-ESC-009", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b9Id}", CreatedAt = now.AddDays(-20).AddHours(-1), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 750000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 75000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 675000, AvailableAt = now.AddDays(-20) } } },

                    // Đơn 10 (300k -> đang ký quỹ)
                    new JournalEntry { BookingId = b10Id, TransactionId = "TX-PAY-010", Type = "PAYMENT", IdempotencyKey = "PAYOS-100010", CreatedAt = now.AddDays(-2).AddHours(3), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 300000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 300000 } } },

                    // Đơn 11 (250k -> đang ký quỹ chờ xác nhận)
                    new JournalEntry { BookingId = b11Id, TransactionId = "TX-PAY-011", Type = "PAYMENT", IdempotencyKey = "PAYOS-100011", CreatedAt = now.AddDays(-3).AddHours(2), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 250000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 250000 } } },

                    // Đơn 14 (350k -> 315k sitter, 35k sàn)
                    new JournalEntry { BookingId = b14Id, TransactionId = "TX-PAY-014", Type = "PAYMENT", IdempotencyKey = "PAYOS-100014", CreatedAt = now.AddDays(-30).AddHours(3), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 350000 }, new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 350000 } } },
                    new JournalEntry { BookingId = b14Id, TransactionId = "TX-ESC-014", Type = "ESCROW_RELEASE", IdempotencyKey = $"ESCROW-RELEASE-{b14Id}", CreatedAt = now.AddDays(-28).AddHours(-1), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.DEBIT, Amount = 350000 }, new JournalLine { Account = LedgerAccounts.PLATFORM_REVENUE, Direction = LedgerDirection.CREDIT, Amount = 35000 }, new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.CREDIT, Amount = 315000, AvailableAt = now.AddDays(-28) } } },

                    // Các bút toán RÚT TIỀN THÀNH CÔNG (Tổng rút: 1.300.000 đ)
                    new JournalEntry { BookingId = "WITHDRAWAL", TransactionId = "TX-WDR-001", Type = "WITHDRAWAL_PAID", IdempotencyKey = "WDR-PAID-DEMO-001", CreatedAt = now.AddDays(-1).AddHours(-5), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.DEBIT, Amount = 200000 }, new JournalLine { Account = LedgerAccounts.PAYOUT_PENDING, Direction = LedgerDirection.CREDIT, Amount = 200000 } } },
                    new JournalEntry { BookingId = "WITHDRAWAL", TransactionId = "TX-WDR-003", Type = "WITHDRAWAL_PAID", IdempotencyKey = "WDR-PAID-DEMO-003", CreatedAt = now.AddDays(-8).AddHours(-4), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.DEBIT, Amount = 500000 }, new JournalLine { Account = LedgerAccounts.PAYOUT_PENDING, Direction = LedgerDirection.CREDIT, Amount = 500000 } } },
                    new JournalEntry { BookingId = "WITHDRAWAL", TransactionId = "TX-WDR-004", Type = "WITHDRAWAL_PAID", IdempotencyKey = "WDR-PAID-DEMO-004", CreatedAt = now.AddDays(-16).AddHours(-3), Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.SitterEarnings(sitter.UserId), Direction = LedgerDirection.DEBIT, Amount = 600000 }, new JournalLine { Account = LedgerAccounts.PAYOUT_PENDING, Direction = LedgerDirection.CREDIT, Amount = 600000 } } }
                };
                await context.JournalEntries.InsertManyAsync(entries);

                // 5. Tạo 5 Lệnh rút tiền (WithdrawalRequests)
                var withdrawals = new List<WithdrawalRequest>
                {
                    // Lệnh 1: 200k PAID
                    new WithdrawalRequest
                    {
                        WithdrawalId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterUserId = sitter.UserId,
                        Amount = 200000,
                        Status = WithdrawalStatus.PAID,
                        BankName = "MBBank (Ngân hàng Quân Đội)",
                        BankBin = "970422",
                        MaskedAccountNumber = "****6868",
                        AccountHolderName = "TRAN VAN SITTER",
                        ReferenceId = "WDR-DEMO-001",
                        IdempotencyKey = "IDEMP-WDR-001",
                        EncryptedBankSnapshot = "demo_snapshot",
                        BankTransactionRef = "FT2611029988",
                        ApprovedByAdminUserId = admin.UserId,
                        ApprovedAt = now.AddDays(-1).AddHours(-6),
                        PaidByAdminUserId = admin.UserId,
                        PaidAt = now.AddDays(-1).AddHours(-5),
                        CreatedAt = now.AddDays(-1).AddHours(-8),
                        UpdatedAt = now.AddDays(-1).AddHours(-5)
                    },
                    // Lệnh 2: 100k REQUESTED (Chờ duyệt)
                    new WithdrawalRequest
                    {
                        WithdrawalId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterUserId = sitter.UserId,
                        Amount = 100000,
                        Status = WithdrawalStatus.REQUESTED,
                        BankName = "MBBank (Ngân hàng Quân Đội)",
                        BankBin = "970422",
                        MaskedAccountNumber = "****6868",
                        AccountHolderName = "TRAN VAN SITTER",
                        ReferenceId = "WDR-DEMO-002",
                        IdempotencyKey = "IDEMP-WDR-002",
                        EncryptedBankSnapshot = "demo_snapshot",
                        CreatedAt = now.AddHours(-3),
                        UpdatedAt = now.AddHours(-3)
                    },
                    // Lệnh 3: 500k PAID (Tuần trước)
                    new WithdrawalRequest
                    {
                        WithdrawalId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterUserId = sitter.UserId,
                        Amount = 500000,
                        Status = WithdrawalStatus.PAID,
                        BankName = "MBBank (Ngân hàng Quân Đội)",
                        BankBin = "970422",
                        MaskedAccountNumber = "****6868",
                        AccountHolderName = "TRAN VAN SITTER",
                        ReferenceId = "WDR-DEMO-003",
                        IdempotencyKey = "IDEMP-WDR-003",
                        EncryptedBankSnapshot = "demo_snapshot",
                        BankTransactionRef = "FT2610261122",
                        ApprovedByAdminUserId = admin.UserId,
                        ApprovedAt = now.AddDays(-8).AddHours(-5),
                        PaidByAdminUserId = admin.UserId,
                        PaidAt = now.AddDays(-8).AddHours(-4),
                        CreatedAt = now.AddDays(-8).AddHours(-7),
                        UpdatedAt = now.AddDays(-8).AddHours(-4)
                    },
                    // Lệnh 4: 600k PAID (2 tuần trước)
                    new WithdrawalRequest
                    {
                        WithdrawalId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterUserId = sitter.UserId,
                        Amount = 600000,
                        Status = WithdrawalStatus.PAID,
                        BankName = "MBBank (Ngân hàng Quân Đội)",
                        BankBin = "970422",
                        MaskedAccountNumber = "****6868",
                        AccountHolderName = "TRAN VAN SITTER",
                        ReferenceId = "WDR-DEMO-004",
                        IdempotencyKey = "IDEMP-WDR-004",
                        EncryptedBankSnapshot = "demo_snapshot",
                        BankTransactionRef = "FT2610183344",
                        ApprovedByAdminUserId = admin.UserId,
                        ApprovedAt = now.AddDays(-16).AddHours(-4),
                        PaidByAdminUserId = admin.UserId,
                        PaidAt = now.AddDays(-16).AddHours(-3),
                        CreatedAt = now.AddDays(-16).AddHours(-6),
                        UpdatedAt = now.AddDays(-16).AddHours(-3)
                    },
                    // Lệnh 5: 150k APPROVED (Admin đã duyệt, đang xử lý chuyển tiền)
                    new WithdrawalRequest
                    {
                        WithdrawalId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterUserId = sitter.UserId,
                        Amount = 150000,
                        Status = WithdrawalStatus.APPROVED,
                        BankName = "MBBank (Ngân hàng Quân Đội)",
                        BankBin = "970422",
                        MaskedAccountNumber = "****6868",
                        AccountHolderName = "TRAN VAN SITTER",
                        ReferenceId = "WDR-DEMO-005",
                        IdempotencyKey = "IDEMP-WDR-005",
                        EncryptedBankSnapshot = "demo_snapshot",
                        ApprovedByAdminUserId = admin.UserId,
                        ApprovedAt = now.AddHours(-1),
                        CreatedAt = now.AddHours(-2),
                        UpdatedAt = now.AddHours(-1)
                    }
                };
                await context.WithdrawalRequests.InsertManyAsync(withdrawals);

                // 6. Cập nhật EarningsAccount cho Sitter (ReservedAmount = 100k + 150k = 250k)
                var existingAccount = await context.EarningsAccounts.Find(e => e.UserId == sitter.UserId).FirstOrDefaultAsync();
                if (existingAccount == null)
                {
                    await context.EarningsAccounts.InsertOneAsync(new EarningsAccount
                    {
                        UserId = sitter.UserId,
                        ReservedAmount = 250000, // Đang giữ 250k cho lệnh REQUESTED và APPROVED
                        DailyWithdrawn = 200000,
                        DailyWindowDate = now.AddHours(7).ToString("yyyy-MM-dd"),
                        Version = 1,
                        UpdatedAt = now
                    });
                }
                else
                {
                    await context.EarningsAccounts.UpdateOneAsync(
                        e => e.UserId == sitter.UserId,
                        Builders<EarningsAccount>.Update.Set(e => e.ReservedAmount, 250000)
                    );
                }

                // 7. Tạo các yêu cầu hoàn tiền (RefundRequests) cho hàng chờ của Admin
                var refundRequests = new List<RefundRequest>
                {
                    new RefundRequest
                    {
                        BookingId = "BK-CANCEL-001",
                        OwnerUserId = user.UserId,
                        Amount = 150000,
                        Reason = "Khách hủy đơn trước 48 giờ theo chính sách hoàn tiền 100%",
                        Status = RefundStatus.REFUND_PENDING,
                        CreatedAt = now.AddHours(-6)
                    },
                    new RefundRequest
                    {
                        BookingId = b12Id,
                        OwnerUserId = user.UserId,
                        Amount = 150000,
                        Reason = "Sitter bận việc đột xuất không nhận chăm sóc, hoàn tiền toàn bộ",
                        Status = RefundStatus.REFUND_PENDING,
                        CreatedAt = now.AddDays(-3)
                    },
                    new RefundRequest
                    {
                        BookingId = "BK-OLD-REFUND-009",
                        OwnerUserId = user.UserId,
                        Amount = 200000,
                        Reason = "Khách thay đổi lịch công tác hủy trước thời hạn",
                        Status = RefundStatus.COMPLETED,
                        BankTransactionRef = "RF2610205566",
                        AdminUserId = admin.UserId,
                        AdminNote = "Đã chuyển khoản hoàn tiền qua tài khoản Techcombank của khách",
                        CreatedAt = now.AddDays(-14),
                        CompletedAt = now.AddDays(-13)
                    }
                };
                await context.RefundRequests.InsertManyAsync(refundRequests);

                // 8. Tạo 2 lượt rút doanh thu nền tảng (RevenueSweeps) và cập nhật PlatformAccount
                var sweeps = new List<RevenueSweep>
                {
                    new RevenueSweep
                    {
                        SweepId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        Amount = 100000,
                        BankTransactionRef = "SWEEP-DEMO-001",
                        Note = "Rút hoa hồng nền tảng tuần 3 tháng 10 về Vietcombank sàn",
                        AdminUserId = admin.UserId,
                        IpAddress = "127.0.0.1",
                        CreatedAt = now.AddDays(-10)
                    },
                    new RevenueSweep
                    {
                        SweepId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        Amount = 100000,
                        BankTransactionRef = "SWEEP-DEMO-002",
                        Note = "Rút hoa hồng nền tảng tuần 4 tháng 10 về Vietcombank sàn",
                        AdminUserId = admin.UserId,
                        IpAddress = "127.0.0.1",
                        CreatedAt = now.AddDays(-3)
                    }
                };
                await context.RevenueSweeps.InsertManyAsync(sweeps);

                var pAcc = await context.PlatformAccounts.Find(p => p.AccountId == "platform_main").FirstOrDefaultAsync();
                if (pAcc == null)
                {
                    await context.PlatformAccounts.InsertOneAsync(new PlatformAccount
                    {
                        AccountId = "platform_main",
                        SweptTotal = 200000,
                        PayosLinksCreatedCount = 14,
                        Version = 1,
                        UpdatedAt = now
                    });
                }
                else
                {
                    await context.PlatformAccounts.UpdateOneAsync(
                        p => p.AccountId == "platform_main",
                        Builders<PlatformAccount>.Update.Set(p => p.SweptTotal, 200000).Set(p => p.PayosLinksCreatedCount, 14)
                    );
                }

                // 9. Tạo 7 bài Đánh giá (Reviews) cho các đơn COMPLETED
                await context.Reviews.DeleteManyAsync(r => r.BookingId == b1Id || r.BookingId == b5Id || r.BookingId == b6Id || r.BookingId == b7Id || r.BookingId == b8Id || r.BookingId == b9Id || r.BookingId == b14Id);
                var reviews = new List<Review>
                {
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b1Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "Sitter rất chu đáo và nhiệt tình, ngày nào cũng gửi ảnh và video cập nhật tình hình bé Milo cho mình yên tâm công tác. Milo về nhà vui vẻ, thơm tho.", CreatedAt = now.AddDays(-2).AddHours(-1) },
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b5Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "Nhà bạn sitter có sân chơi thoáng mát sạch sẽ, bé nhà mình gửi 2 ngày về không hề bị stress hay sụt cân. Sẽ tiếp tục gửi lại!", CreatedAt = now.AddDays(-7).AddHours(-2) },
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b6Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "Dịch vụ dắt chó đi dạo rất đúng giờ, sitter theo dõi năng lượng của bé cún rất cẩn thận, có mang theo túi dọn vệ sinh và bình nước riêng.", CreatedAt = now.AddDays(-10).AddHours(-1) },
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b7Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "Mình đi công tác đột xuất gửi khóa nhà cho bạn qua cho mèo ăn. Bạn rất trung thực, cẩn thận quay video khóa cửa và kiểm tra cửa sổ kỹ càng.", CreatedAt = now.AddDays(-12).AddHours(-2) },
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b8Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "House sitting tuyệt vời! Vừa chăm cún vừa hỗ trợ tưới cây giúp mình. Nhà cửa lúc mình về vẫn sạch sẽ ngăn nắp.", CreatedAt = now.AddDays(-15).AddHours(-2) },
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b9Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "Dịp lễ đông nhưng bạn chăm sóc từng bé rất kỹ, phòng điều hòa mát mẻ. Rất ưng ý dịch vụ của PawStay!", CreatedAt = now.AddDays(-20).AddHours(-1) },
                    new Review { ReviewId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), BookingId = b14Id, OwnerId = user.UserId, SitterId = profile.SitterId, Rating = 5, Comment = "Combo gửi cún kèm tắm sấy quá tiện lợi! Cún nhà mình được tắm thơm phức, cắt móng gọn gàng sạch sẽ.", CreatedAt = now.AddDays(-28).AddHours(-1) }
                };
                await context.Reviews.InsertManyAsync(reviews);

                // 10. Tạo các cập nhật nhật ký chăm sóc (Stay Updates) có ảnh thực tế
                await context.StayUpdates.DeleteManyAsync(s => s.BookingId == b1Id || s.BookingId == b2Id);
                var stayUpdates = new List<StayUpdate>
                {
                    new StayUpdate
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        BookingId = b1Id,
                        SitterUserId = sitter.UserId,
                        Note = "Bé Milo đã ăn hết khẩu phần hạt sáng và uống nhiều nước. Bé rất ngoan, quấn người và làm quen rất nhanh với phòng mới.",
                        PhotoPaths = new List<string> { "/uploads/stay/6ad72de86a1c12f72c7a0620/bb93d9fe-e25f-4856-9ec8-12f1df396658.png" },
                        CreatedAt = now.AddDays(-4).AddHours(8)
                    },
                    new StayUpdate
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        BookingId = b1Id,
                        SitterUserId = sitter.UserId,
                        Note = "Buổi chiều dắt Milo đi dạo công viên 45 phút. Bé chạy nhảy vui vẻ, đi vệ sinh đúng chỗ và rất hòa đồng với các bạn cún khác.",
                        PhotoPaths = new List<string> { "/assets/images/hero_pets.png" },
                        CreatedAt = now.AddDays(-4).AddHours(17)
                    },
                    new StayUpdate
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        BookingId = b1Id,
                        SitterUserId = sitter.UserId,
                        Note = "Milo được chải lông và vệ sinh tai móng sạch sẽ trước khi về với bố mẹ. Bé rất khỏe mạnh và hoạt bát!",
                        PhotoPaths = new List<string> { "/uploads/stay/6ad72de86a1c12f72c7a0620/bb93d9fe-e25f-4856-9ec8-12f1df396658.png" },
                        CreatedAt = now.AddDays(-2).AddHours(-6)
                    },
                    new StayUpdate
                    {
                        Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        BookingId = b2Id,
                        SitterUserId = sitter.UserId,
                        Note = "Ghé nhà cho Luna ăn pate cá hồi, dọn khay cát sạch sẽ và chơi đùa với cần câu lông vũ 20 phút. Bé đã bớt nhút nhát và chịu cho vuốt ve.",
                        PhotoPaths = new List<string> { "/uploads/stay/6ad72de86a1c12f72c7a0620/bb93d9fe-e25f-4856-9ec8-12f1df396658.png" },
                        CreatedAt = now.AddHours(-4)
                    }
                };
                await context.StayUpdates.InsertManyAsync(stayUpdates);

                // 11. Tạo cuộc trò chuyện & Tin nhắn trao đổi (Conversations) giữa Chủ nuôi và Sitter
                await context.Conversations.DeleteManyAsync(c => c.UserId == user.UserId && c.SitterId == profile.SitterId);
                var conversations = new List<Conversation>
                {
                    new Conversation { ConversationId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = user.UserId, SitterId = profile.SitterId, SenderType = "USER", Message = "Chào bạn Sitter! Bé Milo nhà mình là cún Golden 25kg, bạn có nhận trông giữ dịp cuối tuần này không ạ?", SentAt = now.AddDays(-6).AddHours(-2) },
                    new Conversation { ConversationId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = user.UserId, SitterId = profile.SitterId, SenderType = "SITTER", Message = "Chào bạn! Nhà mình có sân vườn rào kín rộng rãi, rất thích hợp cho các bé cún lớn vận động. Bạn muốn gửi từ ngày nào đến ngày nào ạ?", SentAt = now.AddDays(-6).AddHours(-1) },
                    new Conversation { ConversationId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = user.UserId, SitterId = profile.SitterId, SenderType = "USER", Message = "Mình gửi từ thứ 6 đến chủ nhật nhé. Bé rất lành tính nhưng thích được dắt đi dạo mỗi chiều tầm 30-45 phút.", SentAt = now.AddDays(-6).AddMinutes(-40) },
                    new Conversation { ConversationId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = user.UserId, SitterId = profile.SitterId, SenderType = "SITTER", Message = "Dạ hoàn toàn được ạ, bên mình có gói dắt đi dạo công viên mỗi ngày. Bạn cứ đặt lịch trên app, mình sẽ xác nhận ngay nhé!", SentAt = now.AddDays(-6).AddMinutes(-30) },
                    new Conversation { ConversationId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = user.UserId, SitterId = profile.SitterId, SenderType = "USER", Message = "Cảm ơn bạn nhiều, mình vừa đặt lịch đơn BK-DEMO-001 xong rồi đó ạ!", SentAt = now.AddDays(-6).AddMinutes(-10) },
                    new Conversation { ConversationId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), UserId = user.UserId, SitterId = profile.SitterId, SenderType = "SITTER", Message = "Dạ mình đã nhận và chấp nhận đơn rồi ạ. Hẹn gặp bạn và bé Milo nhé!", SentAt = now.AddDays(-5).AddHours(-6) }
                };
                await context.Conversations.InsertManyAsync(conversations);

                Console.WriteLine("[Seeder] Tạo 14 đơn giao dịch, 2 lượt rút Sweeps, Reviews, StayUpdates và Conversations cho cả 3 bên THÀNH CÔNG!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể seed dữ liệu giao dịch mẫu: " + ex.Message);
            }
        }

        public static async Task SeedAdditionalSittersAndPetsAsync(PetSitterContext context, IConfiguration config, User ownerUser)
        {
            Console.WriteLine("[Seeder] Đang kiểm tra và bổ sung thêm các Sitter và Thú cưng đa dạng khu vực...");

            try
            {
                // 1. Bổ sung thêm thú cưng (Bắp - Corgi, Bông - Poodle) cho Chủ nuôi nếu chưa có
                var existingBap = await context.Pets.Find(p => p.OwnerId == ownerUser.UserId && p.Name == "Bắp").FirstOrDefaultAsync();
                if (existingBap == null)
                {
                    var extraPets = new List<Pet>
                    {
                        new Pet { OwnerId = ownerUser.UserId, Name = "Bắp", Species = "Chó", Breed = "Corgi chân ngắn", Age = 1, Weight = 11.5m, HealthStatus = "Đã tiêm phòng dại & 7 bệnh đầy đủ, rất hiếu động", DietaryNotes = "Thích ăn ức gà xé và rau củ hấp" },
                        new Pet { OwnerId = ownerUser.UserId, Name = "Bông", Species = "Chó", Breed = "Poodle trắng", Age = 3, Weight = 4.8m, HealthStatus = "Khỏe mạnh, vừa tỉa lông gọn gàng", DietaryNotes = "Ăn hạt ngâm nước ấm" }
                    };
                    await context.Pets.InsertManyAsync(extraPets);
                }

                // 2. Bổ sung 5 Sitter mới (3 Approved đa dạng khu vực, 2 Pending chờ Admin duyệt)
                string defaultPassword = config.GetValue<string>("Seed:Passwords:Sitter") ?? "Sitter@123";
                string passHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword);

                var catalogServices = await context.Services.Find(_ => true).ToListAsync();
                var boardingSvc = catalogServices.FirstOrDefault(s => s.ServiceName.Contains("Boarding")) ?? catalogServices.FirstOrDefault();
                var dayCareSvc = catalogServices.FirstOrDefault(s => s.ServiceName.Contains("Day Care"));
                var walkSvc = catalogServices.FirstOrDefault(s => s.ServiceName.Contains("Dog Walking"));
                var dropInSvc = catalogServices.FirstOrDefault(s => s.ServiceName.Contains("Drop-in"));
                var houseSitSvc = catalogServices.FirstOrDefault(s => s.ServiceName.Contains("House Sitting"));

                DateTime today = DateTime.UtcNow.Date;

                // Sitter 1: Lê Hoàng Yến (Cầu Giấy, Hà Nội) - Approved
                var s1User = await context.Users.Find(u => u.Email == "sitter.yen@pawstay.vn").FirstOrDefaultAsync();
                if (s1User == null)
                {
                    s1User = new User { Email = "sitter.yen@pawstay.vn", PasswordHash = passHash, FullName = "Bác sĩ Lê Hoàng Yến", Role = "SITTER" };
                    await context.Users.InsertOneAsync(s1User);

                    var s1Profile = new SitterProfile
                    {
                        UserId = s1User.UserId,
                        Bio = "Bác sĩ thú y tốt nghiệp ĐH Nông Nghiệp, có phòng khám nhỏ tại nhà và khu nội trú riêng biệt cho chó mèo. Nhận chăm sóc cả các bé cần uống thuốc.",
                        Address = "Quận Cầu Giấy, Hà Nội",
                        ExperienceYears = 5,
                        Tags = new List<string> { "Bác sĩ thú y", "Nhận mèo bệnh/uống thuốc", "Phòng điều hòa 24/7", "Khu vực Cầu Giấy" },
                        Status = "Approved",
                        IsVerified = true,
                        ReviewedAt = DateTime.UtcNow.AddDays(-30),
                        Rating = 4.9m,
                        TotalReviews = 18
                    };
                    await context.SitterProfiles.InsertOneAsync(s1Profile);

                    if (boardingSvc != null)
                    {
                        var s1Services = new List<SitterService>
                        {
                            new SitterService { SitterId = s1Profile.SitterId, ServiceId = boardingSvc.ServiceId, Price = 180000, IsPrimary = true }
                        };
                        if (dayCareSvc != null) s1Services.Add(new SitterService { SitterId = s1Profile.SitterId, ServiceId = dayCareSvc.ServiceId, Price = 120000 });
                        if (dropInSvc != null) s1Services.Add(new SitterService { SitterId = s1Profile.SitterId, ServiceId = dropInSvc.ServiceId, Price = 60000 });
                        await context.SitterServices.InsertManyAsync(s1Services);
                    }

                    var s1Availabilities = new List<SitterAvailability>();
                    for (int i = 0; i < 14; i++)
                    {
                        s1Availabilities.Add(new SitterAvailability { SitterId = s1Profile.SitterId, AvailableDate = today.AddDays(i), IsAvailable = true });
                    }
                    await context.SitterAvailabilities.InsertManyAsync(s1Availabilities);
                }

                // Sitter 2: Vũ Minh Đức (Tây Hồ, Hà Nội) - Approved
                var s2User = await context.Users.Find(u => u.Email == "sitter.duc@pawstay.vn").FirstOrDefaultAsync();
                if (s2User == null)
                {
                    s2User = new User { Email = "sitter.duc@pawstay.vn", PasswordHash = passHash, FullName = "Vũ Minh Đức", Role = "SITTER" };
                    await context.Users.InsertOneAsync(s2User);

                    var s2Profile = new SitterProfile
                    {
                        UserId = s2User.UserId,
                        Bio = "Nhà biệt thự ven Hồ Tây có sân cỏ rào kín 150m², chuyên nhận các dòng chó vận động nhiều như Husky, Golden, Corgi. Dắt dạo Hồ Tây mỗi ngày.",
                        Address = "Quận Tây Hồ, Hà Nội",
                        ExperienceYears = 6,
                        Tags = new List<string> { "Sân cỏ 150m²", "Gần Hồ Tây", "Dắt dạo 2 lần/ngày", "Nhận chó lớn" },
                        Status = "Approved",
                        IsVerified = true,
                        ReviewedAt = DateTime.UtcNow.AddDays(-45),
                        Rating = 5.0m,
                        TotalReviews = 24
                    };
                    await context.SitterProfiles.InsertOneAsync(s2Profile);

                    if (boardingSvc != null)
                    {
                        var s2Services = new List<SitterService>
                        {
                            new SitterService { SitterId = s2Profile.SitterId, ServiceId = boardingSvc.ServiceId, Price = 250000, IsPrimary = true }
                        };
                        if (walkSvc != null) s2Services.Add(new SitterService { SitterId = s2Profile.SitterId, ServiceId = walkSvc.ServiceId, Price = 80000 });
                        await context.SitterServices.InsertManyAsync(s2Services);
                    }

                    var s2Availabilities = new List<SitterAvailability>();
                    for (int i = 0; i < 14; i++)
                    {
                        s2Availabilities.Add(new SitterAvailability { SitterId = s2Profile.SitterId, AvailableDate = today.AddDays(i), IsAvailable = true });
                    }
                    await context.SitterAvailabilities.InsertManyAsync(s2Availabilities);
                }

                // Sitter 3: Phạm Quỳnh Anh (Quận 7, TP.HCM) - Approved
                var s3User = await context.Users.Find(u => u.Email == "sitter.quynhanh@pawstay.vn").FirstOrDefaultAsync();
                if (s3User == null)
                {
                    s3User = new User { Email = "sitter.quynhanh@pawstay.vn", PasswordHash = passHash, FullName = "Phạm Quỳnh Anh", Role = "SITTER" };
                    await context.Users.InsertOneAsync(s3User);

                    var s3Profile = new SitterProfile
                    {
                        UserId = s3User.UserId,
                        Bio = "Căn hộ cao cấp Phú Mỹ Hưng Quận 7, chuyên chăm sóc mèo cưng và các bé cún nhỏ như Poodle, Phốc sóc. Có camera full HD theo dõi 24/7 cho chủ xem.",
                        Address = "Quận 7, TP. Hồ Chí Minh",
                        ExperienceYears = 3,
                        Tags = new List<string> { "Camera 24/7", "Chuyên cún nhỏ & mèo", "Máy lọc không khí", "Khu vực Q7" },
                        Status = "Approved",
                        IsVerified = true,
                        ReviewedAt = DateTime.UtcNow.AddDays(-20),
                        Rating = 4.8m,
                        TotalReviews = 14
                    };
                    await context.SitterProfiles.InsertOneAsync(s3Profile);

                    if (boardingSvc != null)
                    {
                        var s3Services = new List<SitterService>
                        {
                            new SitterService { SitterId = s3Profile.SitterId, ServiceId = boardingSvc.ServiceId, Price = 160000, IsPrimary = true }
                        };
                        if (houseSitSvc != null) s3Services.Add(new SitterService { SitterId = s3Profile.SitterId, ServiceId = houseSitSvc.ServiceId, Price = 200000 });
                        if (dayCareSvc != null) s3Services.Add(new SitterService { SitterId = s3Profile.SitterId, ServiceId = dayCareSvc.ServiceId, Price = 100000 });
                        await context.SitterServices.InsertManyAsync(s3Services);
                    }

                    var s3Availabilities = new List<SitterAvailability>();
                    for (int i = 0; i < 14; i++)
                    {
                        s3Availabilities.Add(new SitterAvailability { SitterId = s3Profile.SitterId, AvailableDate = today.AddDays(i), IsAvailable = true });
                    }
                    await context.SitterAvailabilities.InsertManyAsync(s3Availabilities);
                }

                // Sitter 4: Đặng Gia Huy (Bình Thạnh, TP.HCM) - PENDING CHỜ ADMIN DUYỆT
                var s4User = await context.Users.Find(u => u.Email == "applicant.huy@pawstay.vn").FirstOrDefaultAsync();
                if (s4User == null)
                {
                    s4User = new User { Email = "applicant.huy@pawstay.vn", PasswordHash = passHash, FullName = "Đặng Gia Huy", Role = "USER" };
                    await context.Users.InsertOneAsync(s4User);

                    var s4Profile = new SitterProfile
                    {
                        UserId = s4User.UserId,
                        Bio = "Sinh viên năm cuối ngành Thú y ĐH Nông Lâm TP.HCM, có kinh nghiệm thực tập tại trạm thú y, rất yêu quý động vật và muốn làm thêm.",
                        Address = "Quận Bình Thạnh, TP. Hồ Chí Minh",
                        ExperienceYears = 2,
                        Tags = new List<string> { "Sinh viên thú y", "Khu vực Bình Thạnh", "Nhiệt tình", "Ứng viên mới" },
                        Status = "Pending",
                        IsVerified = false,
                        ReviewedAt = null,
                        Rating = 0,
                        TotalReviews = 0
                    };
                    await context.SitterProfiles.InsertOneAsync(s4Profile);

                    if (boardingSvc != null)
                    {
                        await context.SitterServices.InsertOneAsync(new SitterService
                        {
                            SitterId = s4Profile.SitterId,
                            ServiceId = boardingSvc.ServiceId,
                            Price = 120000,
                            IsPrimary = true
                        });
                    }
                }

                // Sitter 5: Nguyễn Thu Trang (Đống Đa, Hà Nội) - PENDING CHỜ ADMIN DUYỆT
                var s5User = await context.Users.Find(u => u.Email == "applicant.trang@pawstay.vn").FirstOrDefaultAsync();
                if (s5User == null)
                {
                    s5User = new User { Email = "applicant.trang@pawstay.vn", PasswordHash = passHash, FullName = "Nguyễn Thu Trang", Role = "USER" };
                    await context.Users.InsertOneAsync(s5User);

                    var s5Profile = new SitterProfile
                    {
                        UserId = s5User.UserId,
                        Bio = "Tình nguyện viên hỗ trợ trạm cứu hộ động vật Hà Nội hơn 3 năm, có nhiều kinh nghiệm chăm sóc và nuôi dưỡng mèo mồ côi.",
                        Address = "Quận Đống Đa, Hà Nội",
                        ExperienceYears = 3,
                        Tags = new List<string> { "Cứu hộ thú cưng", "Yêu mèo", "Khu vực Đống Đa", "Ứng viên mới" },
                        Status = "Pending",
                        IsVerified = false,
                        ReviewedAt = null,
                        Rating = 0,
                        TotalReviews = 0
                    };
                    await context.SitterProfiles.InsertOneAsync(s5Profile);

                    if (boardingSvc != null)
                    {
                        await context.SitterServices.InsertOneAsync(new SitterService
                        {
                            SitterId = s5Profile.SitterId,
                            ServiceId = boardingSvc.ServiceId,
                            Price = 140000,
                            IsPrimary = true
                        });
                    }
                }

                Console.WriteLine("[Seeder] Bổ sung thêm các Sitter và Thú cưng đa dạng khu vực THÀNH CÔNG!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Seeder Lỗi] Không thể bổ sung Sitter/Pets: " + ex.Message);
            }
        }
    }
}