using System;

using Xunit;

namespace NBoardLocalGameServer.Tests
{
    // Hand-verified reference values for PlayerStats'/Statistics.cs's Elo-diff/significance/confidence
    // formulas, independent of concurrency - a previous bug here caused significance/CI to be
    // overestimated when the decisive-game count was small (see MinDecisiveGamesForNormalApprox).
    public class StatisticsFormulaTests
    {
        [Fact]
        public void EloDiff_MatchesHandComputedFormula()
        {
            // 3 wins, 1 loss, 0 draws -> s = 0.75.
            var stats = new PlayerStats("test") { WinCount = [3, 0], LossCount = [1, 0], DrawCount = [0, 0] };

            var expected = 400.0 * Math.Log10(0.75 / 0.25);
            Assert.NotNull(stats.EloDiff);
            Assert.Equal(expected, stats.EloDiff!.Value, precision: 9);
        }

        [Fact]
        public void EloDiff_IsNullAtExtremeWinRates()
        {
            var allWins = new PlayerStats("test") { WinCount = [5, 0], LossCount = [0, 0], DrawCount = [0, 0] };
            var allLosses = new PlayerStats("test") { WinCount = [0, 0], LossCount = [5, 0], DrawCount = [0, 0] };
            var noGames = new PlayerStats("test");

            Assert.Null(allWins.EloDiff);
            Assert.Null(allLosses.EloDiff);
            Assert.Null(noGames.EloDiff);
        }

        [Fact]
        public void EloDiff_IsZeroWhenAllDraws()
        {
            var stats = new PlayerStats("test") { WinCount = [0, 0], LossCount = [0, 0], DrawCount = [10, 0] };
            Assert.NotNull(stats.EloDiff);
            Assert.Equal(0.0, stats.EloDiff!.Value, precision: 9);
        }

        [Fact]
        public void GamesNeededFor95PctSignificance_IsNullWhenWinRateIsExactly50Percent()
        {
            var stats = new PlayerStats("test") { WinCount = [5, 0], LossCount = [5, 0], DrawCount = [0, 0] };
            Assert.Null(stats.GamesNeededFor95PctSignificance);
        }

        [Fact]
        public void GamesNeededFor95PctSignificance_MatchesHandComputedFormula()
        {
            // 3 wins, 1 loss -> s = 0.75, deviation = 0.25, variance = (1 - 0)/4 = 0.25.
            var stats = new PlayerStats("test") { WinCount = [3, 0], LossCount = [1, 0], DrawCount = [0, 0] };

            var expected = (int)Math.Ceiling(0.25 * Math.Pow(1.96 / 0.25, 2));
            Assert.Equal(expected, stats.GamesNeededFor95PctSignificance);
        }

        // Regression test for the fixed bug: with few decisive games, the normal approximation
        // (SignificanceZ/ConfidenceLevel's z-based path) overestimates significance. Below
        // ExactBinomialTest.MinDecisiveGamesForNormalApprox, ConfidenceLevel must fall back to the
        // exact two-sided binomial (sign) test instead - this is the exact "50 games, 3-0-47" example
        // documented in Statistics.cs's own ExactBinomialTest doc comment (expected ~75%, not ~91.7%).
        [Fact]
        public void ConfidenceLevel_FallsBackToExactBinomialTestBelowDecisiveGameThreshold()
        {
            var stats = new PlayerStats("test") { WinCount = [3, 0], LossCount = [0, 0], DrawCount = [47, 0] };

            Assert.Equal(50, stats.TotalGameCount);
            Assert.Equal(3, stats.TotalWinCount + stats.TotalLossCount); // decisive games = 3 < 10

            Assert.Null(stats.SignificanceZ);
            Assert.Null(stats.EloDiffMargin95);

            // Exact two-sided binomial (sign) test p-value for 3 wins out of 3 decisive games under
            // p=0.5: only the single most extreme outcome (all 3 the same way) on each tail.
            // P(X=0 or X=3 | Binomial(3, 0.5)) = 2 * 0.5^3 = 0.25 -> confidence = 1 - 0.25 = 0.75.
            Assert.Equal(0.75, stats.ConfidenceLevel!.Value, precision: 9);
        }

        [Fact]
        public void SignificanceZ_BecomesAvailableExactlyAtTheDecisiveGameThreshold()
        {
            const int threshold = 10; // ExactBinomialTest.MinDecisiveGamesForNormalApprox

            var below = new PlayerStats("test") { WinCount = [threshold / 2, 0], LossCount = [threshold / 2 - 1, 0], DrawCount = [0, 0] };
            var atThreshold = new PlayerStats("test") { WinCount = [threshold / 2, 0], LossCount = [threshold / 2, 0], DrawCount = [0, 0] };

            Assert.Equal(threshold - 1, below.TotalWinCount + below.TotalLossCount);
            Assert.Null(below.SignificanceZ);

            Assert.Equal(threshold, atThreshold.TotalWinCount + atThreshold.TotalLossCount);
            Assert.NotNull(atThreshold.SignificanceZ);
        }

        [Fact]
        public void SignificanceZ_MatchesHandComputedFormula()
        {
            // 12 wins, 3 losses, 3 draws -> s = (12 + 1.5)/18 = 0.75, decisive = 15 (>= threshold).
            var stats = new PlayerStats("test") { WinCount = [12, 0], LossCount = [3, 0], DrawCount = [3, 0] };

            var s = 0.75;
            var drawRate = 3.0 / 18.0;
            var variance = (1.0 - drawRate) / 4.0;
            var se = Math.Sqrt(variance / 18.0);
            var expectedZ = (s - 0.5) / se;

            Assert.NotNull(stats.SignificanceZ);
            Assert.Equal(expectedZ, stats.SignificanceZ!.Value, precision: 9);

            var expectedConfidence = 2.0 * NormalCdf(Math.Abs(expectedZ)) - 1.0;
            Assert.Equal(expectedConfidence, stats.ConfidenceLevel!.Value, precision: 6);
        }

        // Independent re-implementation of the standard normal CDF (Abramowitz & Stegun 7.1.26 is what
        // NormalDistribution.cs itself uses; a plain series/continued-fraction-free reference here would
        // be circular, so this cross-checks against .NET's own high-precision erf via a different route:
        // the complementary error function identity Phi(x) = 0.5*erfc(-x/sqrt(2)).
        static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2.0));

        // Numerical Recipes-style erfc approximation (Chebyshev fit), independent of the
        // Abramowitz & Stegun approximation used in NormalDistribution.Erf.
        static double Erfc(double x)
        {
            var t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
            var tau = t * Math.Exp(-x * x - 1.26551223 +
                t * (1.00002368 +
                t * (0.37409196 +
                t * (0.09678418 +
                t * (-0.18628806 +
                t * (0.27886807 +
                t * (-1.13520398 +
                t * (1.48851587 +
                t * (-0.82215223 +
                t * 0.17087277)))))))));
            return x >= 0 ? tau : 2.0 - tau;
        }
    }
}
