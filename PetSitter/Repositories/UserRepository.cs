using Microsoft.EntityFrameworkCore;
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
            return await _dbSet.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<bool> UserExistsAsync(string email)
        {
            return await _dbSet.AnyAsync(u => u.Email == email);
        }
    }
}
