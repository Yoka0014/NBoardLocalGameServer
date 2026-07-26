using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using NBoardLocalGameServer.Web.Models;

namespace NBoardLocalGameServer.Web.Storage
{
    /// <summary>Named match presets, stored as one JSON file per id (no content subfolder needed).</summary>
    internal class PresetStore(PathConventions paths)
    {
        readonly object _lock = new();

        public IReadOnlyList<MatchPresetRecord> ListAll()
        {
            lock (_lock)
            {
                var results = new List<MatchPresetRecord>();
                foreach (var file in Directory.EnumerateFiles(paths.PresetsDir, "*.json"))
                {
                    try
                    {
                        var preset = JsonSerializer.Deserialize<MatchPresetRecord>(File.ReadAllText(file));
                        if (preset is not null)
                            results.Add(preset);
                    }
                    catch (JsonException) { }
                }
                return [.. results.OrderBy(p => p.CreatedAt)];
            }
        }

        public MatchPresetRecord? Load(string id)
        {
            lock (_lock)
            {
                var path = paths.PresetPath(id);
                if (!File.Exists(path))
                    return null;

                try
                {
                    return JsonSerializer.Deserialize<MatchPresetRecord>(File.ReadAllText(path));
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        public void Save(MatchPresetRecord record)
        {
            lock (_lock)
                File.WriteAllText(paths.PresetPath(record.Id), JsonSerializer.Serialize(record, JsonConventions.Options));
        }

        public bool Delete(string id)
        {
            lock (_lock)
            {
                var path = paths.PresetPath(id);
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                return true;
            }
        }
    }
}
