using System;

namespace NBoardLocalGameServer
{
    /// <summary>
    /// PlayerStats/MatchStatsの統計的有意性判定で使う, 標準正規分布関連の計算.
    /// </summary>
    internal static class NormalDistribution
    {
        /// <summary>
        /// 標準正規分布の累積分布関数Φ(z). Abramowitz &amp; Stegun 7.1.26による誤差関数近似
        /// (最大誤差 約1.5×10^-7) を使用.
        /// </summary>
        public static double Cdf(double z) => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));

        static double Erf(double x)
        {
            var sign = x < 0 ? -1.0 : 1.0;
            x = Math.Abs(x);

            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p = 0.3275911;

            var t = 1.0 / (1.0 + p * x);
            var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }

        /// <summary>
        /// 両側検定における, z値から求めた信頼度(0〜1). 例えばz=1.96なら約0.95(95%)を返す.
        /// confidence = 2Φ(|z|) - 1.
        /// </summary>
        public static double TwoSidedConfidence(double z) => 2.0 * Cdf(Math.Abs(z)) - 1.0;
    }

    /// <summary>
    /// 決着数(勝ち数+負け数, 引き分けを除く)が少ないケース向けの厳密検定.
    /// NormalDistributionベースの正規近似は, 1局のスコアの分散を(1-引き分け率)/4と仮定して中心極限定理を
    /// 適用するが, 決着数が少ないとこの近似は成り立たず, 実際より高い信頼度/小さい信頼区間を示してしまう
    /// (目安: 期待決着数が最低でもMinDecisiveGamesForNormalApprox程度は必要).
    /// 例えば50局中3勝0敗47分けのようなケースでは, 正規近似はz=1.73(信頼度91.7%)を示すが,
    /// 決着した3局だけを対象にした厳密な二項検定(符号検定)では信頼度75%程度にしかならない.
    /// </summary>
    internal static class ExactBinomialTest
    {
        /// <summary>この局数未満の決着数では, 正規近似の代わりにこちらの厳密検定を使う.</summary>
        public const int MinDecisiveGamesForNormalApprox = 10;

        /// <summary>
        /// 引き分けを除いたn局中wins勝という結果に対する, 帰無仮説(実力互角, p=0.5)のもとでの
        /// 両側二項検定(符号検定)のp値.
        /// </summary>
        public static double TwoSidedPValue(int wins, int n)
        {
            if (n <= 0)
                return 1.0;

            var m = Math.Min(wins, n - wins);

            // P(X=i) = C(n,i) * 0.5^n. Builds up via the recurrence C(n,i) = C(n,i-1) * (n-i+1)/i,
            // starting from P(X=0) = 0.5^n, to avoid computing factorials/binomial coefficients
            // directly (n stays small here since this only runs below MinDecisiveGamesForNormalApprox).
            var pmf = Math.Pow(0.5, n);
            var cumulative = pmf;
            for (var i = 1; i <= m; i++)
            {
                pmf *= (double)(n - i + 1) / i;
                cumulative += pmf;
            }

            // Two-sided: double the probability of the more extreme tail (by symmetry under p=0.5,
            // the other tail has equal probability), capped at 1 for the m == n/2 (near-even) case.
            return Math.Min(1.0, cumulative * 2.0);
        }
    }
}
