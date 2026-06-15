using System;
using System.Collections.Generic;
using System.Text;
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
    }
}
