using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NBoardLocalGameServer.Web.Models;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Endpoints
{
    internal static class SettingsEndpoints
    {
        public static void MapSettingsEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/settings");

            group.MapGet("/", (SettingsStore store) => Results.Ok(store.Load()));

            group.MapPut("/", (AppSettingsRecord body, SettingsStore store) =>
            {
                store.Save(body);
                return Results.Ok(body);
            });
        }
    }
}
