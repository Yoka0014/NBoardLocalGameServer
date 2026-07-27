using System;
using System.Collections.Generic;

namespace NBoardLocalGameServer.Web.Models
{
    /// <summary>Summary of the currently-running queue entry, for the dashboard's "running match" card.</summary>
    internal record RunningMatchInfo(
        string MatchId, string Engine0Name, string Engine1Name, string PresetName,
        int Matches, int Sessions, int CompletedGames, int TotalGames, DateTime StartedAt);

    internal record QueueStatusResponse(RunningMatchInfo? Running, IReadOnlyList<QueueEntry> Pending);

    /// <summary>One concurrently-active game/session's live board, for a spectate card.</summary>
    internal record SpectateSession(
        int SessionSlot, int GameId, string BlackName, string WhiteName,
        int MoveCount, string Board, int BlackDiscs, int WhiteDiscs, string SideToMove, bool IsGameOver);

    /// <summary>
    /// Live win/loss tallies while a match is running — same shape as the final stats.json
    /// (PlayerStats[] for Normal, {PlayerStats, MatchStats} for Synchro) so the frontend can reuse its
    /// existing stats-rendering logic, just updated continuously instead of only after the match ends.
    /// </summary>
    internal record SpectateStatus(bool Running, string? MatchId, int TotalGames, int CompletedGames, IReadOnlyList<SpectateSession> Sessions, object? LiveStats = null);
}
