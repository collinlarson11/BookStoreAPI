using BookStoreApi.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BookStoreApi.Services;

public record TopBookDto(string Name, decimal Price);
public record PriceUpdateDto(decimal Price);

public class BooksService
{
    private readonly IMongoCollection<Book> _booksCollection;

    public BooksService(IOptions<BookStoreDatabaseSettings> bookStoreDatabaseSettings)
    {
        var mongoClient = new MongoClient(
            bookStoreDatabaseSettings.Value.ConnectionString);

        var mongoDatabase = mongoClient.GetDatabase(
            bookStoreDatabaseSettings.Value.DatabaseName);

        _booksCollection = mongoDatabase.GetCollection<Book>(
            bookStoreDatabaseSettings.Value.BooksCollectionName);
    }

    public async Task<List<Book>> GetAsync() =>
        await _booksCollection.Find(_ => true).ToListAsync();

    public async Task<Book?> GetAsync(string id) =>
        await _booksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task CreateAsync(Book newBook) =>
        await _booksCollection.InsertOneAsync(newBook);

    public async Task UpdateAsync(string id, Book updatedBook) =>
        await _booksCollection.ReplaceOneAsync(x => x.Id == id, updatedBook);

    public async Task RemoveAsync(string id) =>
        await _booksCollection.DeleteOneAsync(x => x.Id == id);

    public async Task<List<TopBookDto>> GetTopAsync(int count = 3)
    {
        var sort = Builders<Book>.Sort.Descending(book => book.Price);

        var projection = Builders<Book>.Projection
            .Include(book => book.Name)
            .Include(book => book.Price)
            .Exclude(book => book.Id);

        return await _booksCollection
            .Find(Builders<Book>.Filter.Empty)
            .Sort(sort)
            .Limit(count)
            .Project<TopBookDto>(projection)
            .ToListAsync();
    }

    public static List<string> ValidateBooks(IEnumerable<Book> books)
    {
        var errors = new List<string>();
        var index = 0;

        foreach (var book in books)
        {
            var bookLabel = string.IsNullOrWhiteSpace(book.Name)
                ? $"Item[{index}]"
                : $"'{book.Name}' (Item[{index}])";

            if (string.IsNullOrWhiteSpace(book.Name))
                errors.Add($"{bookLabel}: Name is required.");

            if (string.IsNullOrWhiteSpace(book.Category))
                errors.Add($"{bookLabel}: Category is required.");

            if (string.IsNullOrWhiteSpace(book.Author))
                errors.Add($"{bookLabel}: Author is required.");

            if (book.Price <= 0)
                errors.Add($"{bookLabel}: Price must be greater than 0.");

            index++;
        }

        return errors;
    }

    public async Task<(int InsertedCount, List<string?> Ids)> CreateManyAsync(List<Book> books)
    {
        await _booksCollection.InsertManyAsync(books);
        var ids = books.Select(b => b.Id).ToList();
        return (books.Count, ids);
    }

    public async Task<List<Book>> SearchAsync(string? category, decimal? minPrice, decimal? maxPrice)
    {
        var filters = new List<FilterDefinition<Book>>();

        if (!string.IsNullOrWhiteSpace(category))
        {
            // Case-insensitive exact match on Category
            filters.Add(Builders<Book>.Filter.Regex(
                book => book.Category,
                new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(category.Trim())}$", "i")));
        }

        if (minPrice.HasValue)
        {
            filters.Add(Builders<Book>.Filter.Gte(book => book.Price, minPrice.Value));
        }

        if (maxPrice.HasValue)
        {
            filters.Add(Builders<Book>.Filter.Lte(book => book.Price, maxPrice.Value));
        }

        var combinedFilter = filters.Count > 0
            ? Builders<Book>.Filter.And(filters)
            : Builders<Book>.Filter.Empty;

        return await _booksCollection.Find(combinedFilter).ToListAsync();
    }

    public async Task<bool> UpdatePriceAsync(string id, decimal newPrice)
    {
        var filter = Builders<Book>.Filter.Eq(book => book.Id, id);
        var update = Builders<Book>.Update.Set(book => book.Price, newPrice);

        var result = await _booksCollection.UpdateOneAsync(filter, update);
        return result.MatchedCount > 0;
    }
}