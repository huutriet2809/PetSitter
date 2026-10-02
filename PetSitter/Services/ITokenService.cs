using PetSitter.Models;

namespace PetSitter.Services
{
    public interface ITokenService
    {
        string GenerateToken(User user);
    }
}
