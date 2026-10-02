using Microsoft.EntityFrameworkCore;

namespace PetSitter.Models
{
    public class PetSitterContext : DbContext
    {
        public PetSitterContext(DbContextOptions<PetSitterContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<SitterProfile> SitterProfiles { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<SitterService> SitterServices { get; set; }
        public DbSet<SitterAvailability> SitterAvailabilities { get; set; }
        public DbSet<Pet> Pets { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<SitterVerification> SitterVerifications { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Unique Constraints and cascade rules if needed
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<SitterProfile>()
                .HasIndex(sp => sp.UserId)
                .IsUnique();

            modelBuilder.Entity<Service>()
                .HasIndex(s => s.ServiceName)
                .IsUnique();

            modelBuilder.Entity<SitterService>()
                .HasIndex(ss => new { ss.SitterId, ss.ServiceId })
                .IsUnique();

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.BookingId)
                .IsUnique();

            modelBuilder.Entity<Review>()
                .HasIndex(r => r.BookingId)
                .IsUnique();
                
            modelBuilder.Entity<SitterVerification>()
                .HasIndex(sv => sv.SitterId)
                .IsUnique();

            // Set Delete behaviors to Restrict to avoid multiple cascade paths, typical for complex schemas
            foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                relationship.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
