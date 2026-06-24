using System;
using System.IO;
using System.Collections.Generic;

using NBoardLocalGameServer.Reversi;

namespace NBoardLocalGameServer
{
    /// <summary>
    /// 序盤進行集. テキストファイルからロードする.
    /// 
    /// テキストファイルのフォーマットは, 各行に棋譜が記述されたもの.
    /// 棋譜の形式は [盤面('*': 黒, 'O': 白, '-': 空きマス)] [手番('*': 黒, 'O': 白)]
    /// 
    /// ex)
    /// ---------------------------O*------O*--------------------------- * 
    /// </summary>
    internal class OpeningBook
    {
        public static OpeningBook Empty { get; } = new();

        public int NumPositions => _book.Length;

        readonly Position[] _book;
        int _loc = 0;

        public OpeningBook(string path)
        {
            using var sr = new StreamReader(path);
            var book = new List<Position>();
            var lineCount = 0;
            while (sr.Peek() != -1)
            {
                lineCount++;

                var line = sr.ReadLine()?.Trim();
                if (line is null || line == string.Empty)
                    continue;

                book.Add(ParseBookItem(line, lineCount));
            }
            _book = [.. book];
        }

        OpeningBook() => _book = [];

        public Position GetPosition() => new(_book[_loc++ % _book.Length]);
        public void Shuffle() => Shuffle(Random.Shared);
        public void Shuffle(Random rand) => rand.Shuffle(_book);

        static Position ParseBookItem(string str, int lineNum)
        {
            var pos = new Position();

            var splitted = str.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

            if (splitted.Length < 2)
                throw new FormatException($"Line {lineNum} is invalid. It must contain at least position and side to move.");

            var posStr = splitted[0];
            if (posStr.Length < Constants.NumSquares)
                throw new FormatException($"Position string at line {lineNum} is too short.");

            for (var coord = BoardCoordinate.A1; coord <= BoardCoordinate.H8; coord++)
            {
                var ch = posStr[(byte)coord];
                if (ch == '*')
                    pos.PutDiscAt(DiscColor.Black, coord);
                else if (ch == 'O')
                    pos.PutDiscAt(DiscColor.White, coord);
                else if (ch == '-')
                    pos.RemoveDiscAt(coord);
                else
                    throw new FormatException($"Character \'{ch}\' at line {lineNum} is invalid. It must be '*', 'O' or '-'.");
            }

            var sideToMoveCh = splitted[1][0];
            if (sideToMoveCh == '*')
                pos.SideToMove = DiscColor.Black;
            else if (sideToMoveCh == 'O')
                pos.SideToMove = DiscColor.White;
            else
                throw new FormatException($"Character \'{sideToMoveCh}\' at line {lineNum} is invalid. It must be '*' or 'O'.");

            return pos;
        }
    }
}
