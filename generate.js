const fs = require('fs');
let code = fs.readFileSync('c:/PetSitter/PetSitter/wwwroot/assets/data.js', 'utf8');
code = code + '\nmodule.exports = { SITTERS_DATA, REVIEWS_DATA, ADDONS };';
fs.writeFileSync('temp_data.js', code);
const data = require('./temp_data.js');

const csharpCode = `
using MongoDB.Driver;
using PetSitter.Models;
using System.Text.Json;

namespace PetSitter.Data
{
    public class MongoSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PetSitterContext>();

            // Seed Addons to Services
            var servicesCollection = context.Services;
            if (await servicesCollection.CountDocumentsAsync(_ => true) == 0)
            {
                var services = new List<Service> {
                    ${data.ADDONS.map(a => `new Service { ServiceName = "${a.ten}", Description = "${a.moTa}", BasePrice = ${a.gia} }`).join(',\n                    ')}
                };
                await servicesCollection.InsertManyAsync(services);
            }

            var usersCollection = context.Users;
            var sittersCollection = context.SitterProfiles;
            
            if (await usersCollection.CountDocumentsAsync(_ => true) == 0 && await sittersCollection.CountDocumentsAsync(_ => true) == 0)
            {
                var users = new List<User>();
                var profiles = new List<SitterProfile>();

                ${data.SITTERS_DATA.map((s, i) => `
                var user${i} = new User {
                    FullName = "${s.ten}",
                    Email = "sitter${s.id}@pawstay.local",
                    PasswordHash = "seed",
                    Role = "SITTER",
                    CreatedAt = DateTime.UtcNow
                };
                users.Add(user${i});
                `).join('')}
                
                await usersCollection.InsertManyAsync(users);

                ${data.SITTERS_DATA.map((s, i) => `
                profiles.Add(new SitterProfile {
                    UserId = user${i}.Id,
                    Bio = "${s.moTa}",
                    ExperienceYears = ${parseInt(s.kinhNghiem) || 0},
                    Address = "${s.diaChiDay}",
                    AvatarUrl = "${s.anh}",
                    IsVerified = ${s.xacMinh ? 'true' : 'false'},
                    Rating = ${s.rating}m,
                    TotalReviews = ${s.soReview}
                });
                `).join('')}

                await sittersCollection.InsertManyAsync(profiles);
                
                var reviewsCollection = context.Reviews;
                if (await reviewsCollection.CountDocumentsAsync(_ => true) == 0)
                {
                    var reviews = new List<Review>();
                    ${data.REVIEWS_DATA.map(r => `
                    reviews.Add(new Review {
                        SitterId = users[${data.SITTERS_DATA.findIndex(s => s.id === r.sitterId)}].Id,
                        Rating = ${r.rating},
                        Comment = "${r.noiDung.replace(/"/g, '\\"')}",
                        CreatedAt = DateTime.Parse("${r.ngay}")
                    });
                    `).join('')}
                    await reviewsCollection.InsertManyAsync(reviews);
                }
            }
        }
    }
}
`;

if (!fs.existsSync('c:/PetSitter/PetSitter/Data')) {
    fs.mkdirSync('c:/PetSitter/PetSitter/Data');
}
fs.writeFileSync('c:/PetSitter/PetSitter/Data/MongoSeeder.cs', csharpCode);
