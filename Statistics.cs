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
}
