using MongoDB.Driver;
using PetSitter.Data;
using PetSitter.DTOs;
using PetSitter.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class PetService : IPetService
    {
        private readonly IMongoCollection<Pet> _pets;

        public PetService(PetSitterContext context)
        {
            _pets = context.Pets;
        }

        public async Task<List<PetDto>> GetMyPetsAsync(string ownerId)
        {
            var pets = await _pets.Find(p => p.OwnerId == ownerId).ToListAsync();
            return pets.Select(p => new PetDto
            {
                PetId = p.PetId,
                Name = p.Name,
                Species = p.Species,
                Breed = p.Breed,
                Gender = p.Gender,
                Age = p.Age,
                Weight = p.Weight,
                HealthStatus = p.HealthStatus,
                DietaryNotes = p.DietaryNotes,
                AvatarUrl = p.AvatarUrl
            }).ToList();
        }

        public async Task<PetDto> CreatePetAsync(string ownerId, CreatePetDto dto)
        {
            var pet = new Pet
            {
                OwnerId = ownerId,
                Name = dto.Name,
                Species = dto.Species,
                Breed = dto.Breed,
                Gender = dto.Gender,
                Age = dto.Age,
                Weight = dto.Weight,
                HealthStatus = dto.HealthStatus,
                DietaryNotes = dto.DietaryNotes,
                AvatarUrl = dto.AvatarUrl
            };
            await _pets.InsertOneAsync(pet);

            return new PetDto
            {
                PetId = pet.PetId,
                Name = pet.Name,
                Species = pet.Species,
                Breed = pet.Breed,
                Gender = pet.Gender,
                Age = pet.Age,
                Weight = pet.Weight,
                HealthStatus = pet.HealthStatus,
                DietaryNotes = pet.DietaryNotes,
                AvatarUrl = pet.AvatarUrl
            };
        }

        public async Task<PetDto?> UpdatePetAsync(string ownerId, string petId, CreatePetDto dto)
        {
            var filter = Builders<Pet>.Filter.And(
                Builders<Pet>.Filter.Eq(p => p.PetId, petId),
                Builders<Pet>.Filter.Eq(p => p.OwnerId, ownerId)
            );

            var update = Builders<Pet>.Update
                .Set(p => p.Name, dto.Name)
                .Set(p => p.Species, dto.Species)
                .Set(p => p.Breed, dto.Breed)
                .Set(p => p.Gender, dto.Gender)
                .Set(p => p.Age, dto.Age)
                .Set(p => p.Weight, dto.Weight)
                .Set(p => p.HealthStatus, dto.HealthStatus)
                .Set(p => p.DietaryNotes, dto.DietaryNotes)
                .Set(p => p.AvatarUrl, dto.AvatarUrl);

            var options = new FindOneAndUpdateOptions<Pet> { ReturnDocument = ReturnDocument.After };
            var pet = await _pets.FindOneAndUpdateAsync(filter, update, options);

            if (pet == null) return null;

            return new PetDto
            {
                PetId = pet.PetId,
                Name = pet.Name,
                Species = pet.Species,
                Breed = pet.Breed,
                Gender = pet.Gender,
                Age = pet.Age,
                Weight = pet.Weight,
                HealthStatus = pet.HealthStatus,
                DietaryNotes = pet.DietaryNotes,
                AvatarUrl = pet.AvatarUrl
            };
        }

        public async Task<bool> DeletePetAsync(string ownerId, string petId)
        {
            var filter = Builders<Pet>.Filter.And(
                Builders<Pet>.Filter.Eq(p => p.PetId, petId),
                Builders<Pet>.Filter.Eq(p => p.OwnerId, ownerId)
            );
            var result = await _pets.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }
    }
}