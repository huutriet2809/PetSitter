using MongoDB.Driver;

namespace PetSitter.Models
{
    public class PetSitterContext
    {
        private readonly IMongoDatabase _database;

        public PetSitterContext(IMongoDatabase database)
        {
            _database = database;
        }

        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<SitterProfile> SitterProfiles => _database.GetCollection<SitterProfile>("SitterProfiles");
        public IMongoCollection<Service> Services => _database.GetCollection<Service>("Services");
        public IMongoCollection<SitterService> SitterServices => _database.GetCollection<SitterService>("SitterServices");
        public IMongoCollection<SitterAvailability> SitterAvailabilities => _database.GetCollection<SitterAvailability>("SitterAvailabilities");
        public IMongoCollection<Pet> Pets => _database.GetCollection<Pet>("Pets");
        public IMongoCollection<Booking> Bookings => _database.GetCollection<Booking>("Bookings");
        public IMongoCollection<StayUpdate> StayUpdates => _database.GetCollection<StayUpdate>("StayUpdates");
        public IMongoCollection<PasswordReset> PasswordResets => _database.GetCollection<PasswordReset>("PasswordResets");
        public IMongoCollection<ContactMessage> ContactMessages => _database.GetCollection<ContactMessage>("ContactMessages");
        public IMongoCollection<Newsletter> Newsletters => _database.GetCollection<Newsletter>("Newsletters");
        public IMongoCollection<Conversation> Conversations => _database.GetCollection<Conversation>("Conversations");
        public IMongoCollection<Review> Reviews => _database.GetCollection<Review>("Reviews");
        public IMongoCollection<SitterVerification> SitterVerifications => _database.GetCollection<SitterVerification>("SitterVerifications");
        public IMongoCollection<RefreshToken> RefreshTokens => _database.GetCollection<RefreshToken>("RefreshTokens");

        public IMongoCollection<T> GetCollection<T>(string name)
        {
            return _database.GetCollection<T>(name);
        }
    }
}
