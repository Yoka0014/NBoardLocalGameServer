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

        /// <summary>
        /// Packs the engine's extracted/ directory into a zip and returns it positioned at the start,
        /// ready to be handed to an HTTP response. Entry paths are relative to the extraction root with
        /// no wrapper directory, so the result can be fed straight back into <see cref="ExtractZipAsync"/>.
        /// </summary>
        /// <remarks>
        /// Staged in a temp file rather than streamed into the response: that keeps the archive seekable so
        /// the response can carry a Content-Length (a built engine plus its eval tables is tens of MB, and a
        /// download over an SSH tunnel wants a progress bar), and it keeps zip writing off the response
        /// stream, which rejects synchronous writes. The stream is opened DeleteOnClose, so the temp file
        /// disappears when the response disposes it -- including when the client disconnects mid-download.
        /// </remarks>
        public async Task<Stream> OpenZipAsync(string id)
        {
            var root = GetExtractedRoot(id);
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"nboard-engine-download-{Guid.NewGuid():N}.zip");
            var zipStream = new FileStream(tempZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.DeleteOnClose);
            try
            {
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    if (Directory.Exists(root))
                    {
                        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                            await AddZipEntryAsync(archive, root, file);
                    }
                }
                zipStream.Position = 0;
                return zipStream;
            }
            catch
            {
                await zipStream.DisposeAsync();
                throw;
            }
        }

        static async Task AddZipEntryAsync(ZipArchive archive, string root, string file)
        {
            FileStream source;
            try
            {
                // Share write and delete: a match may be running out of this very directory, and the
                // engine's own log or data files can be open for writing while we read them.
                source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException)
            {
                return;     // a file we cannot read is not a reason to lose the rest of the engine
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            await using (source)
            {
                // Fastest, not Optimal: this runs on the match server, where CPU is what the engines want.
                var entry = archive.CreateEntry(Path.GetRelativePath(root, file).Replace('\\', '/'),
                    CompressionLevel.Fastest);
                var modified = File.GetLastWriteTime(file);
                if (modified.Year >= 1980)      // the zip timestamp field cannot represent anything earlier
                    entry.LastWriteTime = modified;

                await using var entryStream = entry.Open();
                await source.CopyToAsync(entryStream);
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
