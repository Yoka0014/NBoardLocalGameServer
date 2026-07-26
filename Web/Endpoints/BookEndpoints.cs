using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class BookEndpoints
    {
        public static void MapBookEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/books");

            group.MapGet("/", (BookStore store) => Results.Ok(store.ListAll()));

            group.MapGet("/{id}", (string id, BookStore store)
                => store.Load(id) is { } book ? Results.Ok(book) : Results.NotFound());

            group.MapPost("/", async (HttpRequest request, BookStore store) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest("Expected multipart/form-data.");

                var form = await request.ReadFormAsync();
                var name = form["name"].ToString();
                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest("\"name\" is required.");

                var file = form.Files.GetFile("file");
                if (file is null)
                    return Results.BadRequest("A \"file\" is required.");

                var id = Guid.NewGuid().ToString("N");
                try
                {
                    await using var stream = file.OpenReadStream();
                    var record = await store.SaveAsync(id, name, stream);
                    return Results.Created($"/api/books/{id}", record);
                }
                catch (Exception ex)
                {
                    store.Delete(id);
                    return Results.BadRequest($"Invalid opening book file: {ex.Message}");
                }
            });

            group.MapGet("/{id}/content", (string id, BookStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                var path = store.GetFilePath(id);
                if (!System.IO.File.Exists(path))
                    return Results.NotFound();

                return Results.File(path, "text/plain", System.IO.Path.GetFileName(path));
            });

            group.MapDelete("/{id}", (string id, BookStore store)
                => store.Delete(id) ? Results.NoContent() : Results.NotFound());
        }
    }
}
