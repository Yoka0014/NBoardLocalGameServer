using System;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>One file inside a registered engine's extracted directory (used to render the file-tree UI).</summary>
    internal record EngineFileEntry(string Path, long Size, DateTime Modified);
}
