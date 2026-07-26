using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using NBoardLocalGameServer.Web.Models;

namespace NBoardLocalGameServer.Web.Storage
{
    /// <summary>
    /// One folder per completed (or running) match: meta.json (this store) plus stats.json/record.ggf,
    /// which QueueRunner has GameServer write to directly via StatsPath/RecordPath — no copy step, so
    /// their content is byte-identical in shape to an equivalent CLI run.
    /// </summary>
    internal class HistoryStore(PathConventions paths)
    {
        public IReadOnlyList<HistoryEntryRecord> ListAll()
        {
            var results = new List<HistoryEntryRecord>();
            if (!Directory.Exists(paths.HistoryDir))
                return results;

            foreach (var dir in Directory.EnumerateDirectories(paths.HistoryDir))
            {
                var record = LoadInternal(Path.GetFileName(dir));
                if (record is not null)
                    results.Add(record);
            }
            return [.. results.OrderByDescending(r => r.StartedAt)];
        }

        public HistoryEntryRecord? Load(string id) => LoadInternal(id);

        HistoryEntryRecord? LoadInternal(string id)
        {
            var path = paths.HistoryMetaPath(id);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<HistoryEntryRecord>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public void Save(HistoryEntryRecord record)
        {
            Directory.CreateDirectory(paths.HistoryEntryDir(record.Id));
            File.WriteAllText(paths.HistoryMetaPath(record.Id), JsonSerializer.Serialize(record, JsonConventions.Options));
        }

        public string StatsPath(string id) => paths.HistoryStatsPath(id);
        public string RecordPath(string id) => paths.HistoryRecordPath(id);

        public bool Delete(string id)
        {
            var dir = paths.HistoryEntryDir(id);
            if (!Directory.Exists(dir))
                return false;

            Directory.Delete(dir, recursive: true);
            return true;
        }
    }
}
