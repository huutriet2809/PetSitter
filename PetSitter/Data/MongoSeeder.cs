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

            // Create Unique Index for BookedDates (SitterId, Date)
            var bookedDatesCollection = context.BookedDates;
            try
            {
                var bdIndexKeys = Builders<BookedDate>.IndexKeys.Ascending(b => b.SitterId).Ascending(b => b.Date);
                var bdIndexOptions = new CreateIndexOptions { Unique = true };
                var bdIndexModel = new CreateIndexModel<BookedDate>(bdIndexKeys, bdIndexOptions);
                await bookedDatesCollection.Indexes.CreateOneAsync(bdIndexModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not create BookedDates index: " + ex.Message);
            }

            // Create Unique Index for Bookings (MaDatLich)
            var bookingsCollection = context.Bookings;
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
            } catch (Exception ex) { Console.WriteLine("Could not create Newsletters index: " + ex.Message); }

            
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
                            new SitterService { SitterId = profile.SitterId, ServiceId = services[0].ServiceId, Price = 150000 },
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
        }
    }
}