using BookStoreApi.Models;
using BookStoreApi.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Read the BookStoreDatabase settings and register the service (one MongoClient for the app)
builder.Services.Configure<BookStoreDatabaseSettings>(
    builder.Configuration.GetSection("BookStoreDatabase"));
builder.Services.AddSingleton<BooksService>();

// 2. Keep JSON property names as written in C# (Name, Price), not camelCase
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// 3. OpenAPI document at /openapi/v1.json.
//    Version 3.0 so Swagger UI can read it (.NET 11 defaults to 3.2, which Swagger UI rejects).
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
});

var app = builder.Build();

// 4. Swagger UI at /swagger (Development only).
//    Needs: dotnet add package Swashbuckle.AspNetCore.SwaggerUI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "BookStore API");
    });
}

// 5. Endpoints, all under /books, shown under "Books" in Swagger
var books = app.MapGroup("/books").WithTags("Books");

// GET /books
books.MapGet("/", async (BooksService service) =>
    await service.GetAsync());

// GET /books/search?category=&minPrice=&maxPrice=
books.MapGet("/search", async (
    string? category,
    decimal? minPrice,
    decimal? maxPrice,
    BooksService service) =>
{
    if (minPrice.HasValue && maxPrice.HasValue && minPrice.Value > maxPrice.Value)
    {
        return Results.BadRequest(new { message = "minPrice cannot be greater than maxPrice." });
    }

    var results = await service.SearchAsync(category, minPrice, maxPrice);
    return Results.Ok(results);
});

// GET /books/top?count=3
books.MapGet("/top", async (int? count, BooksService service) =>
{
    var topCount = count ?? 3;

    if (topCount <= 0)
    {
        return Results.BadRequest(new { message = "count must be greater than 0." });
    }

    var results = await service.GetTopAsync(topCount);
    return Results.Ok(results);
});

// GET /books/{id}   (an ObjectId is 24 characters)
books.MapGet("/{id:length(24)}", async (string id, BooksService service) =>
    await service.GetAsync(id) is Book book
        ? Results.Ok(book)
        : Results.NotFound());

// POST /books
books.MapPost("/", async (Book newBook, BooksService service) =>
{
    await service.CreateAsync(newBook);
    return Results.Created($"/books/{newBook.Id}", newBook);
});

// PUT /books/{id}
books.MapPut("/{id:length(24)}", async (string id, Book updatedBook, BooksService service) =>
{
    var book = await service.GetAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    updatedBook.Id = book.Id;
    await service.UpdateAsync(id, updatedBook);
    return Results.NoContent();
});

// PATCH /books/{id}/price
books.MapPatch("/{id:length(24)}/price", async (
    string id,
    PriceUpdateDto body,
    BooksService service) =>
{
    if (body.Price <= 0)
    {
        return Results.BadRequest(new { message = "Price must be greater than 0." });
    }

    var updated = await service.UpdatePriceAsync(id, body.Price);
    return updated ? Results.NoContent() : Results.NotFound();
});

// DELETE /books/{id}
books.MapDelete("/{id:length(24)}", async (string id, BooksService service) =>
{
    var book = await service.GetAsync(id);
    if (book is null)
    {
        return Results.NotFound();
    }

    await service.RemoveAsync(id);
    return Results.NoContent();
});

// POST /books/upload
books.MapPost("/upload", async (IFormFile file, BooksService service) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "Please upload a non-empty .json file." });
    }

    if (!Path.GetExtension(file.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { message = "Only .json files are supported." });
    }

    List<Book>? uploadedBooks;
    try
    {
        await using var stream = file.OpenReadStream();
        uploadedBooks = await System.Text.Json.JsonSerializer.DeserializeAsync<List<Book>>(stream);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { message = "Invalid JSON format." });
    }

    if (uploadedBooks is null || uploadedBooks.Count == 0)
    {
        return Results.BadRequest(new { message = "The JSON file must contain a non-empty array of books." });
    }

    var validationErrors = BooksService.ValidateBooks(uploadedBooks);
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new
        {
            message = "Validation failed. No books were inserted.",
            errors = validationErrors
        });
    }

    var (insertedCount, ids) = await service.CreateManyAsync(uploadedBooks);

    return Results.Ok(new
    {
        message = $"Successfully inserted {insertedCount} book(s).",
        insertedCount,
        ids
    });
})
.DisableAntiforgery();

app.Run();