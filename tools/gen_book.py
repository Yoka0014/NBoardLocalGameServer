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
    python gen_book.py d<N> <count> [output.book]
    python gen_book.py g<N> <count> [output.book]

    r<N>    number of random legal moves to play from the start position for each
            position (e.g. r14 = 14 plies). A forced pass does not count toward N,
            since there was no random choice to make.
    d<N>    total number of discs on the board for each generated position
            (e.g. d14 = 14 discs). Every legal move places exactly one disc (flips
            change color, not count) and passes place none, so a position has N
            discs iff exactly N-4 random legal moves have been played from the
            start position (4 initial discs). This is really just r<N-4> under a
            more convenient name when what you care about is disc count rather
            than ply count.
    g<N>    total number of discs (like d<N>), but generated with GGS's own R<N>
            random-setup algorithm (Service/Othello/src/OthelloImpl.C in the GGS
            server source) instead of "play random legal moves from the standard
            start". For N>=10, GGS itself coin-flips per position between two
            different generators, so this mode does too:
              - random_setup(N): discs are placed directly (not via a legal-move
                sequence). Corners and their neighbors are excluded from
                selection; the board is divided into concentric Chebyshev "rings"
                centered on the middle 2x2, shuffled ring-by-ring, and the first N
                squares (innermost rings first) are selected and given random
                colors. Color split and side-to-move follow GGS's exact formulas
                (see direct_setup() below: side-to-move is deterministic from the
                parity of N, not random). Always connected (rings guarantee
                adjacency), but the resulting position is not necessarily
                reachable through actual legal play.
              - random_setup_2(N): starts from a random_setup(5) 5-disc position,
                then plays N-5 random legal moves (excluding corner-adjacent
                squares), retrying from scratch up to 5 times if it runs out of
                legal moves or ends up too color-lopsided (either side <=
                max(N//4, 3) discs). Falls back to random_setup(N) if all 5
                retries fail. This one *is* reachable via legal play.
            For N<10, GGS always uses random_setup(N) (no coin flip); for N<=3 it
            just returns the standard 4-disc start.
    count   how many positions to generate (one per line).
    output.book (optional) output path. Defaults to "r<N>_<count>.book" (or
            "d<N>_<count>.book" / "g<N>_<count>.book") in the current directory.

Example:
    python gen_book.py r14 10000
        -> writes r14_10000.book, 10000 positions each reached by 14 random legal
           moves from the initial position (18 discs each).
    python gen_book.py d14 5000
        -> writes d14_5000.book, 5000 positions each with exactly 14 discs on the
           board (i.e. 10 random legal moves from the initial position).
    python gen_book.py g14 5000
        -> writes g14_5000.book, 5000 positions with exactly 14 discs, generated
           via GGS's R14 algorithm (coin-flip between random_setup and
           random_setup_2).
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


def legal_moves(board, color, exclude=None):
    opp = opponent(color)
    moves = []
    for idx in range(64):
        if board[idx] != EMPTY or (exclude and idx in exclude):
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


def corner_avoid_squares():
    """The four corners plus every square adjacent (incl. diagonally) to a
    corner -- GGS's avoid[] set, excluded from direct placement and from
    random_setup_2's legal-move candidates."""
    avoid = set()
    for r, c in ((0, 0), (0, 7), (7, 0), (7, 7)):
        avoid.add(r * 8 + c)
        for dr in (-1, 0, 1):
            for dc in (-1, 0, 1):
                if dr == 0 and dc == 0:
                    continue
                rr, cc = r + dr, c + dc
                if 0 <= rr < 8 and 0 <= cc < 8:
                    avoid.add(rr * 8 + cc)
    return avoid


def chebyshev_rings(avoid):
    """Groups the squares not in avoid into concentric Chebyshev rings
    around the board center, innermost first (ring 0 = the central 2x2)."""
    groups = {}
    for idx in range(64):
        if idx in avoid:
            continue
        r, c = divmod(idx, 8)
        d = (max(abs(2 * r - 7), abs(2 * c - 7)) - 1) // 2
        groups.setdefault(d, []).append(idx)
    return [groups[d] for d in sorted(groups)]


def direct_setup(count, rng, avoid, rings):
    """GGS's random_setup(ra): places `count` discs directly (no legal-move
    simulation), filling Chebyshev rings from the center outward so the
    result is always a single connected group, then splits colors and picks
    the side to move exactly as GGS's C++ does:

        int rnd_white = ra / 2;
        if ((ra & 1) && ::ra.num(2)) rnd_white++;   // odd ra: 50% chance +1
        int imb = rnd_white / 3;
        rnd_white += ::ra.num(2*imb+1) - imb;       // uniform in [-imb, +imb]
        turn_color = (ra & 1) ? WHITE : BLACK;       // deterministic, NOT random
    """
    mm = []
    for ring in rings:
        shuffled = ring[:]
        rng.shuffle(shuffled)
        mm.extend(shuffled)
    if count > len(mm):
        raise ValueError(
            f'Cannot place {count} discs with corners/corner-adjacent squares '
            f'excluded; at most {len(mm)} squares are eligible.')
    selected = mm[:count]
    rng.shuffle(selected)

    white = count // 2
    if count % 2 == 1 and rng.random() < 0.5:
        white += 1
    imb = white // 3
    white += rng.randint(0, 2 * imb) - imb
    white = max(0, min(count, white))
    black = count - white

    colors = [BLACK] * black + [WHITE] * white
    rng.shuffle(colors)

    board = [EMPTY] * 64
    for idx, color in zip(selected, colors):
        board[idx] = color

    side = WHITE if count % 2 == 1 else BLACK  # "regular othello parity" -- deterministic
    return board, side


def random_setup_2(count, rng, avoid, rings, max_retries=5):
    """GGS's random_setup_2(ra): a random_setup(5) 5-disc start, then count-5
    random legal moves (corner-adjacent squares excluded throughout), retried
    from scratch up to max_retries times on a dead end or a lopsided result,
    falling back to direct_setup(count) if every retry fails."""
    for _ in range(max_retries):
        board, color = direct_setup(5, rng, avoid, rings)
        ok = True
        for _ in range(count - 5):
            moves = legal_moves(board, color, exclude=avoid)
            if not moves:
                ok = False
                break
            apply_move(board, rng.choice(moves), color)
            color = opponent(color)
        if not ok:
            continue
        black_count = board.count(BLACK)
        white_count = board.count(WHITE)
        if min(black_count, white_count) <= max(count // 4, 3):
            continue
        return board, color
    return direct_setup(count, rng, avoid, rings)


def ggs_setup(count, rng, avoid, rings):
    """Reproduces GGS's Board::init dispatch table for R<count>."""
    if count <= 3:
        return initial_board(), BLACK
    if count < 10:
        return direct_setup(count, rng, avoid, rings)
    if rng.random() < 0.5:
        return random_setup_2(count, rng, avoid, rings)
    return direct_setup(count, rng, avoid, rings)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    m = re.fullmatch(r'([rdg])(\d+)', sys.argv[1])
    if not m:
        print(f'First argument must look like "r<N>", "d<N>" or "g<N>" (e.g. r14, d14, g14), got "{sys.argv[1]}"')
        sys.exit(1)
    mode, n = m.group(1), int(m.group(2))
    if mode == 'd':
        if n < 4:
            print(f'd<N> must be at least 4 (the initial disc count), got d{n}')
            sys.exit(1)
    elif mode == 'g':
        if n < 0:
            print(f'g<N> must be non-negative, got g{n}')
            sys.exit(1)
    count = int(sys.argv[2])
    out_path = sys.argv[3] if len(sys.argv) > 3 else f'{mode}{n}_{count}.book'

    rng = random.Random()
    with open(out_path, 'w', encoding='ascii') as f:
        if mode == 'g':
            avoid = corner_avoid_squares()
            rings = chebyshev_rings(avoid)
            for _ in range(count):
                board, color = ggs_setup(n, rng, avoid, rings)
                f.write(''.join(board) + ' ' + color + '\n')
        else:
            num_plies = n - 4 if mode == 'd' else n
            for _ in range(count):
                result = None
                while result is None:
                    result = random_position(num_plies, rng)
                board, color = result
                f.write(''.join(board) + ' ' + color + '\n')

    print(f'Wrote {count} positions to {out_path}')


if __name__ == '__main__':
    main()
