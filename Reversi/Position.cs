using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace NBoardLocalGameServer.Reversi
{
    using static Constants;

    /// <summary>
    /// リバーシの盤面.
    /// </summary>
    internal class Position
    {
        public DiscColor SideToMove
        {
            get => _sideToMove;

            set
            {
                if (value != _sideToMove)
                    Pass();
                _sideToMove = value;
            }
        }

        public DiscColor OpponentColor => ReversiTypes.ToOpponent(_sideToMove);
        public int EmptySquareCount => _bitboard.EmptyCount;
        public int PlayerDiscCount => _bitboard.PlayerDiscCount;
        public int OpponentDiscCount => _bitboard.OpponentDiscCount;
        public int DiscCount => _bitboard.DiscCount;

        public bool CanPass => BitOperations.PopCount(_bitboard.CalculatePlayerMobility()) == 0
                            && BitOperations.PopCount(_bitboard.CalculateOpponentMobility()) != 0;

        public bool IsGameOver => BitOperations.PopCount(_bitboard.CalculatePlayerMobility()) == 0
                               && BitOperations.PopCount(_bitboard.CalculateOpponentMobility()) == 0;

        public DiscColor Winner
        {
            get
            {
                if (!IsGameOver)
                    return DiscColor.Null;

                var diff = _bitboard.PlayerDiscCount - _bitboard.OpponentDiscCount;
                if (diff == 0)
                    return DiscColor.Null;

                return (diff > 0) ? SideToMove : OpponentColor;
            }
        }

        Bitboard _bitboard;
        DiscColor _sideToMove;
        readonly Stack<Move> _moveHistory = new();

        public Position()
        {
            _bitboard = new Bitboard(CoordToBit[(byte)BoardCoordinate.E4] | CoordToBit[(byte)BoardCoordinate.D5],
                                         CoordToBit[(byte)BoardCoordinate.D4] | CoordToBit[(byte)BoardCoordinate.E5]);
            _sideToMove = DiscColor.Black;
        }

        public Position(Bitboard bitboard, DiscColor sideToMove)
        {
            _bitboard = bitboard;
            _sideToMove = sideToMove;
        }

        public Position(Position pos)
        {
            _bitboard = pos._bitboard;
            _sideToMove = pos._sideToMove;
            foreach (var move in pos._moveHistory.Reverse())
                _moveHistory.Push(move);
        }

        public static bool operator ==(Position left, Position right) => left._sideToMove == right._sideToMove && left._bitboard == right._bitboard;
        public static bool operator !=(Position left, Position right) => !(left == right);

        public override bool Equals(object? obj) => obj is Position pos && this == pos;

        // 警告抑制のためのコード.
        public override int GetHashCode() => base.GetHashCode();

        public int GetDiscCountOf(DiscColor color)
        {
            if (color == DiscColor.Null)
                return 0;

            return (_sideToMove == color) ? PlayerDiscCount : OpponentDiscCount;
        }

        public int? GetScoreFrom(DiscColor color)
        {
            if (!IsGameOver)
                return null;

            var opponent = ReversiTypes.ToOpponent(color);
            var pCount = GetDiscCountOf(color);
            var oCount = GetDiscCountOf(opponent);

            if (pCount == oCount)
                return 0;

            if (pCount > oCount)
                return pCount + EmptySquareCount - oCount;
            else
                return pCount - (oCount + EmptySquareCount);
        }

        /// <summary>
        /// 指定された座標にある石が現在の手番の石なのか, 相手の石なのか, それとも石が存在しないのかを返す.
        /// </summary>
        /// <param name="coord"></param>
        /// <returns></returns>
        public Player GetSquareOwnerAt(BoardCoordinate coord)
            => (Player)(2 - 2 * ((_bitboard.Player >> (byte)coord) & 1) - ((_bitboard.Opponent >> (byte)coord) & 1));

        public DiscColor GetSquareColorAt(BoardCoordinate coord)
        {
            var owner = GetSquareOwnerAt(coord);
            if (owner == Player.Null)
                return DiscColor.Null;
            return (owner == Player.Current) ? _sideToMove : OpponentColor;
        }

        public IEnumerable<(DiscColor color, BoardCoordinate coord)> EnumeratePastMoves(bool firstPlayFirstOut = true)
        {
            var e = _moveHistory.Select(x => (x.Color, x.Coord));
            return firstPlayFirstOut ? e.Reverse() : e;
        }

        public bool IsLegal(BoardCoordinate coord)
            => (coord == BoardCoordinate.PA) ? CanPass : ((_bitboard.CalculatePlayerMobility() & CoordToBit[(byte)coord]) != 0);

        public void Pass()
        {
            _sideToMove = OpponentColor;
            _bitboard.Swap();
            _moveHistory.Push(new Move(OpponentColor, BoardCoordinate.PA));
        }

        public void PutPlayerDiscAt(BoardCoordinate coord) => _bitboard.PutPlayerDiscAt(coord);
        public void PutOpponentDiscAt(BoardCoordinate coord) => _bitboard.PutOpponentDiscAt(coord);

        public void PutDiscAt(DiscColor color, BoardCoordinate coord)
        {
            if (color == DiscColor.Null)
                return;

            if (_sideToMove == color)
                PutPlayerDiscAt(coord);
            else
                PutOpponentDiscAt(coord);
        }

        public void RemoveDiscAt(BoardCoordinate coord) => _bitboard.RemoveDiscAt(coord);

        public bool Update(BoardCoordinate move)
        {
            if (!IsLegal(move))
                return false;

            if (move == BoardCoordinate.PA)
            {
                Pass();
                return true;
            }

            var m = new Move
            {
                Color = _sideToMove,
                Coord = move,
                Flip = _bitboard.CalculateFlippedDiscs(move)
            };

            _sideToMove = OpponentColor;
            _bitboard.Update(m.Coord, m.Flip);
            _moveHistory.Push(m);
            return true;
        }

        public bool UpdateAlongMoves(IEnumerable<BoardCoordinate> moves)
        {
            foreach(var move in moves)
            {
                if (!Update(move))
                    return false;
            }
            return true;
        }

        public bool Undo()
        {
            if (_moveHistory.Count == 0)
                return false;

            _sideToMove = OpponentColor;
            var move = _moveHistory.Pop();
            _bitboard.Undo(move.Coord, move.Flip);
            return true;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("  ");
            for (var i = 0; i < BoardSize; i++)
                sb.Append((char)('A' + i)).Append(' ');

            var p = _bitboard.Player;
            var o = _bitboard.Opponent;
            var mask = 1UL << (NumSquares - 1);
            for (var y = 0; y < BoardSize; y++)
            {
                sb.Append('\n').Append(y + 1).Append(' ');
                for (var x = 0; x < BoardSize; x++)
                {
                    if ((p & mask) != 0)
                        sb.Append((SideToMove == DiscColor.Black) ? "X " : "O ");
                    else if ((o & mask) != 0)
                        sb.Append((SideToMove != DiscColor.Black) ? "X " : "O ");
                    else
                        sb.Append(". ");
                    mask >>= 1;
                }
            }
            return sb.ToString();
        }
    }
}