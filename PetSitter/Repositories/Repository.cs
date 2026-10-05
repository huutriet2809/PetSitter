using MongoDB.Bson;
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
            _collection = context.GetCollection<T>();
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _collection.Find(FilterDefinition<T>.Empty).ToListAsync();
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
            {
                return null;
            }

            return await _collection
                .Find(new BsonDocument("_id", objectId))
                .FirstOrDefaultAsync();
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

        public async Task UpdateAsync(T entity)
        {
            await _collection.ReplaceOneAsync(GetIdFilter(entity), entity);
        }

        public async Task RemoveAsync(T entity)
        {
            await _collection.DeleteOneAsync(GetIdFilter(entity));
        }

        public async Task RemoveRangeAsync(IEnumerable<T> entities)
        {
            var ids = entities.Select(GetId).ToList();
            if (ids.Count > 0)
            {
                await _collection.DeleteManyAsync(
                    Builders<T>.Filter.In("_id", ids));
            }
        }

        private static FilterDefinition<T> GetIdFilter(T entity)
        {
            return new BsonDocument("_id", GetId(entity));
        }

        private static BsonValue GetId(T entity)
        {
            var id = entity.ToBsonDocument()["_id"];
            if (id.BsonType != BsonType.ObjectId)
            {
                throw new InvalidOperationException($"The {typeof(T).Name} entity must use an ObjectId.");
            }

            return id;
        }
    }
}
