using System;
using System.Text.RegularExpressions;

using NBoardLocalGameServer.Reversi;

namespace DummyEngine
{
    // Minimal NBoard-protocol engine used only for aggregation-correctness tests: picks a uniformly
    // random legal move (or passes) instead of doing any real search, so many games finish almost
    // instantly and can be run at high concurrency without depending on a real (proprietary) engine
    // binary. Args: "<name> [<minDelayMs> <maxDelayMs> [<chaosPercent>]]" — name is self-reported via
    // "set myname" so tests can identify which physical player played Black/White in a given recorded
    // game (the process's own OS process name is otherwise identical - "dotnet" - for every instance);
    // the optional delay range widens the window where concurrently-finishing games race each other;
    // chaosPercent (0-100) makes this fraction of moves deliberately illegal, to exercise GameServer's
    // EngineException/aborted-game handling instead of always playing a clean game.
    internal class Program
    {
        static void Main(string[] args)
        {
            var name = args.Length >= 1 ? args[0] : "DummyEngine";
            var (minDelayMs, maxDelayMs) = ParseDelayArgs(args);
            var chaosPercent = args.Length >= 4 && int.TryParse(args[3], out var c) ? c : 0;
            var rand = Random.Shared;
            var pos = new Position();

            Console.WriteLine($"set myname {name}");

            string? line;
            while ((line = Console.ReadLine()) is not null)
            {
                line = line.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("ping ", StringComparison.OrdinalIgnoreCase))
                {
                    var id = line[5..].Trim();
                    Console.WriteLine($"pong {id}");
                }
                else if (line.StartsWith("set game ", StringComparison.OrdinalIgnoreCase))
                {
                    pos = ParseGgfPosition(line[9..]);
                }
                else if (line.StartsWith("move ", StringComparison.OrdinalIgnoreCase))
                {
                    var coord = ReversiTypes.ParseCoordinate(line.AsSpan(5));
                    pos.Update(coord);
                }
                else if (line.Equals("go", StringComparison.OrdinalIgnoreCase))
                {
                    if (maxDelayMs > 0)
                        System.Threading.Thread.Sleep(rand.Next(minDelayMs, maxDelayMs + 1));

                    var move = chaosPercent > 0 && rand.Next(100) < chaosPercent
                        ? BoardCoordinate.A1
                        : ChooseMove(pos, rand);
                    Console.WriteLine($"=== {move}");
                }
                else if (line.StartsWith("quit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                // "nboard N", "set depth N", etc.: no response expected, ignore.
            }
        }

        static (int min, int max) ParseDelayArgs(string[] args)
        {
            if (args.Length >= 3 && int.TryParse(args[1], out var min) && int.TryParse(args[2], out var max) && max >= min)
                return (min, max);
            return (0, 0);
        }

        static Position ParseGgfPosition(string ggf)
        {
            var match = Regex.Match(ggf, @"BO\[8 ([*O\-]{64}) ([*O])\]");
            var board = match.Groups[1].Value;
            var sideToMove = match.Groups[2].Value[0] == '*' ? DiscColor.Black : DiscColor.White;

            var pos = new Position(new Bitboard(0UL, 0UL), sideToMove);
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
            {
                var ch = board[(byte)coord];
                if (ch == '*')
                    pos.PutDiscAt(DiscColor.Black, coord);
                else if (ch == 'O')
                    pos.PutDiscAt(DiscColor.White, coord);
            }
            return pos;
        }

        static BoardCoordinate ChooseMove(Position pos, Random rand)
        {
            Span<BoardCoordinate> legal = stackalloc BoardCoordinate[64];
            var count = 0;
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
                if (pos.IsLegal(coord))
                    legal[count++] = coord;

            return count == 0 ? BoardCoordinate.PA : legal[rand.Next(count)];
        }
    }
}
