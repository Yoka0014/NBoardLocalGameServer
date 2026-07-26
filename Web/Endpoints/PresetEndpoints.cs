using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NBoardLocalGameServer.Web.Models;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class PresetEndpoints
    {
        internal record PresetRequest(
            string Name, GameSessionMode SessionMode, MatchMode MatchMode,
            bool SwapPlayer, bool UseSamePositionWhenSwapPlayer, bool ShuffleBook, string? BookId,
            string? TimeControlCommon, string? TimeControlFirst, string? TimeControlSecond);

        public static void MapPresetEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/presets");

            group.MapGet("/", (PresetStore store) => Results.Ok(store.ListAll()));

            group.MapGet("/{id}", (string id, PresetStore store)
                => store.Load(id) is { } preset ? Results.Ok(preset) : Results.NotFound());

            group.MapPost("/", (PresetRequest body, PresetStore store) =>
            {
                var record = new MatchPresetRecord { Id = Guid.NewGuid().ToString("N"), Name = body.Name };
                Apply(record, body);
                store.Save(record);
                return Results.Created($"/api/presets/{record.Id}", record);
            });

            group.MapPut("/{id}", (string id, PresetRequest body, PresetStore store) =>
            {
                var existing = store.Load(id);
                if (existing is null)
                    return Results.NotFound();

                Apply(existing, body);
                store.Save(existing);
                return Results.Ok(existing);
            });

            group.MapDelete("/{id}", (string id, PresetStore store)
                => store.Delete(id) ? Results.NoContent() : Results.NotFound());
        }

        static void Apply(MatchPresetRecord record, PresetRequest body)
        {
            record.Name = body.Name;
            record.SessionMode = body.SessionMode;
            record.MatchMode = body.MatchMode;
            record.SwapPlayer = body.SwapPlayer;
            record.UseSamePositionWhenSwapPlayer = body.UseSamePositionWhenSwapPlayer;
            record.ShuffleBook = body.ShuffleBook;
            record.BookId = body.BookId;
            record.TimeControlCommon = body.TimeControlCommon;
            record.TimeControlFirst = body.TimeControlFirst;
            record.TimeControlSecond = body.TimeControlSecond;
        }
    }
}
