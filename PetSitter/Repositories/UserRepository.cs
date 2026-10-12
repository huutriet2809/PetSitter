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
            var filter = Builders<User>.Filter.Eq(u => u.Email, email);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> UserExistsAsync(string email)
        {
            var filter = Builders<User>.Filter.Eq(u => u.Email, email);
            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }
    }
}
