using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using NBoardLocalGameServer.Web.Models;

namespace NBoardLocalGameServer.Web.Storage
{
    /// <summary>Registered opening books, stored as-is in the native OpeningBook line format.</summary>
    internal class BookStore
    {
        readonly PathConventions _paths;
        readonly JsonFileStore<OpeningBookRecord> _store;

        public BookStore(PathConventions paths)
        {
            _paths = paths;
            _store = new JsonFileStore<OpeningBookRecord>(paths.BooksDir, "manifest.json");
        }

        public IReadOnlyList<OpeningBookRecord> ListAll()
            => [.. _store.LoadAll().Select(x => x.Record).OrderBy(b => b.UploadedAt)];

        public OpeningBookRecord? Load(string id) => _store.Load(id);
        public bool Delete(string id) => _store.Delete(id);
        public string GetFilePath(string id) => _paths.BookFilePath(id);

        /// <summary>
        /// Stores the uploaded book file and validates it by loading it through the real OpeningBook
        /// parser. Callers should let a thrown exception surface as a 400 response — the file is not
        /// registered (manifest not written) if validation fails.
        /// </summary>
        public async Task<OpeningBookRecord> SaveAsync(string id, string name, Stream fileStream)
        {
            var filePath = _paths.BookFilePath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await using (var fs = File.Create(filePath))
                await fileStream.CopyToAsync(fs);

            var book = new OpeningBook(filePath);
            var record = new OpeningBookRecord { Id = id, Name = name, NumPositions = book.NumPositions };
            _store.Save(id, record);
            return record;
        }
    }
}
