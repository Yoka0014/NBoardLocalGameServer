using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBoardLocalGameServer
{
    /// <summary>
    /// プレイヤ統計情報.
    /// Json形式で保存して後で項目別に閲覧することを目的としている．
    /// </summary>
    /// <param name="label"></param>
    internal class PlayerStats(string label)
    {
        static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        [JsonPropertyOrder(0)]
        public string Label { get; } = label;

        [JsonPropertyOrder(1)]
        public double TotalWinRate => TotalGameCount == 0 ? 0.0 : (TotalWinCount + TotalDrawCount * 0.5) / TotalGameCount;

        [JsonPropertyOrder(2)]
        public double TotalPureWinRate
        {
            get
            {
                int decidedGames = TotalGameCount - TotalDrawCount;
                return decidedGames == 0 ? 0.0 : (double)TotalWinCount / decidedGames;
            }
        }

        [JsonPropertyOrder(3)]
        public double TotalDrawRate => TotalGameCount == 0 ? 0.0 : (double)TotalDrawCount / TotalGameCount;

        [JsonPropertyOrder(4)]
        public int TotalGameCount => TotalWinCount + TotalLossCount + TotalDrawCount;

        [JsonPropertyOrder(5)]
        public int TotalWinCount => WinCount.Sum();

        [JsonPropertyOrder(6)]
        public int TotalLossCount => LossCount.Sum();

        [JsonPropertyOrder(7)]
        public int TotalDrawCount => DrawCount.Sum();

        [JsonPropertyOrder(8)]
        public IReadOnlyList<double> WinRate => [
            GameCount[0] == 0 ? 0.0 : (WinCount[0] + DrawCount[0] * 0.5) / GameCount[0],
            GameCount[1] == 0 ? 0.0 : (WinCount[1] + DrawCount[1] * 0.5) / GameCount[1]
        ];

        [JsonPropertyOrder(9)]
        public IReadOnlyList<double> PureWinRate => [
            (GameCount[0] - DrawCount[0]) == 0 ? 0.0 : (double)WinCount[0] / (GameCount[0] - DrawCount[0]),
            (GameCount[1] - DrawCount[1]) == 0 ? 0.0 : (double)WinCount[1] / (GameCount[1] - DrawCount[1])
        ];

        [JsonPropertyOrder(10)]
        public IReadOnlyList<double> DrawRate => [
            GameCount[0] == 0 ? 0.0 : (double)DrawCount[0] / GameCount[0],
            GameCount[1] == 0 ? 0.0 : (double)DrawCount[1] / GameCount[1]
        ];

        [JsonPropertyOrder(11)]
        public IReadOnlyList<int> GameCount => [
            WinCount[0] + LossCount[0] + DrawCount[0],
            WinCount[1] + LossCount[1] + DrawCount[1]
        ];

        [JsonPropertyOrder(12)]
        public int[] WinCount { get; init; } = [0, 0];

        [JsonPropertyOrder(13)]
        public int[] LossCount { get; init; } = [0, 0];

        [JsonPropertyOrder(14)]
        public int[] DrawCount { get; init; } = [0, 0];

        [JsonPropertyOrder(15)]
        public double AverageTotalGainedScore => TotalGameCount == 0 ? 0.0 : (double)TotalGainedScore / TotalGameCount;

        [JsonPropertyOrder(16)]
        public int TotalGainedScore => GainedScore.Sum();

        [JsonPropertyOrder(17)]
        public IReadOnlyList<double> AverageGainedScore => [
            GameCount[0] == 0 ? 0.0 : (double)GainedScore[0] / GameCount[0],
            GameCount[1] == 0 ? 0.0 : (double)GainedScore[1] / GameCount[1],
        ];

        [JsonPropertyOrder(18)]
        public int[] GainedScore { get; init; } = [0, 0];

        /// <summary>
        /// TotalWinRate（引き分けを0.5勝として計算した得点率）から推定したEloレーティング差．
        /// 得点率が0%または100%（無敗/全敗）の場合は理論上無限大になるため, 意味のある値として
        /// 計算できないことを示すnullを返す．
        /// </summary>
        [JsonPropertyOrder(19)]
        public double? EloDiff
        {
            get
            {
                if (TotalGameCount == 0)
                    return null;

                var s = TotalWinRate;
                if (s <= 0.0 || s >= 1.0)
                    return null;

                return 400.0 * System.Math.Log10(s / (1.0 - s));
            }
        }

        /// <summary>
        /// 実力互角(帰無仮説)を前提とした標準正規分布のz値．
        /// 1局のスコア(勝ち1, 引き分け0.5, 負け0)の分散はVar=(1-引き分け率)/4なので,
        /// N局の標準誤差はSE=√(Var/N). TotalWinRateの0.5からの乖離をこのSEで割った値がz．
        /// |z|が1.96以上で両側5%水準, 2.576以上で1%水準, 3.291以上で0.1%水準の統計的有意性を示す．
        /// 対局数が0, または引き分け率100%(分散0で乖離があっても定義不能)の場合はnull．
        /// </summary>
        [JsonPropertyOrder(20)]
        public double? SignificanceZ
        {
            get
            {
                if (TotalGameCount == 0)
                    return null;

                var variance = (1.0 - TotalDrawRate) / 4.0;
                if (variance <= 0.0)
                    return null;

                var se = System.Math.Sqrt(variance / TotalGameCount);
                return (TotalWinRate - 0.5) / se;
            }
        }

        /// <summary>
        /// SignificanceZに対応する, 両側検定での信頼度(0〜1). 例えばz=1.28ならおよそ0.80(80%)．
        /// SignificanceZがnullの場合はnull．
        /// </summary>
        [JsonPropertyOrder(21)]
        public double? ConfidenceLevel => SignificanceZ is { } z ? NormalDistribution.TwoSidedConfidence(z) : null;

        /// <summary>
        /// 現在の得点率・引き分け率をそのまま維持したと仮定した場合に, 両側5%水準で統計的有意と
        /// 言えるようになるまでに必要な総対局数の目安(切り上げ)．既に有意な場合や, 得点率がちょうど
        /// 50%(差の検出に無限大の対局数を要する)の場合はnull．
        /// </summary>
        [JsonPropertyOrder(22)]
        public int? GamesNeededFor95PctSignificance
        {
            get
            {
                if (TotalGameCount == 0)
                    return null;

                var variance = (1.0 - TotalDrawRate) / 4.0;
                if (variance <= 0.0)
                    return null;

                var deviation = System.Math.Abs(TotalWinRate - 0.5);
                if (deviation <= 0.0)
                    return null;

                var n = variance * System.Math.Pow(1.96 / deviation, 2);
                return (int)System.Math.Ceiling(n);
            }
        }
    }
}
