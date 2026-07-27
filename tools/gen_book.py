#!/usr/bin/env python3
"""
Generates a .book file of random opening positions ("XOT"-style: N random legal moves
played from the standard start position), in the format NBoardLocalGameServer's
OpeningBook expects: one line per position,

    <64-char board><space><side-to-move>

where the board string reads square A1..H8 in order (rank-major: A1 B1 ... H1 A2 ... H8),
using '*' for a black disc, 'O' for a white disc, '-' for empty, and the side-to-move
character is '*' (Black) or 'O' (White). This mirrors Reversi/Types.cs's BoardCoordinate
enum ordering and OpeningBook.cs's parser exactly, so files this script writes load
straight into the app with no conversion step.

This is a standalone, dependency-free local tool -- it does not talk to the web app or
reimplement anything from it beyond this one text format, and is not part of the deployed
application.

Usage:
    python gen_book.py r<N> <count> [output.book]

    r<N>    number of random legal moves to play from the start position for each
            position (e.g. r14 = 14 plies). A forced pass does not count toward N,
            since there was no random choice to make.
    count   how many positions to generate (one per line).
    output.book (optional) output path. Defaults to "r<N>_<count>.book" in the
            current directory.

Example:
    python gen_book.py r14 10000
        -> writes r14_10000.book, 10000 positions each reached by 14 random legal
           moves from the initial position.
"""
import random
import re
import sys

BLACK, WHITE, EMPTY = '*', 'O', '-'
DIRECTIONS = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]


def initial_board():
    board = [EMPTY] * 64
    board[3 * 8 + 3] = WHITE  # D4
    board[3 * 8 + 4] = BLACK  # E4
    board[4 * 8 + 3] = BLACK  # D5
    board[4 * 8 + 4] = WHITE  # E5
    return board


def opponent(color):
    return WHITE if color == BLACK else BLACK


def would_flip(board, r, c, dr, dc, color, opp):
    r, c = r + dr, c + dc
    seen_opp = False
    while 0 <= r < 8 and 0 <= c < 8:
        cell = board[r * 8 + c]
        if cell == opp:
            seen_opp = True
        elif cell == color:
            return seen_opp
        else:
            return False
        r, c = r + dr, c + dc
    return False


def legal_moves(board, color):
    opp = opponent(color)
    moves = []
    for idx in range(64):
        if board[idx] != EMPTY:
            continue
        r, c = divmod(idx, 8)
        if any(would_flip(board, r, c, dr, dc, color, opp) for dr, dc in DIRECTIONS):
            moves.append(idx)
    return moves


def apply_move(board, idx, color):
    opp = opponent(color)
    r, c = divmod(idx, 8)
    board[idx] = color
    for dr, dc in DIRECTIONS:
        if would_flip(board, r, c, dr, dc, color, opp):
            rr, cc = r + dr, c + dc
            while board[rr * 8 + cc] == opp:
                board[rr * 8 + cc] = color
                rr, cc = rr + dr, cc + dc


def random_position(num_plies, rng):
    """Plays num_plies random legal moves from the start position. Returns
    (board, side_to_move), or None if the game ended (both sides passed) before
    reaching num_plies -- the caller should just try again from a fresh start."""
    board = initial_board()
    color = BLACK
    plies_played = 0
    while plies_played < num_plies:
        moves = legal_moves(board, color)
        if not moves:
            color = opponent(color)
            if not legal_moves(board, color):
                return None
            continue  # forced pass -- doesn't consume one of the N random plies
        apply_move(board, rng.choice(moves), color)
        color = opponent(color)
        plies_played += 1
    return board, color


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    m = re.fullmatch(r'r(\d+)', sys.argv[1])
    if not m:
        print(f'First argument must look like "r<N>" (e.g. r14), got "{sys.argv[1]}"')
        sys.exit(1)
    num_plies = int(m.group(1))
    count = int(sys.argv[2])
    out_path = sys.argv[3] if len(sys.argv) > 3 else f'r{num_plies}_{count}.book'

    rng = random.Random()
    with open(out_path, 'w', encoding='ascii') as f:
        for _ in range(count):
            result = None
            while result is None:
                result = random_position(num_plies, rng)
            board, color = result
            f.write(''.join(board) + ' ' + color + '\n')

    print(f'Wrote {count} positions ({num_plies} random plies each) to {out_path}')


if __name__ == '__main__':
    main()
