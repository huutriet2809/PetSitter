using MongoDB.Driver;
using PetSitter.Models;
using System.Linq.Expressions;

namespace PetSitter.Repositories
{
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly IMongoCollection<T> _collection;

        public Repository(PetSitterContext context)
        {
            var collectionName = typeof(T).Name;
            if (collectionName.EndsWith("y")) collectionName = collectionName.Substring(0, collectionName.Length - 1) + "ies";
            else if (collectionName == "SitterProfile") collectionName = "SitterProfiles";
            else if (collectionName == "SitterService") collectionName = "SitterServices";
            else if (collectionName == "SitterAvailability") collectionName = "SitterAvailabilities";
            else if (collectionName == "SitterVerification") collectionName = "SitterVerifications";
            else collectionName += "s";
            
            _collection = context.GetCollection<T>(collectionName);
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _collection.Find(_ => true).ToListAsync();
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            var idProperty = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty(typeof(T).Name + "Id");
            if (idProperty == null) return null;
            var filter = Builders<T>.Filter.Eq(idProperty.Name, id);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return await _collection.Find(predicate).ToListAsync();
        }

        public async Task AddAsync(T entity)
        {
            await _collection.InsertOneAsync(entity);
        }

        public async Task AddRangeAsync(IEnumerable<T> entities)
        {
            await _collection.InsertManyAsync(entities);
        }

        public void Update(T entity)
        {
            var idProperty = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty(typeof(T).Name + "Id");
            if (idProperty != null)
            {
                var id = idProperty.GetValue(entity)?.ToString();
                var filter = Builders<T>.Filter.Eq(idProperty.Name, id);
                _collection.ReplaceOne(filter, entity);
            }
        }

        public void Remove(T entity)
        {
            var idProperty = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty(typeof(T).Name + "Id");
            if (idProperty != null)
            {
                var id = idProperty.GetValue(entity)?.ToString();
                var filter = Builders<T>.Filter.Eq(idProperty.Name, id);
                _collection.DeleteOne(filter);
            }
        }

        public void RemoveRange(IEnumerable<T> entities)
        {
            foreach (var entity in entities)
            {
                Remove(entity);
            }
        }

        public async Task<bool> SaveChangesAsync()
        {
            return await Task.FromResult(true);
        }
    }
}
