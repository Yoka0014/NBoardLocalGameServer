using System.IO;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class HistoryEndpoints
    {
        public static void MapHistoryEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/history");

            group.MapGet("/", (HistoryStore store) => Results.Ok(store.ListAll()));

            group.MapGet("/{id}", (string id, HistoryStore store)
                => store.Load(id) is { } entry ? Results.Ok(entry) : Results.NotFound());

            group.MapGet("/{id}/stats.json", (string id, HistoryStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                var path = store.StatsPath(id);
                return File.Exists(path) ? Results.File(path, "application/json", "stats.json") : Results.NotFound();
            });

            group.MapGet("/{id}/record.ggf", (string id, HistoryStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                var path = store.RecordPath(id);
                return File.Exists(path) ? Results.File(path, "application/octet-stream", "record.ggf") : Results.NotFound();
            });

            group.MapDelete("/{id}", (string id, HistoryStore store)
                => store.Delete(id) ? Results.NoContent() : Results.NotFound());
        }
    }
}
