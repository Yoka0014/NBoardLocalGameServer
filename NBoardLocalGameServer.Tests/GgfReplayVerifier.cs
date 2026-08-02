using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using NBoardLocalGameServer.Reversi;

namespace NBoardLocalGameServer.Tests
{
    // Recovers each recorded game's Black/White player and final result directly from record.ggf,
    // independently of PlayerStats' own incrementally-updated counters - the point is to have a
    // second, unrelated code path to cross-check aggregation against.
    internal record ParsedGgfGame(string BlackName, string WhiteName, Position RootPosition, List<Move> Moves);

    internal static class GgfReplayVerifier
    {
        public static List<ParsedGgfGame> ParseRecordFile(string path)
        {
            var games = new List<ParsedGgfGame>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length > 0)
                    games.Add(ParseLine(line));
            }
            return games;
        }

        static ParsedGgfGame ParseLine(string line)
        {
            var black = Regex.Match(line, @"PB\[([^\]]*)\]").Groups[1].Value;
            var white = Regex.Match(line, @"PW\[([^\]]*)\]").Groups[1].Value;

            var boMatch = Regex.Match(line, @"BO\[8 ([*O\-]{64}) ([*O])\]");
            var board = boMatch.Groups[1].Value;
            var sideToMove = boMatch.Groups[2].Value[0] == '*' ? DiscColor.Black : DiscColor.White;

            var pos = new Position(new Bitboard(0UL, 0UL), sideToMove);
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
            {
                var ch = board[(byte)coord];
                if (ch == '*')
                    pos.PutDiscAt(DiscColor.Black, coord);
                else if (ch == 'O')
                    pos.PutDiscAt(DiscColor.White, coord);
            }

            // The move list directly follows the BO[] field, before the closing ";)" - scoping the
            // regex to this substring avoids false matches like "PB[" / "PW[" earlier in the line.
            var moveListText = line[(boMatch.Index + boMatch.Length)..];
            var moves = new List<Move>();
            foreach (Match m in Regex.Matches(moveListText, @"([BW])\[([A-Za-z0-9]+)\]"))
            {
                var color = m.Groups[1].Value == "B" ? DiscColor.Black : DiscColor.White;
                var coord = ReversiTypes.ParseCoordinate(m.Groups[2].Value);
                moves.Add(new Move(color, coord));
            }

            return new ParsedGgfGame(black, white, pos, moves);
        }
    }
}
