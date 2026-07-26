using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NBoardLocalGameServer.Web.Models;
using NBoardLocalGameServer.Web.Services;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class EngineEndpoints
    {
        internal record EngineUpdateRequest(
            string Name, string BuildCommand, string BuildWorkDir,
            string DefaultPath, string DefaultArguments, string DefaultWorkDir,
            List<string> DefaultInitialCommands);

        public static void MapEngineEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/engines");

            group.MapGet("/", (EngineStore store) => Results.Ok(store.ListAll()));

            group.MapGet("/{id}", (string id, EngineStore store)
                => store.Load(id) is { } engine ? Results.Ok(engine) : Results.NotFound());

            group.MapPost("/", async (HttpRequest request, EngineStore store) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest("Expected multipart/form-data.");

                var form = await request.ReadFormAsync();
                var name = form["name"].ToString();
                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest("\"name\" is required.");

                var zipFile = form.Files.GetFile("zip");
                if (zipFile is null)
                    return Results.BadRequest("A \"zip\" file is required.");

                var id = Guid.NewGuid().ToString("N");
                var record = new EngineRecord
                {
                    Id = id,
                    Name = name,
                    BuildCommand = form["buildCommand"].ToString(),
                    BuildWorkDir = NonEmptyOr(form["buildWorkDir"].ToString(), "."),
                    DefaultPath = form["defaultPath"].ToString(),
                    DefaultArguments = form["defaultArguments"].ToString(),
                    DefaultWorkDir = NonEmptyOr(form["defaultWorkDir"].ToString(), "."),
                    DefaultInitialCommands = SplitLines(form["defaultInitialCommands"].ToString()),
                };

                await using (var stream = zipFile.OpenReadStream())
                    await store.ExtractZipAsync(id, stream);

                store.Save(record);
                return Results.Created($"/api/engines/{id}", record);
            });

            group.MapPut("/{id}", (string id, EngineUpdateRequest body, EngineStore store) =>
            {
                var existing = store.Load(id);
                if (existing is null)
                    return Results.NotFound();

                existing.Name = body.Name;
                existing.BuildCommand = body.BuildCommand;
                existing.BuildWorkDir = body.BuildWorkDir;
                existing.DefaultPath = body.DefaultPath;
                existing.DefaultArguments = body.DefaultArguments;
                existing.DefaultWorkDir = body.DefaultWorkDir;
                existing.DefaultInitialCommands = body.DefaultInitialCommands;
                store.Save(existing);
                return Results.Ok(existing);
            });

            group.MapDelete("/{id}", (string id, EngineStore store)
                => store.Delete(id) ? Results.NoContent() : Results.NotFound());

            group.MapGet("/{id}/files", (string id, EngineStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                return Results.Ok(store.ListFiles(id));
            });

            group.MapGet("/{id}/files/content", (string id, string path, EngineStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                var resolved = store.ResolveFilePath(id, path);
                if (resolved is null || !System.IO.File.Exists(resolved))
                    return Results.NotFound();

                return Results.File(resolved, "application/octet-stream", System.IO.Path.GetFileName(resolved));
            });

            group.MapPut("/{id}/files/content", async (string id, string path, HttpRequest request, EngineStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                var resolved = store.ResolveFilePath(id, path);
                if (resolved is null)
                    return Results.BadRequest("Invalid path.");

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(resolved)!);
                await using (var fileStream = System.IO.File.Create(resolved))
                    await request.Body.CopyToAsync(fileStream);

                return Results.NoContent();
            });

            group.MapPost("/{id}/build", async (string id, EngineStore store, EngineBuildService buildService) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                try
                {
                    var result = await buildService.BuildAsync(id);
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to run the build: {ex.Message}");
                }
            });

            group.MapGet("/{id}/build-log", (string id, EngineStore store) =>
            {
                if (store.Load(id) is null)
                    return Results.NotFound();

                var logPath = store.GetBuildLogPath(id);
                if (!System.IO.File.Exists(logPath))
                    return Results.NotFound();

                return Results.File(logPath, "text/plain");
            });

            group.MapPut("/{id}/zip", async (string id, HttpRequest request, EngineStore store) =>
            {
                var existing = store.Load(id);
                if (existing is null)
                    return Results.NotFound();

                if (!request.HasFormContentType)
                    return Results.BadRequest("Expected multipart/form-data.");

                var form = await request.ReadFormAsync();
                var zipFile = form.Files.GetFile("zip");
                if (zipFile is null)
                    return Results.BadRequest("A \"zip\" file is required.");

                await using (var stream = zipFile.OpenReadStream())
                    await store.ExtractZipAsync(id, stream);

                existing.LastBuildStatus = "NotBuilt";
                existing.LastBuildAt = null;
                store.Save(existing);
                return Results.Ok(existing);
            });
        }

        static string NonEmptyOr(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        static List<string> SplitLines(string value) =>
            [.. value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
