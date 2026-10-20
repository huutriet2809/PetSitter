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
    }
}