using System.IO;

namespace NBoardLocalGameServer.Web
{
    /// <summary>
    /// Resolves the on-disk layout under --data-dir for engines/books/presets/queue/history/settings.
    /// See the "New Web/ folder" section of the HTTP server mode plan for the directory layout.
    /// </summary>
    internal class PathConventions
    {
        public string Root { get; }
        public string EnginesDir { get; }
        public string BooksDir { get; }
        public string PresetsDir { get; }
        public string QueueDir { get; }
        public string HistoryDir { get; }
        public string HistoryExportDir { get; }
        public string SettingsFilePath { get; }

        public PathConventions(string dataDir)
        {
            Root = Path.GetFullPath(dataDir);
            EnginesDir = Path.Combine(Root, "engines");
            BooksDir = Path.Combine(Root, "books");
            PresetsDir = Path.Combine(Root, "presets");
            QueueDir = Path.Combine(Root, "queue");
            HistoryDir = Path.Combine(Root, "history");
            HistoryExportDir = Path.Combine(Root, "history-export");
            SettingsFilePath = Path.Combine(Root, "settings.json");

            foreach (var dir in new[] { Root, EnginesDir, BooksDir, PresetsDir, QueueDir, HistoryDir, HistoryExportDir })
                Directory.CreateDirectory(dir);
        }

        public string EngineDir(string id) => Path.Combine(EnginesDir, id);
        public string EngineManifestPath(string id) => Path.Combine(EngineDir(id), "manifest.json");
        public string EngineExtractedDir(string id) => Path.Combine(EngineDir(id), "extracted");
        public string EngineBuildLogPath(string id) => Path.Combine(EngineDir(id), "build.log");

        public string BookDir(string id) => Path.Combine(BooksDir, id);
        public string BookManifestPath(string id) => Path.Combine(BookDir(id), "manifest.json");
        public string BookFilePath(string id) => Path.Combine(BookDir(id), "book.txt");

        public string PresetPath(string id) => Path.Combine(PresetsDir, $"{id}.json");

        public string QueueFilePath => Path.Combine(QueueDir, "queue.json");

        public string HistoryEntryDir(string id) => Path.Combine(HistoryDir, id);
        public string HistoryMetaPath(string id) => Path.Combine(HistoryEntryDir(id), "meta.json");
        public string HistoryStatsPath(string id) => Path.Combine(HistoryEntryDir(id), "stats.json");
        public string HistoryRecordPath(string id) => Path.Combine(HistoryEntryDir(id), "record.ggf");
    }
}
