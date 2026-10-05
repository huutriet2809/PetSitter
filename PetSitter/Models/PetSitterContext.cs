using MongoDB.Driver;

namespace PetSitter.Models
{
    public sealed class PetSitterContext
    {
        private readonly IMongoDatabase _database;

        public PetSitterContext(string connectionString, string databaseName)
        {
            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        public IMongoCollection<T> GetCollection<T>()
        {
            return _database.GetCollection<T>(CollectionName<T>());
        }

        public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
        {
            await GetCollection<User>().Indexes.CreateOneAsync(
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(user => user.Email),
                    new CreateIndexOptions { Unique = true, Name = "ux_users_email" }),
                cancellationToken: cancellationToken);

            await GetCollection<SitterProfile>().Indexes.CreateOneAsync(
                new CreateIndexModel<SitterProfile>(
                    Builders<SitterProfile>.IndexKeys.Ascending(profile => profile.UserId),
                    new CreateIndexOptions { Unique = true, Name = "ux_sitter_profiles_user_id" }),
                cancellationToken: cancellationToken);

            await GetCollection<Service>().Indexes.CreateOneAsync(
                new CreateIndexModel<Service>(
                    Builders<Service>.IndexKeys.Ascending(service => service.ServiceName),
                    new CreateIndexOptions { Unique = true, Name = "ux_services_name" }),
                cancellationToken: cancellationToken);

            await GetCollection<SitterService>().Indexes.CreateOneAsync(
                new CreateIndexModel<SitterService>(
                    Builders<SitterService>.IndexKeys
                        .Ascending(service => service.SitterId)
                        .Ascending(service => service.ServiceId),
                    new CreateIndexOptions { Unique = true, Name = "ux_sitter_services_sitter_service" }),
                cancellationToken: cancellationToken);

            await GetCollection<Payment>().Indexes.CreateOneAsync(
                new CreateIndexModel<Payment>(
                    Builders<Payment>.IndexKeys.Ascending(payment => payment.BookingId),
                    new CreateIndexOptions { Unique = true, Name = "ux_payments_booking_id" }),
                cancellationToken: cancellationToken);

            await GetCollection<Review>().Indexes.CreateOneAsync(
                new CreateIndexModel<Review>(
                    Builders<Review>.IndexKeys.Ascending(review => review.BookingId),
                    new CreateIndexOptions { Unique = true, Name = "ux_reviews_booking_id" }),
                cancellationToken: cancellationToken);

            await GetCollection<SitterVerification>().Indexes.CreateOneAsync(
                new CreateIndexModel<SitterVerification>(
                    Builders<SitterVerification>.IndexKeys.Ascending(verification => verification.SitterId),
                    new CreateIndexOptions { Unique = true, Name = "ux_sitter_verifications_sitter_id" }),
                cancellationToken: cancellationToken);
        }

        private static string CollectionName<T>()
        {
            return typeof(T) switch
            {
                var type when type == typeof(User) => "users",
                var type when type == typeof(SitterProfile) => "sitterProfiles",
                var type when type == typeof(Service) => "services",
                var type when type == typeof(SitterService) => "sitterServices",
                var type when type == typeof(SitterAvailability) => "sitterAvailabilities",
                var type when type == typeof(Pet) => "pets",
                var type when type == typeof(Booking) => "bookings",
                var type when type == typeof(Payment) => "payments",
                var type when type == typeof(Conversation) => "conversations",
                var type when type == typeof(Review) => "reviews",
                var type when type == typeof(SitterVerification) => "sitterVerifications",
                _ => throw new InvalidOperationException($"No MongoDB collection is configured for {typeof(T).Name}.")
            };
        }
    }
}
