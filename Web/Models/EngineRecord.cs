using System;
using System.Collections.Generic;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>
    /// A registered thinking engine: build settings + default launch config (PlayerConfig equivalent),
    /// all paths relative to the extracted zip's root (see EngineStore.ExtractedRoot).
    /// </summary>
    internal class EngineRecord
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;

        public string BuildCommand { get; set; } = string.Empty;
        public string BuildWorkDir { get; set; } = ".";

        public string DefaultPath { get; set; } = string.Empty;
        public string DefaultArguments { get; set; } = string.Empty;
        public string DefaultWorkDir { get; set; } = ".";
        public List<string> DefaultInitialCommands { get; set; } = [];

        public string LastBuildStatus { get; set; } = "NotBuilt";
        public DateTime? LastBuildAt { get; set; }
    }
}
