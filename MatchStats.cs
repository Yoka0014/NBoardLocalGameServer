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

        /// <summary>
        /// 引き分けを0.5勝として加点したスコア（players[0]から見た値）．
        /// PlayerStats.TotalWinRateと同じ定義で, Elo換算/統計的有意性の判定はこちらを使う必要がある——
        /// Player0MatchWinRateは引き分けに一切加点しないため, 引き分け率が高いと（互角の相手同士でも）
        /// 0.5から大きく外れた値になり, そのままEloの式に入れると実際には存在しない大差として現れてしまう.
        /// </summary>
        public double Player0MatchScore => TotalMatchCount == 0 ? 0.0 : (MatchWinCount[0] + MatchDrawCount * 0.5) / TotalMatchCount;

        // 各マッチにおける(players[0]の合計得点 - players[1]の合計得点)の累計.
        public long TotalNetScoreForPlayer0 { get; set; }
        public double AverageNetScoreForPlayer0 => TotalMatchCount == 0 ? 0.0 : (double)TotalNetScoreForPlayer0 / TotalMatchCount;

        /// <summary>
        /// Player0MatchScore（引き分けを0.5勝として計算したスコア）から推定したEloレーティング差
        /// （players[0]から見た値）．PlayerStats.EloDiffと同様, 得点率が0%または100%の場合はnullを返す．
        /// </summary>
        public double? EloDiffForPlayer0
        {
            get
            {
                if (TotalMatchCount == 0)
                    return null;

                var s = Player0MatchScore;
                if (s <= 0.0 || s >= 1.0)
                    return null;

                return 400.0 * System.Math.Log10(s / (1.0 - s));
            }
        }

        /// <summary>
        /// PlayerStats.EloDiffMargin95と同じ考え方を, マッチ単位のPlayer0MatchScoreに適用した,
        /// EloDiffForPlayer0の両側95%信頼区間の半幅（±分）．EloDiffForPlayer0がnullになる条件
        /// （マッチ数0, マッチ得点率が0%または100%）ではこちらもnull．
        /// PlayerStats.EloDiffMargin95と同様, 決着マッチ数(MatchWinCountの合計)が
        /// ExactBinomialTest.MinDecisiveGamesForNormalApprox未満のときはnullを返す．
        /// </summary>
        public double? EloDiffMargin95ForPlayer0
        {
            get
            {
                if (TotalMatchCount == 0)
                    return null;

                var s = Player0MatchScore;
                if (s <= 0.0 || s >= 1.0)
                    return null;

                if (MatchWinCount.Sum() < ExactBinomialTest.MinDecisiveGamesForNormalApprox)
                    return null;

                var variance = (1.0 - MatchDrawRate) / 4.0;
                var se = System.Math.Sqrt(variance / TotalMatchCount);
                var derivative = 400.0 / (System.Math.Log(10.0) * s * (1.0 - s));
                return 1.96 * se * derivative;
            }
        }

        /// <summary>
        /// PlayerStats.SignificanceZと同じ考え方を, マッチ単位(1マッチ=勝ち1/引き分け0.5/負け0)に
        /// 適用したz値．players[0]から見た値．TotalMatchCountが0, またはMatchDrawRateが100%の場合はnull．
        /// 決着マッチ数(MatchWinCountの合計)がExactBinomialTest.MinDecisiveGamesForNormalApprox未満の
        /// 場合も, 正規近似が信頼できないためnull(ConfidenceLevelForPlayer0が代わりに厳密な値を返す)．
        /// </summary>
        public double? SignificanceZForPlayer0
        {
            get
            {
                if (TotalMatchCount == 0)
                    return null;

                if (MatchWinCount.Sum() < ExactBinomialTest.MinDecisiveGamesForNormalApprox)
                    return null;

                var variance = (1.0 - MatchDrawRate) / 4.0;
                if (variance <= 0.0)
                    return null;

                var se = System.Math.Sqrt(variance / TotalMatchCount);
                return (Player0MatchScore - 0.5) / se;
            }
        }

        /// <summary>
        /// PlayerStats.ConfidenceLevelのマッチ単位版．決着マッチ数が少ない場合は
        /// ExactBinomialTestによる厳密な二項検定のp値から求める．
        /// </summary>
        public double? ConfidenceLevelForPlayer0
        {
            get
            {
                if (TotalMatchCount == 0)
                    return null;

                if (SignificanceZForPlayer0 is { } z)
                    return NormalDistribution.TwoSidedConfidence(z);

                var decisive = MatchWinCount.Sum();
                if (decisive == 0)
                    return null;

                return 1.0 - ExactBinomialTest.TwoSidedPValue(MatchWinCount[0], decisive);
            }
        }

        /// <summary>
        /// PlayerStats.GamesNeededFor95PctSignificanceのマッチ単位版．
        /// </summary>
        public int? MatchesNeededFor95PctSignificance
        {
            get
            {
                if (TotalMatchCount == 0)
                    return null;

                var variance = (1.0 - MatchDrawRate) / 4.0;
                if (variance <= 0.0)
                    return null;

                var deviation = System.Math.Abs(Player0MatchScore - 0.5);
                if (deviation <= 0.0)
                    return null;

                var n = variance * System.Math.Pow(1.96 / deviation, 2);
                return (int)System.Math.Ceiling(n);
            }
        }
    }

    /// <summary>
    /// Synchroモードでの--stats出力の形. Normalモードは従来通りPlayerStats[]を裸配列で出力する.
    /// </summary>
    internal record SynchroStatsOutput(PlayerStats[] PlayerStats, MatchStats MatchStats);
}
