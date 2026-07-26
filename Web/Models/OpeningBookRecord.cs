using System;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>
    /// A registered opening book file, stored as-is in the native OpeningBook line format.
    /// </summary>
    internal class OpeningBookRecord
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public DateTime UploadedAt { get; init; } = DateTime.Now;
        public int NumPositions { get; set; }
    }
}
