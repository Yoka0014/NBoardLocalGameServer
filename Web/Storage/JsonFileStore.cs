using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NBoardLocalGameServer.Web.Storage
{
    internal static class JsonConventions
    {
        // Mirrors GameServerConfig/PlayerConfig's own JsonSerializerOptions{WriteIndented=true}
        // (no naming policy) so on-disk JSON stays PascalCase and consistent with the CLI's config files.
        public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    }

    /// <summary>
    /// Generic per-id-folder JSON manifest store, used for entities that also own a content
    /// subfolder (engines' extracted source, books' uploaded file).
    /// </summary>
    internal class JsonFileStore<T>(string rootDir, string manifestFileName) where T : class
    {
        readonly object _lock = new();

        public IReadOnlyList<(string Id, T Record)> LoadAll()
        {
            lock (_lock)
            {
                var results = new List<(string, T)>();
                if (!Directory.Exists(rootDir))
                    return results;

                foreach (var dir in Directory.EnumerateDirectories(rootDir))
                {
                    var id = Path.GetFileName(dir);
                    var record = LoadUnlocked(id);
                    if (record is not null)
                        results.Add((id, record));
                }
                return results;
            }
        }

        public T? Load(string id)
        {
            lock (_lock)
                return LoadUnlocked(id);
        }

        T? LoadUnlocked(string id)
        {
            var path = Path.Combine(rootDir, id, manifestFileName);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public void Save(string id, T record)
        {
            lock (_lock)
            {
                var dir = Path.Combine(rootDir, id);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, manifestFileName), JsonSerializer.Serialize(record, JsonConventions.Options));
            }
        }

        public bool Delete(string id)
        {
            lock (_lock)
            {
                var dir = Path.Combine(rootDir, id);
                if (!Directory.Exists(dir))
                    return false;

                Directory.Delete(dir, recursive: true);
                return true;
            }
        }
    }
}
