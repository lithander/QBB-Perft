/*
 This perft implementation is based on QBBEngine by Fabio Gobbato and ported to C# by Thomas Jahn
 
 The purpose is to compare the speed differences of C# and C in chess-programming workload.
*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;

namespace QBB
{
    class QbbPerft
    {
        const int MAX_PLY = 32;
        const int WHITE = 0;
        const int BLACK = 8;

        /* define the move type, for example
           KING|CASTLE is a castle move
           PAWN|CAPTURE|EP is an enpassant move
           PAWN|PROMO|CAPTURE is a promotion with a capture */

        /* define the piece type: empty, pawn, knight, bishop, rook, queen, king */
        [Flags]
        enum TPieceType : byte { EMPTY, PAWN, KNIGHT, BISHOP, ROOK, QUEEN, KING, PIECE_MASK = 0x07, CASTLE = 0x40, PROMO = 0x20, EP = 0x10, CAPTURE = 0x08 }


        /* move structure */
        struct TMove
        {
            public TPieceType MoveType;
            public byte From;
            public byte To;
            public TPieceType Promotion;
        };


        static TMove[][] MovesLists;

        static QbbPerft()
        {
            Game = new TBoard[MAX_PLY];
            MovesLists = new TMove[MAX_PLY][];
            for (int i = 0; i < MAX_PLY; i++)
            {
                MovesLists[i] = new TMove[225];
                Game[i] = new TBoard();
            }

            Position = Game[0];
        }

        /*
        Board structure definition

        PM,P0,P1,P2 are the 4 bitboards that contain the whole board
        PM is the bitboard with the side to move pieces
        P0,P1 and P2: with these bitboards you can obtain every type of pieces and every pieces combinations.
        */
        class TBoard
        {
            public ulong PM;
            public ulong P0;
            public ulong P1;
            public ulong P2;
            public byte CastleFlags; /* ..sl..SL  short long opponent SHORT LONG side to move */
            public byte EnPassant; /* enpassant column, =8 if not set */
            public byte STM; /* side to move */

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public TBoard Copy(TBoard other)
            {
                PM = other.PM;
                P0 = other.P0;
                P1 = other.P1;
                P2 = other.P2;
                CastleFlags = other.CastleFlags;
                EnPassant = other.EnPassant;
                STM = other.STM;
                return this;
            }
        }

        static TBoard[] Game;
        static int iPosition;
        private static TBoard Position;

        /* array of bitboards that contains all the knight destination for every square */
        static readonly ulong[] KnightDest = {
            0x0000000000020400UL,0x0000000000050800UL,0x00000000000a1100UL,0x0000000000142200UL,
            0x0000000000284400UL,0x0000000000508800UL,0x0000000000a01000UL,0x0000000000402000UL,
            0x0000000002040004UL,0x0000000005080008UL,0x000000000a110011UL,0x0000000014220022UL,
            0x0000000028440044UL,0x0000000050880088UL,0x00000000a0100010UL,0x0000000040200020UL,
            0x0000000204000402UL,0x0000000508000805UL,0x0000000a1100110aUL,0x0000001422002214UL,
            0x0000002844004428UL,0x0000005088008850UL,0x000000a0100010a0UL,0x0000004020002040UL,
            0x0000020400040200UL,0x0000050800080500UL,0x00000a1100110a00UL,0x0000142200221400UL,
            0x0000284400442800UL,0x0000508800885000UL,0x0000a0100010a000UL,0x0000402000204000UL,
            0x0002040004020000UL,0x0005080008050000UL,0x000a1100110a0000UL,0x0014220022140000UL,
            0x0028440044280000UL,0x0050880088500000UL,0x00a0100010a00000UL,0x0040200020400000UL,
            0x0204000402000000UL,0x0508000805000000UL,0x0a1100110a000000UL,0x1422002214000000UL,
            0x2844004428000000UL,0x5088008850000000UL,0xa0100010a0000000UL,0x4020002040000000UL,
            0x0400040200000000UL,0x0800080500000000UL,0x1100110a00000000UL,0x2200221400000000UL,
            0x4400442800000000UL,0x8800885000000000UL,0x100010a000000000UL,0x2000204000000000UL,
            0x0004020000000000UL,0x0008050000000000UL,0x00110a0000000000UL,0x0022140000000000UL,
            0x0044280000000000UL,0x0088500000000000UL,0x0010a00000000000UL,0x0020400000000000UL,
        };

        /* The same for the king */
        static readonly ulong[] KingDest = {
            0x0000000000000302UL,0x0000000000000705UL,0x0000000000000e0aUL,0x0000000000001c14UL,
            0x0000000000003828UL,0x0000000000007050UL,0x000000000000e0a0UL,0x000000000000c040UL,
            0x0000000000030203UL,0x0000000000070507UL,0x00000000000e0a0eUL,0x00000000001c141cUL,
            0x0000000000382838UL,0x0000000000705070UL,0x0000000000e0a0e0UL,0x0000000000c040c0UL,
            0x0000000003020300UL,0x0000000007050700UL,0x000000000e0a0e00UL,0x000000001c141c00UL,
            0x0000000038283800UL,0x0000000070507000UL,0x00000000e0a0e000UL,0x00000000c040c000UL,
            0x0000000302030000UL,0x0000000705070000UL,0x0000000e0a0e0000UL,0x0000001c141c0000UL,
            0x0000003828380000UL,0x0000007050700000UL,0x000000e0a0e00000UL,0x000000c040c00000UL,
            0x0000030203000000UL,0x0000070507000000UL,0x00000e0a0e000000UL,0x00001c141c000000UL,
            0x0000382838000000UL,0x0000705070000000UL,0x0000e0a0e0000000UL,0x0000c040c0000000UL,
            0x0003020300000000UL,0x0007050700000000UL,0x000e0a0e00000000UL,0x001c141c00000000UL,
            0x0038283800000000UL,0x0070507000000000UL,0x00e0a0e000000000UL,0x00c040c000000000UL,
            0x0302030000000000UL,0x0705070000000000UL,0x0e0a0e0000000000UL,0x1c141c0000000000UL,
            0x3828380000000000UL,0x7050700000000000UL,0xe0a0e00000000000UL,0xc040c00000000000UL,
            0x0203000000000000UL,0x0507000000000000UL,0x0a0e000000000000UL,0x141c000000000000UL,
            0x2838000000000000UL,0x5070000000000000UL,0xa0e0000000000000UL,0x40c0000000000000UL
        };

        /* masks for finding the pawns that can capture with an enpassant (in move generation) */
        static readonly ulong[] EnPassant = {
            0x0000000200000000UL,0x0000000500000000UL,0x0000000A00000000UL,0x0000001400000000UL,
            0x0000002800000000UL,0x0000005000000000UL,0x000000A000000000UL,0x0000004000000000UL
        };

        /* masks for finding the pawns that can capture with an enpassant (in make move) */
        static readonly ulong[] EnPassantM = {
            0x0000000002000000UL,0x0000000005000000UL,0x000000000A000000UL,0x0000000014000000UL,
            0x0000000028000000UL,0x0000000050000000UL,0x00000000A0000000UL,0x0000000040000000UL
        };

        /*
        reverse a bitboard:
        A bitboard is an array of byte: Byte0,Byte1,Byte2,Byte3,Byte4,Byte5,Byte6,Byte7
        after this function the bitboard will be: Byte7,Byte6,Byte5,Byte4,Byte3,Byte2,Byte1,Byte0

        The board is saved always with the side to move in the low significant bits of the bitboard, so this function
        is used to change the side to move
        */
        //#define RevBB(bb) (__builtin_bswap64(bb))
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong _RevBB(ulong bb)
        {
            //Swap adjacent 32-bit blocks
            bb = (bb >> 32) | (bb << 32);
            //Swap adjacent 16-bit blocks
            bb = ((bb & 0xFFFF0000FFFF0000U) >> 16) | ((bb & 0x0000FFFF0000FFFFU) << 16);
            //Swap adjacent 8-bit blocks
            bb = ((bb & 0xFF00FF00FF00FF00U) >> 8) | ((bb & 0x00FF00FF00FF00FFU) << 8);
            return bb;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong RevBB(ulong bb) => BinaryPrimitives.ReverseEndianness(bb);

        /* return the index of the most significant bit of the bitboard, bb must always be !=0 */
        //#define MSB(bb) (0x3F ^ __builtin_clzll(bb))
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong MSB(ulong bb) => 63 ^ Lzcnt.X64.LeadingZeroCount(bb);

        /* return the index of the least significant bit of the bitboard, bb must always be !=0 */
        //#define LSB(bb) (__builtin_ctzll(bb))
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong LSB(ulong bb) => Bmi1.X64.TrailingZeroCount(bb);

        /* extract the least significant bit of the bitboard */
        //#define ExtractLSB(bb) ((bb)&(-(bb)))
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong _ExtractLSB(ulong bb) => bb & (0 - bb);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong ExtractLSB(ulong bb) => Bmi1.X64.ExtractLowestSetBit(bb);

        /* reset the least significant bit of bb */
        //#define ClearLSB(bb) ((bb)&((bb)-1))
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong _ClearLSB(ulong bb) => bb & (bb - 1);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static ulong ClearLSB(ulong bb) => Bmi1.X64.ResetLowestSetBit(bb);


        /* Macro to check and reset the castle rights:
           CastleSM: short castling side to move
           CastleLM: long castling side to move
           CastleSO: short castling opponent
           CastleLO: long castling opponent
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static bool CanCastleSM() => (Position.CastleFlags & 0x02) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static bool CanCastleLM() => (Position.CastleFlags & 0x01) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static void ResetCastleSM() => Position.CastleFlags &= 0xFD;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static void ResetCastleLM() => Position.CastleFlags &= 0xFE;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static void ResetCastleSO() => Position.CastleFlags &= 0xDF;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static void ResetCastleLO() => Position.CastleFlags &= 0xEF;

        /* these Macros are used to calculate the bitboard of a particular kind of piece

           P2 P1 P0
            0  0  0    empty
            0  0  1    pawn
            0  1  0    knight
            0  1  1    bishop
            1  0  0    rook
            1  0  1    queen
            1  1  0    king
        */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Occupation() => Position.P0 | Position.P1 | Position.P2; /* board occupation */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Pawns() => Position.P0 & ~Position.P1 & ~Position.P2; /* all the pawns on the board */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Knights() => ~Position.P0 & Position.P1 & ~Position.P2;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Bishops() => Position.P0 & Position.P1;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Rooks() => ~Position.P0 & ~Position.P1 & Position.P2;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Queens() => Position.P0 & Position.P2;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong QueenOrRooks() => ~Position.P1 & Position.P2;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong QueenOrBishops() => Position.P0 & (Position.P2 | Position.P1);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Kings() => Position.P1 & Position.P2; /* a bitboard with the 2 kings */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong SideToMove() => Position.PM;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static byte EnPass() => Position.EnPassant;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Opposing() => Position.PM ^ (Position.P0 | Position.P1 | Position.P2);

        /* get the piece type giving the square */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static ulong Piece(int sq) => ((Position.P2 >> sq) & 1) << 2 |
                                              ((Position.P1 >> sq) & 1) << 1 |
                                              ((Position.P0 >> sq) & 1);

        /* calculate the square related to the opponent */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static int OppSq(int sp) => sp ^ 56;
        /* Absolute Square, we need this macro to return the move in long algebric notation  */
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        static int AbsSq(int sq, int col) => col == WHITE ? sq : OppSq(sq);

        /* get the corresponding string to the given move  */
        static string MoveToStr(TMove move, byte tomove)
        {
            Span<char> promo = stackalloc[] { ' ', ' ', 'n', 'b', 'r', 'q' };
            StringBuilder result = new StringBuilder(6);
            result.Append((char)('a' + AbsSq(move.From, tomove) % 8));
            result.Append((char)('1' + AbsSq(move.From, tomove) / 8));
            result.Append((char)('a' + AbsSq(move.To, tomove) % 8));
            result.Append((char)('1' + AbsSq(move.To, tomove) / 8));
            result.Append(promo[(byte)move.Promotion]);
            return result.ToString();
        }

        static void ChangeSide()
        {
            Position.PM ^= Occupation(); /* update the side to move pieces */
            Position.PM = RevBB(Position.PM);
            Position.P0 = RevBB(Position.P0);
            Position.P1 = RevBB(Position.P1);
            Position.P2 = RevBB(Position.P2);/* reverse the board */
            Position.CastleFlags = (byte)((Position.CastleFlags >> 4) | (Position.CastleFlags << 4));/* roll the castle rights */
            Position.STM ^= BLACK; /* change the side to move */
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GenRook(int sq, ulong occupation)
        {
            //ulong piece = 1UL << sq;
            //occupation ^= piece; /* remove the selected piece from the occupation */
            //ulong piecesup = (0x0101010101010101UL << sq) & (occupation | 0xFF00000000000000UL); /* find the pieces up */
            //ulong piecesdo = (0x8080808080808080UL >> (63 - sq)) & (occupation | 0x00000000000000FFUL); /* find the pieces down */
            //ulong piecesri = (0x00000000000000FFUL << sq) & (occupation | 0x8080808080808080UL); /* find pieces on the right */
            //ulong piecesle = (0xFF00000000000000UL >> (63 - sq)) & (occupation | 0x0101010101010101UL); /* find pieces on the left */
            //return (((0x8080808080808080UL >> (63 - (int)LSB(piecesup))) & (0x0101010101010101UL << (int)MSB(piecesdo))) |
            //             ((0xFF00000000000000UL >> (63 - (int)LSB(piecesri))) & (0x00000000000000FFUL << (int)MSB(piecesle)))) ^ piece;
            /* From every direction find the first piece and from that piece put a mask in the opposite direction.
               Put togheter all the 4 masks and remove the moving piece */

            occupation ^= 1UL << sq; /* remove the selected piece from the occupation */
            return (((0x8080808080808080UL >> (63 ^ (int)LSB((0x0101010101010101UL << sq) & (occupation | 0xFF00000000000000UL)))) & (0x0101010101010101UL << (int)MSB((0x8080808080808080UL >> (63 ^ sq)) & (occupation | 0x00000000000000FFUL)))) |
                    ((0xFF00000000000000UL >> (63 ^ (int)LSB((0x00000000000000FFUL << sq) & (occupation | 0x8080808080808080UL)))) & (0x00000000000000FFUL << (int)MSB((0xFF00000000000000UL >> (63 ^ sq)) & (occupation | 0x0101010101010101UL)))));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GenBishop(int sq, ulong occupation)
        {
            /* it's the same as the rook */
            //ulong piece = 1UL << sq;
            //occupation ^= piece;
            //ulong piecesup = (0x8040201008040201UL << sq) & (occupation | 0xFF80808080808080UL);
            //ulong piecesdo = (0x8040201008040201UL >> (63 - sq)) & (occupation | 0x01010101010101FFUL);
            //ulong piecesle = (0x8102040810204081UL << sq) & (occupation | 0xFF01010101010101UL);
            //ulong piecesri = (0x8102040810204081UL >> (63 - sq)) & (occupation | 0x80808080808080FFUL);
            //return (((0x8040201008040201UL >> (63 - (int)LSB(piecesup))) & (0x8040201008040201UL << (int)MSB(piecesdo))) |
            //             ((0x8102040810204081UL >> (63 - (int)LSB(piecesle))) & (0x8102040810204081UL << (int)MSB(piecesri)))) ^ piece;

            occupation ^= 1UL << sq; /* remove the selected piece from the occupation */
            return (((0x8040201008040201UL >> (63 ^ (int)LSB((0x8040201008040201UL << sq) & (occupation | 0xFF80808080808080UL)))) & (0x8040201008040201UL << (int)MSB((0x8040201008040201UL >> (63 ^ sq)) & (occupation | 0x01010101010101FFUL)))) |
                    ((0x8102040810204081UL >> (63 ^ (int)LSB((0x8102040810204081UL << sq) & (occupation | 0xFF01010101010101UL)))) & (0x8102040810204081UL << (int)MSB((0x8102040810204081UL >> (63 ^ sq)) & (occupation | 0x80808080808080FFUL)))));
        }

        /* return the bitboard with pieces of the same type */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong BBPieces(TPieceType piece)
        {
            switch (piece) // find the bb with the pieces of the same type
            {
                case TPieceType.PAWN: return Pawns();
                case TPieceType.KNIGHT: return Knights();
                case TPieceType.BISHOP: return Bishops();
                case TPieceType.ROOK: return Rooks();
                case TPieceType.QUEEN: return Queens();
                case TPieceType.KING: return Kings();
                default: return 0;
            }
        }

        /* return the bitboard with the destinations of a piece in a square (exept for pawns) */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong BBDestinations(TPieceType piece, int sq, ulong occupation)
        {
            switch (piece) // generate the destination squares of the piece
            {
                case TPieceType.KNIGHT: return KnightDest[sq];
                case TPieceType.BISHOP: return GenBishop(sq, occupation);
                case TPieceType.ROOK: return GenRook(sq, occupation);
                case TPieceType.QUEEN: return GenRook(sq, occupation) | GenBishop(sq, occupation);
                case TPieceType.KING: return KingDest[sq];
                default: return 0;
            }
        }


        /* try the move and see if the king is in check. If so return the attacking pieces, if not return 0 */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Illegal(ref TMove move)
        {
            ulong From = 1UL << move.From;
            ulong To = 1UL << move.To;
            ulong king;
            int kingsq;
            ulong newoccupation = (Occupation() ^ From) | To;
            ulong newopposing = Opposing() & ~To;
            if ((move.MoveType & TPieceType.PIECE_MASK) == TPieceType.KING)
            {
                king = To;
                kingsq = move.To;
            }
            else
            {
                king = Kings() & SideToMove();
                kingsq = (int)LSB(king);
                if ((move.MoveType & TPieceType.EP) != 0)
                {
                    newopposing ^= To >> 8;
                    newoccupation ^= To >> 8;
                }
            }

            return //as soon as there's one attack you can stop evaluating the remaining peieces (early out)
                (Knights() & newopposing & KnightDest[kingsq]) > 0 ||
                (Pawns() & newopposing & (((king << 9) & 0xFEFEFEFEFEFEFEFEUL) | ((king << 7) & 0x7F7F7F7F7F7F7F7FUL))) > 0 ||
                ((QueenOrBishops() & newopposing) > 0 && (QueenOrBishops() & newopposing & GenBishop(kingsq, newoccupation)) > 0) ||
                ((QueenOrRooks() & newopposing) > 0 && (QueenOrRooks() & newopposing & GenRook(kingsq, newoccupation)) > 0) ||
                (Kings() & newopposing & KingDest[kingsq]) > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GenerateQuiets(in TMove[] moves, ref int index)
        {
            ulong occupation = Occupation();
            ulong opposing = Opposing();

            // generate moves from king to knight
            for (TPieceType piece = TPieceType.KING; piece >= TPieceType.KNIGHT; piece--)
            {
                // generate moves for every piece of the same type of the side to move
                for (ulong pieces = BBPieces(piece) & SideToMove(); pieces != 0; pieces = ClearLSB(pieces))
                {
                    int square = (int)LSB(pieces);
                    // for every destinations on a free square generate a move
                    for (ulong destinations = ~occupation & BBDestinations(piece, square, occupation); destinations != 0; destinations = ClearLSB(destinations))
                        moves[index++] = new TMove
                        {
                            MoveType = piece,
                            From = (byte)square,
                            To = (byte)LSB(destinations)
                        };
                }
            }

            /* one pawns push */
            ulong push1 = (((Pawns() & SideToMove()) << 8) & ~occupation) & 0x00FFFFFFFFFFFFFFUL;
            for (ulong pieces = push1; pieces != 0; pieces = ClearLSB(pieces))
                moves[index++] = new TMove
                {
                    MoveType = TPieceType.PAWN,
                    From = (byte)(LSB(pieces) - 8),
                    To = (byte)LSB(pieces) //TODO: avoid calling LSB twice?
                };

            /* double pawns pushes */
            ulong push2 = (push1 << 8) & ~occupation & 0x00000000FF000000UL;
            for (; push2 != 0; push2 = ClearLSB(push2))
                moves[index++] = new TMove
                {
                    MoveType = TPieceType.PAWN,
                    From = (byte)(LSB(push2) - 16),
                    To = (byte)LSB(push2) //TODO: avoid calling LSB twice?
                };

            /* check if long castling is possible */
            if (CanCastleLM() && (occupation & 0x0EUL) == 0)
            {
                ulong roo = ExtractLSB(0x1010101010101000UL & occupation); /* column e */
                roo |= ExtractLSB(0x0808080808080800UL & occupation); /*column d */
                roo |= ExtractLSB(0x0404040404040400UL & occupation); /*column c */
                roo |= ExtractLSB(0x00000000000000E0UL & occupation);  /* row 1 */
                ulong bis = ExtractLSB(0x0000000102040800UL & occupation); /*antidiag from e1/e8 */
                bis |= ExtractLSB(0x0000000001020400UL & occupation); /*antidiag from d1/d8 */
                bis |= ExtractLSB(0x0000000000010200UL & occupation); /*antidiag from c1/c8 */
                bis |= ExtractLSB(0x0000000080402000UL & occupation); /*diag from e1/e8 */
                bis |= ExtractLSB(0x0000008040201000UL & occupation); /*diag from d1/d8 */
                bis |= ExtractLSB(0x0000804020100800UL & occupation); /*diag from c1/c8 */
                if ((((roo & QueenOrRooks()) | (bis & QueenOrBishops()) | (0x00000000003E7700UL & Knights()) |
                (0x0000000000003E00UL & Pawns()) | (Kings() & 0x0000000000000600UL)) & opposing) == 0)
                    /* check if c1/c8 d1/d8 e1/e8 are not attacked */
                    moves[index++] = new TMove
                    {
                        MoveType = TPieceType.KING | TPieceType.CASTLE,
                        From = 4,
                        To = 2,
                    };
            }

            /* check if short castling is possible */
            if (CanCastleSM() && (occupation & 0x60UL) == 0)
            {
                ulong roo = ExtractLSB(0x1010101010101000UL & occupation); /* column e */
                roo |= ExtractLSB(0x2020202020202000UL & occupation); /* column f */
                roo |= ExtractLSB(0x4040404040404000UL & occupation); /* column g */
                roo |= 1UL << (byte)MSB(0x000000000000000FUL & (occupation | 0x1UL));/* row 1 */
                ulong bis = ExtractLSB(0x0000000102040800UL & occupation); /* antidiag from e1/e8 */
                bis |= ExtractLSB(0x0000010204081000UL & occupation); /*antidiag from f1/f8 */
                bis |= ExtractLSB(0x0001020408102000UL & occupation); /*antidiag from g1/g8 */
                bis |= ExtractLSB(0x0000000080402000UL & occupation); /*diag from e1/e8 */
                bis |= ExtractLSB(0x0000000000804000UL & occupation); /*diag from f1/f8 */
                bis |= 0x0000000000008000UL; /*diag from g1/g8 */
                if ((((roo & QueenOrRooks()) | (bis & QueenOrBishops()) | (0x0000000000F8DC00UL & Knights()) |
                (0x000000000000F800UL & Pawns()) | (Kings() & 0x0000000000004000UL)) & opposing) == 0)
                    /* check if e1/e8 f1/f8 g1/g8 are not attacked */
                    moves[index++] = new TMove
                    {
                        MoveType = TPieceType.KING | TPieceType.CASTLE,
                        From = 4,
                        To = 6,
                    };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GenerateCapture(in TMove[] moves, ref int index)
        {
            ulong occupation = Occupation();
            ulong opposing = Opposing();

            // generate moves from king to knight
            for (TPieceType piece = TPieceType.KING; piece >= TPieceType.KNIGHT; piece--)
            {
                // generate moves for every piece of the same type of the side to move
                for (ulong pieces = BBPieces(piece) & SideToMove(); pieces != 0; pieces = ClearLSB(pieces))
                {
                    int square = (int)LSB(pieces);

                    // for every destinations on an opponent pieces generate a move
                    for (ulong destinations = opposing & BBDestinations(piece, square, occupation);
                        destinations != 0;
                        destinations = ClearLSB(destinations))
                        moves[index++] = new TMove
                        {
                            MoveType = piece | TPieceType.CAPTURE,
                            From = (byte)square,
                            To = (byte)LSB(destinations),
                            //Eval = (Piece(LSB(destinations)) << 4) | (KING - piece);
                        };
                }
            }

            ulong pawns = Pawns() & SideToMove();
            /* Generate pawns right captures */
            for (ulong rpc = (pawns << 9) & 0x00FEFEFEFEFEFEFEUL & opposing; rpc != 0; rpc = ClearLSB(rpc))
                moves[index++] = new TMove
                {
                    MoveType = TPieceType.PAWN | TPieceType.CAPTURE,
                    From = (byte)(LSB(rpc) - 9),
                    To = (byte)LSB(rpc),
                    //Eval = (Piece(LSB(captureri)) << 4) | (KING - PAWN);
                };

            /* Generate pawns left captures */
            for (ulong lpc = (pawns << 7) & 0x007F7F7F7F7F7F7FUL & opposing; lpc != 0; lpc = ClearLSB(lpc))
                moves[index++] = new TMove
                {
                    MoveType = TPieceType.PAWN | TPieceType.CAPTURE,
                    From = (byte)(LSB(lpc) - 7),
                    To = (byte)LSB(lpc),
                    //Eval = (Piece(LSB(capturele))<<4)|(KING-PAWN);
                };

            /* Generate pawns promotions */
            if ((pawns & 0x00FF000000000000UL) != 0)
            {
                /* promotions with left capture */
                for (ulong promo = (pawns << 9) & 0xFE00000000000000UL & opposing; promo != 0; promo = ClearLSB(promo))
                {
                    for (TPieceType piece = TPieceType.QUEEN; piece >= TPieceType.KNIGHT; piece--)
                        moves[index++] = new TMove
                        {
                            MoveType = TPieceType.PAWN | TPieceType.PROMO | TPieceType.CAPTURE,
                            From = (byte)(LSB(promo) - 9),
                            To = (byte)LSB(promo),
                            Promotion = piece
                            //Eval = (piece<<4)|(KING-PAWN);
                        };
                }

                /* promotions with right capture */
                for (ulong promo = (pawns << 7) & 0x7F00000000000000UL & opposing; promo != 0; promo = ClearLSB(promo))
                {
                    for (TPieceType piece = TPieceType.QUEEN; piece >= TPieceType.KNIGHT; piece--)
                        moves[index++] = new TMove
                        {
                            MoveType = TPieceType.PAWN | TPieceType.PROMO | TPieceType.CAPTURE,
                            From = (byte)(LSB(promo) - 7),
                            To = (byte)LSB(promo),
                            Promotion = piece
                            //Eval = (piece<<4)|(KING-PAWN);
                        };
                }
                /* no capture promotions */
                for (ulong promo = ((pawns << 8) & ~occupation) & 0xFF00000000000000UL;
                    promo != 0;
                    promo = ClearLSB(promo))
                {
                    for (TPieceType piece = TPieceType.QUEEN; piece >= TPieceType.KNIGHT; piece--)
                        moves[index++] = new TMove
                        {
                            MoveType = TPieceType.PAWN | TPieceType.PROMO,
                            From = (byte)(LSB(promo) - 8),
                            To = (byte)LSB(promo),
                            Promotion = piece
                            //Eval = (piece<<4)|(KING-PAWN);
                        };
                }
            }

            if (EnPass() != 8)
            {
                for (ulong enpassant = pawns & EnPassant[EnPass()]; enpassant != 0; enpassant = ClearLSB((enpassant)))
                    moves[index++] = new TMove
                    {
                        MoveType = TPieceType.PAWN | TPieceType.EP | TPieceType.PROMO,
                        From = (byte)LSB(enpassant),
                        To = (byte)(40 + EnPass())
                        //Eval = (PAWN<<4)|(KING-PAWN);
                    };
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Make(ref TMove move)
        {
            Position = Game[++iPosition].Copy(Position);
            ulong part = 1UL << move.From;
            ulong dest = 1UL << move.To;
            switch (move.MoveType & TPieceType.PIECE_MASK)
            {
                case TPieceType.PAWN:
                    if ((move.MoveType & TPieceType.EP) != 0)
                    { /* EnPassant */
                        Position.PM ^= part | dest;
                        Position.P0 ^= part | dest;
                        Position.P0 ^= dest >> 8; //delete the captured pawn
                        Position.EnPassant = 8;
                    }
                    else
                    {
                        //TODO: move.IsCapture
                        if ((move.MoveType & TPieceType.CAPTURE) != 0)
                        {
                            /* Delete the captured piece */
                            Position.P0 &= ~dest;
                            Position.P1 &= ~dest;
                            Position.P2 &= ~dest;
                        }

                        if ((move.MoveType & TPieceType.PROMO) != 0)
                        {
                            Position.PM ^= part | dest;
                            Position.P0 ^= part;
                            Position.P0 |= (ulong)((int)move.Promotion & 1) << move.To;
                            Position.P1 |= (ulong)(((int)move.Promotion >> 1) & 1) << move.To;
                            Position.P2 |= (ulong)((int)move.Promotion >> 2) << move.To;
                            Position.EnPassant = 8; /* clear enpassant */
                        }
                        else /* capture or push */
                        {
                            Position.PM ^= part | dest;
                            Position.P0 ^= part | dest;
                            Position.EnPassant = 8; /* clear enpassant */

                            if (move.To == move.From + 16 && (EnPassantM[move.To & 0x07] & Pawns() & Opposing()) != 0)
                                Position.EnPassant = (byte)(move.To & 0x07); /* save enpassant column */
                        }

                        if ((move.MoveType & TPieceType.CAPTURE) != 0)
                        {
                            if (move.To == 63) ResetCastleSO(); /* captured the opponent king side rook */
                            else if (move.To == 56) ResetCastleLO(); /* captured the opponent quuen side rook */
                        }
                    }
                    ChangeSide();
                    break;
                case TPieceType.KNIGHT:
                case TPieceType.BISHOP:
                case TPieceType.ROOK:
                case TPieceType.QUEEN:
                    if ((move.MoveType & TPieceType.CAPTURE) != 0)
                    {
                        /* Delete the captured piece */
                        Position.P0 &= ~dest;
                        Position.P1 &= ~dest;
                        Position.P2 &= ~dest;
                    }
                    Position.PM ^= part | dest;
                    //TODO: handle N, B, R & Q seperately?
                    Position.P0 ^= (((int)move.MoveType & 1) != 0) ? part | dest : 0;
                    Position.P1 ^= (((int)move.MoveType & 2) != 0) ? part | dest : 0;
                    Position.P2 ^= (((int)move.MoveType & 4) != 0) ? part | dest : 0;
                    Position.EnPassant = 8;
                    if ((move.MoveType & TPieceType.PIECE_MASK) == TPieceType.ROOK)
                    {
                        if (move.From == 7) 
                            ResetCastleSM(); //king side rook moved
                        else if (move.From == 0) 
                            ResetCastleLM(); // queen side rook moved
                    }
                    if ((move.MoveType & TPieceType.CAPTURE) != 0)
                    {
                        if (move.To == 63) ResetCastleSO(); /* captured the opponent king side rook */
                        else if (move.To == 56) ResetCastleLO(); /* captured the opponent quuen side rook */
                    }
                    ChangeSide();
                    break;
                case TPieceType.KING:
                    if ((move.MoveType & TPieceType.CAPTURE) != 0)
                    {
                        /* Delete the captured piece */
                        Position.P0 &= ~dest;
                        Position.P1 &= ~dest;
                        Position.P2 &= ~dest;
                    }
                    Position.PM ^= part | dest;
                    Position.P1 ^= part | dest;
                    Position.P2 ^= part | dest;
                    ResetCastleSM(); /* update the castle rights */
                    ResetCastleLM();
                    Position.EnPassant = 8;
                    if ((move.MoveType & TPieceType.CAPTURE) != 0)
                    {
                        if (move.To == 63) 
                            ResetCastleSO(); /* captured the opponent king side rook */
                        else if (move.To == 56) 
                            ResetCastleLO(); /* captured the opponent quuen side rook */
                    }
                    else if ((move.MoveType & TPieceType.CASTLE) != 0)
                    {
                        if (move.To == 6)
                        {
                            Position.PM ^= 0x00000000000000A0UL;
                            Position.P2 ^= 0x00000000000000A0UL;
                        } /* short castling */
                        else
                        {
                            Position.PM ^= 0x0000000000000009UL;
                            Position.P2 ^= 0x0000000000000009UL;
                        } /* long castling */
                    }
                    ChangeSide();
                    break;
            }
        }

        private static void LoadPosition(string fen)
        {
            /* Clear the board */
            ref TBoard pos = ref Position;
            pos.P0 = pos.P1 = pos.P2 = pos.PM = 0;
            pos.EnPassant = 8;
            pos.STM = WHITE;
            pos.CastleFlags = 0;

            /* translate the fen to the relative position */
            byte pieceside = WHITE;
            ulong piece = (ulong)TPieceType.PAWN;
            byte sidetomove = WHITE;
            int square = 0;
            int cursor;
            for (cursor = 0; fen[cursor] != ' '; cursor++)
            {
                char cur = fen[cursor];
                if (cur >= '1' && cur <= '8')
                    square += cur - '0';
                else if (cur != '/')
                {
                    int bit = OppSq(square);
                    if (cur == 'p') { piece = (ulong)TPieceType.PAWN; pieceside = BLACK; }
                    else if (cur == 'n') { piece = (ulong)TPieceType.KNIGHT; pieceside = BLACK; }
                    else if (cur == 'b') { piece = (ulong)TPieceType.BISHOP; pieceside = BLACK; }
                    else if (cur == 'r') { piece = (ulong)TPieceType.ROOK; pieceside = BLACK; }
                    else if (cur == 'q') { piece = (ulong)TPieceType.QUEEN; pieceside = BLACK; }
                    else if (cur == 'k') { piece = (ulong)TPieceType.KING; pieceside = BLACK; }
                    else if (cur == 'P') { piece = (ulong)TPieceType.PAWN; pieceside = WHITE; }
                    else if (cur == 'N') { piece = (ulong)TPieceType.KNIGHT; pieceside = WHITE; }
                    else if (cur == 'B') { piece = (ulong)TPieceType.BISHOP; pieceside = WHITE; }
                    else if (cur == 'R') { piece = (ulong)TPieceType.ROOK; pieceside = WHITE; }
                    else if (cur == 'Q') { piece = (ulong)TPieceType.QUEEN; pieceside = WHITE; }
                    else if (cur == 'K') { piece = (ulong)TPieceType.KING; pieceside = WHITE; }
                    pos.P0 |= (piece & 1) << bit; //001
                    pos.P1 |= ((piece >> 1) & 1) << bit; //010
                    pos.P2 |= (piece >> 2) << bit; //100
                    if (pieceside == WHITE)
                    {
                        pos.PM |= (1UL << bit);
                        piece |= BLACK;
                    }
                    square++;
                }
            }

            cursor++; /* read the side to move  */
            if (fen[cursor] == 'w')
                sidetomove = WHITE;
            else if (fen[cursor] == 'b')
                sidetomove = BLACK;
            cursor += 2;
            if (fen[cursor] != '-') /* read the castle rights */
            {
                for (; fen[cursor] != ' '; cursor++)
                {
                    char cur = fen[cursor];
                    if (cur == 'K') pos.CastleFlags |= 0x02;
                    else if (cur == 'Q') pos.CastleFlags |= 0x01;
                    else if (cur == 'k') pos.CastleFlags |= 0x20;
                    else if (cur == 'q') pos.CastleFlags |= 0x10;
                }
                cursor++;
            }
            else cursor += 2;
            if (fen[cursor] != '-') /* read the enpassant column */
            {
                pos.EnPassant = (byte)(fen[cursor] - 'a');
                //cursor++;
            }
            if (sidetomove == BLACK)
                ChangeSide();
        }

        private static long Perft(int depth)
        {
            long total = 0;
            int index = 0;
            var moveList = MovesLists[depth];
            GenerateCapture(moveList, ref index);
            GenerateQuiets(moveList, ref index);
            for (int i = 0; i < index; i++)
            {
                if (Illegal(ref moveList[i]))
                    continue;
                if (depth > 1)
                {
                    Make(ref moveList[i]);
                    total += Perft(depth - 1);
                    Position = Game[--iPosition];
                }
                else
                    total++;
            }
            return total;
        }

        struct PerftResult
        {
            public double Duration;
            public long Nodes;
            
            public PerftResult(double t, long n)
            {
                Duration = t;
                Nodes = n;
            }

            public static PerftResult operator +(PerftResult a, PerftResult b) => new(a.Duration + b.Duration, a.Nodes + b.Nodes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PerftResult TestPerft(string fen, int depth, int expectedResult)
        {
            LoadPosition(fen);
            //PrintPosition(Game[Position]);
            long t0 = Stopwatch.GetTimestamp();
            long count = Perft(depth);
            long t1 = Stopwatch.GetTimestamp();
            double dt = (t1 - t0) / (double)Stopwatch.Frequency;
            double ms = (1000 * dt);
            if (expectedResult != count)
            {
                Console.WriteLine($"ERROR in Perft({fen}, {depth})");
                Console.WriteLine($"Computed result: {count}");
                Console.WriteLine($"Expected result: {expectedResult}");
            }
            else
                Console.WriteLine($"OK! {(int)ms}ms, {(int)(count / ms)}K NPS");
            return new PerftResult(dt, count);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("QBB Perft in C#");
            Console.WriteLine("https://github.com/lithander/QBB-Perft/tree/v1.4");
            Console.WriteLine();
            PerftResult accu = TestPerft("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 6, 119060324); //Start Position
            accu += TestPerft("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 5, 193690690);
            accu += TestPerft("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 7, 178633661);
            accu += TestPerft("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 6, 706045033);
            accu += TestPerft("rnbqkb1r/pp1p1ppp/2p5/4P3/2B5/8/PPP1NnPP/RNBQK2R w KQkq - 0 6", 3, 53392);
            accu += TestPerft("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 5, 164075551);

            Console.WriteLine();
            Console.WriteLine($"Total: {accu.Nodes} Nodes, {(int)(1000 * accu.Duration)}ms, {(int)(accu.Nodes / accu.Duration / 1000)}K NPS");
            Console.WriteLine("Press any key to quit");//stop command prompt from closing automatically on windows
            Console.ReadKey();
        }

        //**** Debug Helpers not in the original C code

        private static void PrintPosition(TBoard pos)
        {
            PrintBB(pos.PM, "PM");
            PrintBB(pos.P0, "P0");
            PrintBB(pos.P1, "P1");
            PrintBB(pos.P2, "P2");
            Console.WriteLine("- - - -");
            PrintBB(Pawns(), "Pawns");
            PrintBB(Knights(), "Knights");
            PrintBB(Bishops(), "Bishops");
            PrintBB(Rooks(), "Roosk");
            PrintBB(Queens(), "Queens");
            PrintBB(Kings(), "Kings");

            Console.WriteLine($"CastleFlags: {pos.CastleFlags}");  /* ..sl..SL  short long opponent SHORT LONG side to move */
            Console.WriteLine($"EnPassant column: {pos.EnPassant} (8 if not set)");
            Console.WriteLine($"SideToMove: {pos.STM}"); /* side to move */
            Console.WriteLine();
        }

        static void PrintBB(ulong bb, string label)
        {
            if (label != null)
                Console.WriteLine(label);
            Console.WriteLine(Convert.ToString((long)bb, 16).PadLeft(16, '0'));
            Console.WriteLine("----------------");
            byte[] bbBytes = BitConverter.GetBytes(bb);
            Array.Reverse(bbBytes);
            foreach (byte bbByte in bbBytes)
            {
                string line = Convert.ToString(bbByte, 2).PadLeft(8, '0');
                line = line.Replace('1', 'X');
                line = line.Replace('0', '.');
                var chars = line.ToCharArray();
                Array.Reverse(chars);
                Console.WriteLine(string.Join(' ', chars));
            }
            Console.WriteLine();
        }

        private static long Divide(int depth)
        {
            long total = 0;
            int index = 0;
            var moveList = MovesLists[depth];
            GenerateCapture(moveList, ref index);
            GenerateQuiets(moveList, ref index);
            for (int i = 0; i < index; i++)
            {
                if (Illegal(ref moveList[i]))
                    continue;
        
                long nodes = 1;
                if (depth > 1)
                {
                    Make(ref moveList[i]);
                    nodes = Perft(depth - 1);
                    Position = Game[--iPosition];
                }
                total += nodes;
                Console.WriteLine($"  {MoveToStr(moveList[i], Position.STM)}:    {nodes:N0}");
            }

            return total;
        }
    }
}