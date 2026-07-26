using System;
using System.Collections.Generic;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>
    /// The exact PlayerConfig-equivalent values an engine was launched with for one specific match —
    /// captured at run time so it stays accurate even if the engine's registered defaults change later.
    /// </summary>
    internal record EngineLaunchConfig(string Path, string Arguments, string WorkDir, List<string> InitialCommands);

    /// <summary>One completed (or currently running) queue entry's outcome metadata.</summary>
    internal record HistoryEntryRecord
    {
        public required string Id { get; init; }
        public required DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; init; }

        /// <summary>"Running" | "Completed" | "Cancelled" | "Failed".</summary>
        public required string Status { get; init; }

        public required string Engine0Id { get; init; }
        public required string Engine0Name { get; init; }
        public required string Engine1Id { get; init; }
        public required string Engine1Name { get; init; }
        public required string PresetId { get; init; }
        public required string PresetName { get; init; }
        public required int Matches { get; init; }
        public required int Sessions { get; init; }
        public string? ErrorMessage { get; init; }

        // Nullable so history entries written before this field existed still deserialize.
        public EngineLaunchConfig? Engine0Config { get; init; }
        public EngineLaunchConfig? Engine1Config { get; init; }
    }
}
