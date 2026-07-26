using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using NBoardLocalGameServer.Web.Models;

namespace NBoardLocalGameServer.Web.Storage
{
    /// <summary>
    /// FIFO queue of not-yet-started match requests, persisted as a single JSON array file.
    /// DequeueNext() removes an entry the instant it's picked up (before the match starts), so a
    /// running match is never present in this list — DELETE /api/queue/{id} can therefore never
    /// target a running entry, with no separate "running" flag or cross-service locking needed.
    /// </summary>
    internal class QueueStore(PathConventions paths)
    {
        readonly object _lock = new();

        public IReadOnlyList<QueueEntry> ListPending()
        {
            lock (_lock)
                return LoadAllUnlocked();
        }

        public QueueEntry Enqueue(QueueEntry entry)
        {
            lock (_lock)
            {
                var list = LoadAllUnlocked();
                list.Add(entry);
                SaveAllUnlocked(list);
                return entry;
            }
        }

        public bool TryRemove(string id)
        {
            lock (_lock)
            {
                var list = LoadAllUnlocked();
                var removed = list.RemoveAll(e => e.Id == id) > 0;
                if (removed)
                    SaveAllUnlocked(list);
                return removed;
            }
        }

        /// <summary>Atomically pops the head of the queue. Should only ever be called by QueueRunner.</summary>
        public QueueEntry? DequeueNext()
        {
            lock (_lock)
            {
                var list = LoadAllUnlocked();
                if (list.Count == 0)
                    return null;

                var next = list[0];
                list.RemoveAt(0);
                SaveAllUnlocked(list);
                return next;
            }
        }

        List<QueueEntry> LoadAllUnlocked()
        {
            if (!File.Exists(paths.QueueFilePath))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<QueueEntry>>(File.ReadAllText(paths.QueueFilePath)) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        void SaveAllUnlocked(List<QueueEntry> list)
            => File.WriteAllText(paths.QueueFilePath, JsonSerializer.Serialize(list, JsonConventions.Options));
    }
}
