using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

using NBoardLocalGameServer.Web.Endpoints;
using NBoardLocalGameServer.Web.Services;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web
{
    internal static class ServeHost
    {
        // Engine data files (eval tables, coefficient files, etc.) can be tens to hundreds of MB.
        // Both Kestrel's request-body limit AND ASP.NET Core's separate multipart-form-parsing limit
        // (FormOptions.MultipartBodyLengthLimit, which defaults to 128 MiB and is otherwise unrelated
        // to Kestrel's limit) need to allow this, or uploads above 128 MiB fail with an unhandled
        // InvalidDataException from ReadFormAsync() even though Kestrel itself accepted the body.
        internal const long MaxUploadBytes = 500_000_000;

        public static async Task RunAsync(int port, string dataDir, string bindAddress = "127.0.0.1")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = System.AppContext.BaseDirectory
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = MaxUploadBytes;
            });

            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = MaxUploadBytes;
            });

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
            });

            var paths = new PathConventions(dataDir);
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton<EngineStore>();
            builder.Services.AddSingleton<BookStore>();
            builder.Services.AddSingleton<PresetStore>();
            builder.Services.AddSingleton<SettingsStore>();
            builder.Services.AddSingleton<EngineBuildService>();
            builder.Services.AddSingleton<QueueStore>();
            builder.Services.AddSingleton<HistoryStore>();
            builder.Services.AddSingleton<RunConfigBuilder>();
            builder.Services.AddSingleton<Ec2SelfStopService>();
            builder.Services.AddSingleton<HistoryExportService>();
            builder.Services.AddSingleton<QueueRunner>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<QueueRunner>());

            var app = builder.Build();

            // Defaults to loopback-only (never reachable from a public network interface), reached via
            // SSH/SSM port forwarding. --bind-address can target a private overlay-network address
            // instead (e.g. a Tailscale IP) so specific enrolled devices can connect directly.
            app.Urls.Add($"http://{bindAddress}:{port}");

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapEngineEndpoints();
            app.MapBookEndpoints();
            app.MapPresetEndpoints();
            app.MapSettingsEndpoints();
            app.MapQueueEndpoints();
            app.MapHistoryEndpoints();

            await app.RunAsync();
        }
    }
}
