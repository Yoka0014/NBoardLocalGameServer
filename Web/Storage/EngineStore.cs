using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

using NBoardLocalGameServer.Web.Models;

namespace NBoardLocalGameServer.Web.Storage
{
    /// <summary>
    /// Registered thinking engines: manifest (build settings + default launch config) plus the
    /// zip-extracted source tree that acts as the working directory during matches.
    /// </summary>
    internal class EngineStore
    {
        readonly PathConventions _paths;
        readonly JsonFileStore<EngineRecord> _store;

        public EngineStore(PathConventions paths)
        {
            _paths = paths;
            _store = new JsonFileStore<EngineRecord>(paths.EnginesDir, "manifest.json");
        }

        public IReadOnlyList<EngineRecord> ListAll()
            => [.. _store.LoadAll().Select(x => x.Record).OrderBy(e => e.CreatedAt)];

        public EngineRecord? Load(string id) => _store.Load(id);
        public void Save(EngineRecord record) => _store.Save(record.Id, record);
        public bool Delete(string id) => _store.Delete(id);

        public string GetExtractedRoot(string id) => _paths.EngineExtractedDir(id);
        public string GetBuildLogPath(string id) => _paths.EngineBuildLogPath(id);

        /// <summary>Updates just the build-status fields of an existing engine's manifest, leaving everything else untouched.</summary>
        public void UpdateBuildStatus(string id, string status, DateTime at)
        {
            var record = Load(id) ?? throw new KeyNotFoundException($"Engine \"{id}\" was not found.");
            record.LastBuildStatus = status;
            record.LastBuildAt = at;
            Save(record);
        }

        /// <summary>Extracts an uploaded zip stream into the engine's extracted/ directory, replacing any prior contents.</summary>
        public async Task ExtractZipAsync(string id, Stream zipStream)
        {
            var extractedRoot = GetExtractedRoot(id);
            if (Directory.Exists(extractedRoot))
                Directory.Delete(extractedRoot, recursive: true);
            Directory.CreateDirectory(extractedRoot);

            var tempZipPath = Path.Combine(Path.GetTempPath(), $"nboard-engine-upload-{Guid.NewGuid():N}.zip");
            try
            {
                await using (var fileStream = File.Create(tempZipPath))
                    await zipStream.CopyToAsync(fileStream);

                ZipFile.ExtractToDirectory(tempZipPath, extractedRoot);
            }
            finally
            {
                File.Delete(tempZipPath);
            }
        }

        public IReadOnlyList<EngineFileEntry> ListFiles(string id)
        {
            var root = GetExtractedRoot(id);
            if (!Directory.Exists(root))
                return [];

            var results = new List<EngineFileEntry>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(root, file).Replace('\\', '/');
                var info = new FileInfo(file);
                results.Add(new EngineFileEntry(relPath, info.Length, info.LastWriteTime));
            }
            return results;
        }

        /// <summary>
        /// Resolves a user-supplied relative path against the engine's extracted root, returning null
        /// if it would escape that root (path-traversal guard for the file-browse/replace endpoints).
        /// </summary>
        public string? ResolveFilePath(string id, string relativePath)
        {
            var root = Path.GetFullPath(GetExtractedRoot(id));
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            var normalizedRoot = root + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
                return null;

            return fullPath;
        }
    }
}
