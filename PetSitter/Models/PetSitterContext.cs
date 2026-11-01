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

        public IMongoDatabase Database => _database;

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
        public IMongoCollection<Notification> Notifications => _database.GetCollection<Notification>("Notifications");
        public IMongoCollection<Payment> Payments => _database.GetCollection<Payment>("Payments");
        public IMongoCollection<JournalEntry> JournalEntries => _database.GetCollection<JournalEntry>("JournalEntries");
        public IMongoCollection<RefundRequest> RefundRequests => _database.GetCollection<RefundRequest>("RefundRequests");
        public IMongoCollection<WithdrawalRequest> WithdrawalRequests => _database.GetCollection<WithdrawalRequest>("WithdrawalRequests");
        public IMongoCollection<EarningsAccount> EarningsAccounts => _database.GetCollection<EarningsAccount>("EarningsAccounts");
        public IMongoCollection<PlatformAccount> PlatformAccounts => _database.GetCollection<PlatformAccount>("PlatformAccounts");
        public IMongoCollection<RevenueSweep> RevenueSweeps => _database.GetCollection<RevenueSweep>("RevenueSweeps");
        public IMongoCollection<ReconciliationRun> ReconciliationRuns => _database.GetCollection<ReconciliationRun>("ReconciliationRuns");
        public IMongoCollection<BankReconciliationRun> BankReconciliationRuns => _database.GetCollection<BankReconciliationRun>("BankReconciliationRuns");
        public IMongoCollection<EmailLog> EmailLogs => _database.GetCollection<EmailLog>("EmailLogs");

        public IMongoCollection<T> GetCollection<T>(string name)
        {
            return _database.GetCollection<T>(name);
        }
    }
}
