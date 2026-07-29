using System;
using System.Collections.Generic;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>
    /// One queued (or currently running) match request. Only Arguments/InitialCommands may be
    /// overridden per match — Path/WorkDir always come from the engine's registered defaults.
    /// </summary>
    internal record QueueEntry
    {
        public required string Id { get; init; }
        public DateTime EnqueuedAt { get; init; } = DateTime.Now;

        public required string Engine0Id { get; init; }
        public string? Engine0ArgumentsOverride { get; init; }
        public List<string>? Engine0InitialCommandsOverride { get; init; }

        public required string Engine1Id { get; init; }
        public string? Engine1ArgumentsOverride { get; init; }
        public List<string>? Engine1InitialCommandsOverride { get; init; }

        public required string PresetId { get; init; }
        public required int Matches { get; init; }
        public required int Sessions { get; init; }

        /// <summary>Free-text note recorded by whoever queued the match (e.g. why it was run).</summary>
        public string? Note { get; init; }
    }
}
