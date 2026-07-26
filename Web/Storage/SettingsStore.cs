using System.IO;
using System.Text.Json;

using NBoardLocalGameServer.Web.Models;

namespace NBoardLocalGameServer.Web.Storage
{
    /// <summary>Single-file application settings (e.g. AutoStopWhenQueueEmpty).</summary>
    internal class SettingsStore(PathConventions paths)
    {
        readonly object _lock = new();

        public AppSettingsRecord Load()
        {
            lock (_lock)
            {
                if (!File.Exists(paths.SettingsFilePath))
                    return new AppSettingsRecord();

                try
                {
                    return JsonSerializer.Deserialize<AppSettingsRecord>(File.ReadAllText(paths.SettingsFilePath)) ?? new AppSettingsRecord();
                }
                catch (JsonException)
                {
                    return new AppSettingsRecord();
                }
            }
        }

        public void Save(AppSettingsRecord record)
        {
            lock (_lock)
                File.WriteAllText(paths.SettingsFilePath, JsonSerializer.Serialize(record, JsonConventions.Options));
        }
    }
}
