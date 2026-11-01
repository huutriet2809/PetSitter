using PetSitter.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public interface IPetService
    {
        Task<List<PetDto>> GetMyPetsAsync(string ownerId);
        Task<PetDto?> GetPetByIdAsync(string ownerId, string petId);
        Task<PetDto> CreatePetAsync(string ownerId, CreatePetDto dto);
        Task<PetDto?> UpdatePetAsync(string ownerId, string petId, CreatePetDto dto);
        Task<bool> DeletePetAsync(string ownerId, string petId);
    }
}