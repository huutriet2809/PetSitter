using MongoDB.Driver;
using PetSitter.Models;

namespace PetSitter.Repositories
{
    public class UserRepository : Repository<User>, IUserRepository
    {
        public UserRepository(PetSitterContext context) : base(context)
        {
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _collection.Find(u => u.Email == email).FirstOrDefaultAsync();
        }

        public async Task<bool> UserExistsAsync(string email)
        {
            return await _collection.Find(u => u.Email == email).AnyAsync();
        }
    }
}
