using System;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>
    /// A named, reusable bundle of GameServerConfig-equivalent settings plus a book selection and
    /// time control, selected when creating a new queued match.
    /// </summary>
    internal class MatchPresetRecord
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;

        public GameSessionMode SessionMode { get; set; } = GameSessionMode.StatefulEngine;
        public MatchMode MatchMode { get; set; } = MatchMode.Normal;
        public bool SwapPlayer { get; set; } = true;
        public bool UseSamePositionWhenSwapPlayer { get; set; } = true;
        public bool ShuffleBook { get; set; }
        public string? BookId { get; set; }

        /// <summary>Time control strings in GameClockConfig.TryParseTime's "ini/inc/extra" format. Empty/null = unset.</summary>
        public string? TimeControlCommon { get; set; }
        public string? TimeControlFirst { get; set; }
        public string? TimeControlSecond { get; set; }
    }
}
