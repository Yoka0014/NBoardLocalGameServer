using System.Linq;

namespace NBoardLocalGameServer
{
    /// <summary>
    /// Synchro Matchにおける, マッチ単位(同一局面・手番反転の2局1組)の統計情報.
    /// 2局それぞれの対局結果はPlayerStatsとして今まで通り個別に集計され, これはその上に
    /// 追加でマッチ単位の勝敗(2局合計の石差)を集計するものである.
    /// </summary>
    internal class MatchStats(string label0, string label1)
    {
        public string Label0 { get; } = label0;
        public string Label1 { get; } = label1;

        // index: 0=players[0]がマッチ勝利, 1=players[1]がマッチ勝利
        public int[] MatchWinCount { get; init; } = [0, 0];

        public int MatchDrawCount { get; set; }

        /// <summary>
        /// 片方の対局が中断/エラーで完了しなかったため, マッチとして採点できなかった数.
        /// </summary>
        public int IncompleteMatchCount { get; set; }

        public int TotalMatchCount => MatchWinCount.Sum() + MatchDrawCount;

        public double Player0MatchWinRate => TotalMatchCount == 0 ? 0.0 : (double)MatchWinCount[0] / TotalMatchCount;
        public double Player1MatchWinRate => TotalMatchCount == 0 ? 0.0 : (double)MatchWinCount[1] / TotalMatchCount;
        public double MatchDrawRate => TotalMatchCount == 0 ? 0.0 : (double)MatchDrawCount / TotalMatchCount;

        // 各マッチにおける(players[0]の合計得点 - players[1]の合計得点)の累計.
        public long TotalNetScoreForPlayer0 { get; set; }
        public double AverageNetScoreForPlayer0 => TotalMatchCount == 0 ? 0.0 : (double)TotalNetScoreForPlayer0 / TotalMatchCount;

        /// <summary>
        /// Player0MatchWinRateから推定したEloレーティング差（players[0]から見た値）．
        /// PlayerStats.EloDiffと同様, 得点率が0%または100%の場合はnullを返す．
        /// </summary>
        public double? EloDiffForPlayer0
        {
            get
            {
                if (TotalMatchCount == 0)
                    return null;

                var s = Player0MatchWinRate;
                if (s <= 0.0 || s >= 1.0)
                    return null;

                return 400.0 * System.Math.Log10(s / (1.0 - s));
            }
        }
    }

    /// <summary>
    /// Synchroモードでの--stats出力の形. Normalモードは従来通りPlayerStats[]を裸配列で出力する.
    /// </summary>
    internal record SynchroStatsOutput(PlayerStats[] PlayerStats, MatchStats MatchStats);
}
