using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

using NBoardLocalGameServer.Reversi;

namespace NBoardLocalGameServer.Tests
{
    // Regression tests for GameServer's win/loss/draw/score aggregation under concurrent sessions.
    // Runs real (multi-process) DummyEngine instances - only the engine is fake, GameServer/Player/
    // EnginePool/GameSession all run exactly as they do in production.
    public class ConcurrencyAggregationTests
    {
        [Fact]
        public async Task NormalMode_HighConcurrency_AggregationIsConsistent()
            => await RunAndVerify(MatchMode.Normal, matches: 80, sessions: 8);

        [Fact]
        public async Task SynchroMode_HighConcurrency_AggregationIsConsistent()
            => await RunAndVerify(MatchMode.Synchro, matches: 40, sessions: 8);

        [Fact]
        public async Task MirrorMatch_SamePlayerBothSides_DoesNotDeadlockOrMiscount()
            => await RunAndVerify(MatchMode.Normal, matches: 20, sessions: 4, mirror: true);

        // A chaotic opponent that sometimes plays an illegal move exercises GameServer.StartSession's
        // catch (EngineException) path: the aborted game must be excluded from PlayerStats/record.ggf
        // without corrupting other concurrently-running games or leaking engine-pool/session-slot
        // capacity (which would otherwise eventually stall the whole run).
        [Fact]
        public async Task EngineErrors_AreExcludedWithoutCorruptingOtherGamesOrLeakingResources()
        {
            const int sessions = 6;
            const int matches = 40;

            var recordPath = TestHelpers.NewTempFile("ggf");
            var statsPath = TestHelpers.NewTempFile("json");
            try
            {
                var config = TestHelpers.NewConfig(MatchMode.Normal);
                var p0Config = TestHelpers.DummyEngineConfig("P0", 0, 5, chaosPercent: 20);
                var p1Config = TestHelpers.DummyEngineConfig("P1", 0, 5);

                var server = new GameServer(config, p0Config, p1Config, recordPath, statsPath, sessions,
                    player0DisplayName: "P0", player1DisplayName: "P1");

                await server.RunAsync(matches);

                Assert.False(server.FailedToStart);
                Assert.False(server.WasCancelled);
                Assert.Equal(matches, server.CompletedGameCount); // every slot resolved, error or not

                var stats = server.CurrentPlayerStats;
                Assert.NotNull(stats);
                var p0 = stats![0];
                var p1 = stats[1];

                // Some games should have actually errored out given a 20% illegal-move rate over 40 games.
                Assert.True(p0.TotalGameCount < matches, "Expected at least one game to be aborted by the chaotic engine.");

                Assert.Equal(p0.TotalGameCount, p1.TotalGameCount);
                Assert.Equal(p0.TotalWinCount, p1.TotalLossCount);
                Assert.Equal(p1.TotalWinCount, p0.TotalLossCount);
                Assert.Equal(p0.TotalDrawCount, p1.TotalDrawCount);
                Assert.Equal(p0.TotalGainedScore, -p1.TotalGainedScore);

                // Aborted games must not appear in the game record at all.
                var recordedGames = File.ReadAllLines(recordPath).Count(l => l.Length > 0);
                Assert.Equal(p0.TotalGameCount, recordedGames);
            }
            finally
            {
                File.Delete(recordPath);
                File.Delete(statsPath);
            }
        }

        static async Task RunAndVerify(MatchMode mode, int matches, int sessions, bool mirror = false)
        {
            var recordPath = TestHelpers.NewTempFile("ggf");
            var statsPath = TestHelpers.NewTempFile("json");

            try
            {
                var config = TestHelpers.NewConfig(mode);
                var p0Config = TestHelpers.DummyEngineConfig("P0", 0, 5);
                // Even in the "mirror match" case (same engine registered as both players, the
                // production scenario being exercised via `mirror`) each launched process still needs
                // a distinct self-reported name so the GGF-based independent verification below can
                // tell players[0]/players[1] apart - this is purely a test-harness identification
                // concern, not something GameServer's own win/loss attribution (which locks on the
                // Player object reference, never on the name) depends on.
                var p1Config = TestHelpers.DummyEngineConfig("P1", 0, 5);

                var server = new GameServer(config, p0Config, p1Config, recordPath, statsPath, sessions,
                    player0DisplayName: "P0", player1DisplayName: "P1");

                await server.RunAsync(matches);

                Assert.False(server.FailedToStart);
                Assert.False(server.WasCancelled);

                var stats = server.CurrentPlayerStats;
                Assert.NotNull(stats);
                var p0 = stats![0];
                var p1 = stats[1];

                Assert.Equal(p0.TotalGameCount, p1.TotalGameCount);
                Assert.Equal(server.CompletedGameCount, p0.TotalGameCount);

                Assert.Equal(p0.TotalWinCount, p1.TotalLossCount);
                Assert.Equal(p1.TotalWinCount, p0.TotalLossCount);
                Assert.Equal(p0.TotalDrawCount, p1.TotalDrawCount);
                Assert.Equal(p0.TotalGainedScore, -p1.TotalGainedScore);

                if (mode == MatchMode.Synchro)
                {
                    var m = server.CurrentMatchStats;
                    Assert.NotNull(m);
                    Assert.Equal(matches, m!.MatchWinCount[0] + m.MatchWinCount[1] + m.MatchDrawCount + m.IncompleteMatchCount);
                }

                VerifyAgainstGgfRecord(recordPath, p0, p1);
            }
            finally
            {
                File.Delete(recordPath);
                File.Delete(statsPath);
            }
        }

        // Independently replays every recorded game from record.ggf (root position + move list only,
        // via the same Position/Bitboard logic GameServer itself uses, but a completely separate
        // computation from PlayerStats' incrementally-updated counters) and cross-checks the result.
        static void VerifyAgainstGgfRecord(string recordPath, PlayerStats p0, PlayerStats p1)
        {
            var games = GgfReplayVerifier.ParseRecordFile(recordPath);

            int[] win = [0, 0], loss = [0, 0], draw = [0, 0], gained = [0, 0];

            foreach (var game in games)
            {
                var pos = new Position(game.RootPosition);
                var replayOk = pos.UpdateAlongMoves(game.Moves.Select(m => m.Coord));
                Assert.True(replayOk, "Replaying the recorded move list failed - a move in record.ggf was illegal against the board it was recorded on.");
                Assert.True(pos.IsGameOver, "Recorded game did not reach a natural game-over state on replay.");

                var p0Color = game.BlackName == "P0" ? DiscColor.Black : DiscColor.White;
                var winner = pos.Winner;
                var p0Score = pos.GetScoreFrom(p0Color)!.Value;

                if (winner == DiscColor.Null)
                {
                    draw[0]++; draw[1]++;
                }
                else if (winner == p0Color)
                {
                    win[0]++; loss[1]++;
                }
                else
                {
                    win[1]++; loss[0]++;
                }

                gained[0] += p0Score;
                gained[1] -= p0Score;
            }

            Assert.Equal(win[0], p0.TotalWinCount);
            Assert.Equal(win[1], p1.TotalWinCount);
            Assert.Equal(loss[0], p0.TotalLossCount);
            Assert.Equal(loss[1], p1.TotalLossCount);
            Assert.Equal(draw[0], p0.TotalDrawCount);
            Assert.Equal(draw[1], p1.TotalDrawCount);
            Assert.Equal(gained[0], p0.TotalGainedScore);
            Assert.Equal(gained[1], p1.TotalGainedScore);
        }
    }
}
