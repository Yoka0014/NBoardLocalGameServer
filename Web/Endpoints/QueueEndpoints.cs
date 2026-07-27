using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NBoardLocalGameServer.Reversi;
using NBoardLocalGameServer.Web.Models;
using NBoardLocalGameServer.Web.Services;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class QueueEndpoints
    {
        internal record QueueCreateRequest(
            string Engine0Id, string? Engine0ArgumentsOverride, List<string>? Engine0InitialCommandsOverride,
            string Engine1Id, string? Engine1ArgumentsOverride, List<string>? Engine1InitialCommandsOverride,
            string PresetId, int Matches, int Sessions);

        public static void MapQueueEndpoints(this WebApplication app)
        {
            app.MapGet("/api/queue", (QueueStore queueStore, QueueRunner runner)
                => Results.Ok(new QueueStatusResponse(runner.GetRunningInfo(), queueStore.ListPending())));

            app.MapPost("/api/queue", (QueueCreateRequest body, EngineStore engineStore, PresetStore presetStore, QueueStore queueStore) =>
            {
                if (engineStore.Load(body.Engine0Id) is null)
                    return Results.BadRequest($"Engine \"{body.Engine0Id}\" was not found.");
                if (engineStore.Load(body.Engine1Id) is null)
                    return Results.BadRequest($"Engine \"{body.Engine1Id}\" was not found.");
                if (presetStore.Load(body.PresetId) is null)
                    return Results.BadRequest($"Preset \"{body.PresetId}\" was not found.");
                if (body.Matches <= 0)
                    return Results.BadRequest("\"Matches\" must be positive.");
                if (body.Sessions <= 0)
                    return Results.BadRequest("\"Sessions\" must be positive.");

                var entry = new QueueEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Engine0Id = body.Engine0Id,
                    Engine0ArgumentsOverride = body.Engine0ArgumentsOverride,
                    Engine0InitialCommandsOverride = body.Engine0InitialCommandsOverride,
                    Engine1Id = body.Engine1Id,
                    Engine1ArgumentsOverride = body.Engine1ArgumentsOverride,
                    Engine1InitialCommandsOverride = body.Engine1InitialCommandsOverride,
                    PresetId = body.PresetId,
                    Matches = body.Matches,
                    Sessions = body.Sessions
                };
                queueStore.Enqueue(entry);
                return Results.Created($"/api/queue/{entry.Id}", entry);
            });

            app.MapDelete("/api/queue/{id}", (string id, QueueStore queueStore)
                => queueStore.TryRemove(id) ? Results.NoContent() : Results.NotFound());

            app.MapPost("/api/queue/cancel-running", (QueueRunner runner)
                => runner.CancelRunning() ? Results.NoContent() : Results.NotFound());

            app.MapGet("/api/spectate", (QueueRunner runner) =>
            {
                var server = runner.CurrentGameServer;
                if (server is null)
                    return Results.Ok(new SpectateStatus(false, null, 0, 0, []));

                var engineNames = runner.CurrentEngineNames;

                var sessions = new List<SpectateSession>();
                // gameID % MaxSessions gives each concurrently-active game a stable slot number for its
                // whole lifetime (no more than MaxSessions games can ever be in flight at once, so this
                // is always unique among them) — this keeps a card "pinned" to one slot instead of every
                // poll re-ranking active games 1..N by ordinal position, which made a game visually jump
                // to a different "Session" card whenever any other session finished/started around it.
                foreach (var (gameId, session) in server.ActiveSessions.OrderBy(kv => kv.Key % server.MaxSessions))
                {
                    var info = session.CurrentGameInfo;
                    var finalPos = info.TryGenerateFinalPosition();
                    // A null result means we raced GameSession's move-application loop mid-update
                    // (see the plan's note on GameSession.CurrentGameInfo's known unsynchronized read) —
                    // just skip this session for this one poll rather than failing the whole request.
                    if (finalPos is null)
                        continue;

                    // Prefer the server-registered engine names (e.g. "Edax 4.6") over the engine's own
                    // self-reported NBoard name (info.Black/WhitePlayerName) — the latter can collide
                    // across different registrations of the same underlying engine binary.
                    string blackName = info.BlackPlayerName, whiteName = info.WhitePlayerName;
                    if (engineNames is { } names && server.BlackIsPlayerZero.TryGetValue(gameId, out var blackIsP0))
                    {
                        blackName = blackIsP0 ? names.Engine0Name : names.Engine1Name;
                        whiteName = blackIsP0 ? names.Engine1Name : names.Engine0Name;
                    }

                    var board = BuildBoardString(finalPos);
                    sessions.Add(new SpectateSession(
                        gameId % server.MaxSessions + 1, gameId, blackName, whiteName,
                        info.Moves.Count, board,
                        board.Count(c => c == '*'), board.Count(c => c == 'O'),
                        finalPos.SideToMove.ToString(), finalPos.IsGameOver));
                }

                object? liveStats = server.CurrentPlayerStats is { } playerStats
                    ? (server.CurrentMatchStats is { } matchStats
                        ? new SynchroStatsOutput([.. playerStats], matchStats)
                        : playerStats)
                    : null;

                return Results.Ok(new SpectateStatus(true, runner.CurrentMatchId, server.TotalGameCount, server.CompletedGameCount, sessions, liveStats));
            });
        }

        static string BuildBoardString(Position pos)
        {
            const string discs = "*O-";
            var chars = new char[64];
            var i = 0;
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
                chars[i++] = discs[(int)pos.GetSquareColorAt(coord)];
            return new string(chars);
        }
    }
}
