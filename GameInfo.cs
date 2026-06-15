using NBoardLocalGameServer.Reversi;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace NBoardLocalGameServer
{
    internal enum GameEndStatus
    {
        Normal,
        Resigned,   // 投了した
        Timeout,    // 時間切れ負け
        MutualScore // 互いの合意で勝敗を決めた（チェスではよくあるけどリバーシではあまり使わない． GGSの仕様にあるので一応用意）
    }

    internal record GameResult(DiscColor Winner, int ScoreFromWinner, GameEndStatus EndStatus = GameEndStatus.Normal);

    internal class  GameInfo(string blackPlayerName, string whitePlayerName, Position rootPos)
    {
        public string BlackPlayerName { get; } = blackPlayerName;
        public string WhitePlayerName { get; } = whitePlayerName;
        public GameClockConfig? BlackGameClock { get; set; }
        public GameClockConfig? WhiteGameClock { get; set; }
        public Position RootPosition { get; } = new(rootPos);
        public List<Move> Moves { get; } = [];
        public DateTime DateTime { get; set; } = DateTime.Now;
        public GameResult? Result { get; set; }

        public GameInfo(GameInfo other) : this(other.BlackPlayerName, other.WhitePlayerName, other.RootPosition)
        {
            BlackGameClock = other.BlackGameClock;
            WhiteGameClock = other.WhiteGameClock;
            DateTime = other.DateTime;
            Result = other.Result;
            Moves.AddRange(other.Moves);
        }

        public string ToGGFString()
        {
            var sb = new StringBuilder("(;GM[Othello]PC[");
            sb.Append(Assembly.GetExecutingAssembly().GetName().Name).Append("]DT[");
            sb.Append(DateTime.ToString()).Append("]PB[");
            sb.Append(BlackPlayerName).Append("]PW[");
            sb.Append(WhitePlayerName).Append("]RE[");
            sb.Append(GameResultToString(Result)).Append("]BT[");
            sb.Append(GameClockToString(BlackGameClock)).Append("]WT[");
            sb.Append(GameClockToString(WhiteGameClock)).Append("]TY[");
            sb.Append(Constants.BoardSize).Append("]BO[").Append(Constants.BoardSize).Append(' ');

            const string Discs = "*O-";
            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
                sb.Append(Discs[(int)RootPosition.GetSquareColorAt(coord)]);
            sb.Append(' ').Append(Discs[(int)RootPosition.SideToMove]).Append(']');

            const string Colors = "BW?";
            foreach (var move in Moves)
                sb.Append(Colors[(int)move.Color]).Append('[').Append(move.Coord).Append(']');

            return sb.Append(";)").ToString();
        }

        static string? GameClockToString(GameClockConfig? clock) => (clock is null) ? string.Empty : clock.ToString();

        static string GameResultToString(GameResult? result)
        {
            if (result is null)
                return "?";

            var score = result.ScoreFromWinner;
            if (result.Winner == DiscColor.White)
                score *= -1;

            var flag = result.EndStatus switch
            {
                GameEndStatus.Resigned => ":r",
                GameEndStatus.Timeout => ":t",
                GameEndStatus.MutualScore => ":s",
                _ => string.Empty,
            };

            string sign;
            if (score == 0)
                sign = string.Empty;
            else
                sign = (score > 0) ? "+" : "-";

            return $"{sign}{score}{flag}";
        }
    }
}
