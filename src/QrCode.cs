using System;
using System.Collections.Generic;
using System.Drawing;

namespace JoeBrowser
{
    /// <summary>
    /// Minimal QR Code model encoder (byte mode, error correction level L, versions 1-10).
    /// Enough for the otpauth:// URIs an authenticator app scans during 2FA setup, with no
    /// external library: the app must stay a single self-contained executable.
    /// </summary>
    public sealed class QrCode
    {
        // Per version (index 0 = version 1), error correction level L:
        // total codewords PER BLOCK, EC codewords per block, blocks in group 1,
        // data codewords per block in group 1, blocks in group 2, data per block group 2.
        // For every version 1-9 at level L the blocks split evenly, so group 2 is empty.
        private static readonly int[,] L =
        {
            { 26, 7, 1, 19, 0, 0 },
            { 44, 10, 1, 34, 0, 0 },
            { 70, 15, 1, 55, 0, 0 },
            { 100, 20, 1, 80, 0, 0 },
            { 134, 26, 1, 108, 0, 0 },
            { 86, 18, 2, 68, 0, 0 },
            { 98, 20, 2, 78, 0, 0 },
            { 121, 24, 2, 97, 0, 0 },
            { 146, 30, 2, 116, 0, 0 }
        };

        private static readonly int[][] AlignmentGrid =
        {
            new int[] { },
            new[] { 6, 18 },
            new[] { 6, 22 },
            new[] { 6, 26 },
            new[] { 6, 30 },
            new[] { 6, 34 },
            new[] { 6, 22, 38 },
            new[] { 6, 24, 42 },
            new[] { 6, 26, 46 }
        };

        private readonly bool[][] _modules;
        private readonly bool[][] _isFunction;
        public readonly int Size;
        public readonly int Version;

        private QrCode(int version)
        {
            Version = version;
            Size = version * 4 + 17;
            _modules = new bool[Size][];
            _isFunction = new bool[Size][];
            for (int i = 0; i < Size; i++)
            {
                _modules[i] = new bool[Size];
                _isFunction[i] = new bool[Size];
            }
        }

        public bool this[int x, int y] { get { return _modules[y][x]; } }

        /// <summary>Encodes text, or returns null when it does not fit in version 9-L.</summary>
        public static QrCode Encode(string text)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(text ?? "");
            int version = 0;
            for (int v = 1; v <= 9; v++)
            {
                if (DataCapacity(v) >= data.Length) { version = v; break; }
            }
            if (version == 0) return null;
            return new QrCode(version).Build(data);
        }

        private static int DataCapacity(int version)
        {
            int row = version - 1;
            int total = L[row, 0], ecPerBlock = L[row, 1], g1 = L[row, 2], d1 = L[row, 3], g2 = L[row, 4], d2 = L[row, 5];
            int dataCodewords = g1 * d1 + g2 * d2;
            int ecCodewords = (total - dataCodewords);
            // 4 bit mode + 8..16 bit length depending on version
            int lengthBits = version <= 9 ? 8 : 16;
            int headerBits = 4 + lengthBits;
            int terminatorBits = 4;
            return (dataCodewords * 8 - headerBits - terminatorBits) / 8 - ecCodewords * 0;
        }

        private QrCode Build(byte[] data)
        {
            int row = Version - 1;
            int perBlock = L[row, 0], ecPerBlock = L[row, 1], g1 = L[row, 2], d1 = L[row, 3], g2 = L[row, 4], d2 = L[row, 5];
            int total = (g1 + g2) * perBlock;   // whole symbol, all blocks combined
            int dataCodewords = g1 * d1 + g2 * d2;

            DrawFunctionPatterns();

            // ---- message bit stream: mode(4) + length(8) + data + terminator + pad
            var bits = new List<bool>();
            AppendBits(bits, 4, 4);                       // byte mode
            AppendBits(bits, data.Length, 8);
            foreach (byte b in data) AppendBits(bits, b, 8);
            int capacityBits = dataCodewords * 8;
            AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
            while (bits.Count % 8 != 0 && bits.Count < capacityBits) bits.Add(false);
            var padBytes = new byte[] { 236, 17 };
            int padIndex = 0;
            while (bits.Count < capacityBits)
            {
                AppendBits(bits, padBytes[padIndex % 2], 8);
                padIndex++;
            }
            byte[] message = BitsToBytes(bits);

            // ---- split into blocks and compute error correction
            var blocks = new List<byte[]>();
            int offset = 0;
            for (int i = 0; i < g1; i++, offset += d1) blocks.Add(Slice(message, offset, d1));
            for (int i = 0; i < g2; i++, offset += d2) blocks.Add(Slice(message, offset, d2));
            var eccs = new List<byte[]>();
            foreach (byte[] block in blocks) eccs.Add(Rs(block, ecPerBlock));

            // ---- interleave
            var final = new List<byte>(total);
            int longestData = Math.Max(d1, d2);
            for (int i = 0; i < longestData; i++)
                foreach (byte[] block in blocks)
                    if (i < block.Length) final.Add(block[i]);
            for (int i = 0; i < ecPerBlock; i++)
                foreach (byte[] ecc in eccs)
                    final.Add(ecc[i]);

            // ---- draw data with mask 0 (checkerboard), then apply format info
            bool[][] applied = new bool[Size][];
            for (int i = 0; i < Size; i++) applied[i] = new bool[Size];
            int bitIndex = 0;
            bool upward = true;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!_isFunction[y][x] && bitIndex < final.Count * 8)
                        {
                            bool dark = ((final[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                            // mask 0: (x + y) % 2 == 0
                            if (((x + y) & 1) == 0) dark = !dark;
                            _modules[y][x] = dark;
                            applied[y][x] = true;
                            bitIndex++;
                        }
                    }
                }
                upward = !upward;
            }

            DrawFormatBits(0);
            return this;
        }

        private static void AppendBits(List<bool> bits, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private static byte[] BitsToBytes(List<bool> bits)
        {
            var result = new byte[bits.Count / 8];
            for (int i = 0; i < result.Length; i++)
                for (int b = 0; b < 8; b++)
                    if (bits[i * 8 + b]) result[i] |= (byte)(1 << (7 - b));
            return result;
        }

        private static byte[] Slice(byte[] src, int offset, int length)
        {
            var dst = new byte[length];
            Array.Copy(src, offset, dst, 0, length);
            return dst;
        }

        // ------------------------------------------------------------- Reed-Solomon

        private static byte[] Rs(byte[] data, int degree)
        {
            // Generator polynomial, computed over GF(2^8) with the QR primitive 0x11D.
            byte[] divisor = new byte[degree];
            divisor[degree - 1] = 1;   // start from x^0 coefficient layout (reversed)
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    divisor[j] = GMul(divisor[j], (byte)root);
                    if (j + 1 < degree) divisor[j] ^= divisor[j + 1];
                }
                root = GMulNoTable((byte)root, 2);
            }
            var result = new byte[degree];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, degree - 1);
                result[degree - 1] = 0;
                for (int j = 0; j < degree; j++)
                    result[j] ^= GMulNoTable((byte)factor, divisor[j]);
            }
            return result;
        }

        private static byte GMul(byte a, byte b) { return GMulNoTable(a, b); }

        private static byte GMulNoTable(byte a, byte b)
        {
            int r = 0;
            for (int i = 7; i >= 0; i--)
            {
                r = (r << 1) ^ ((r >> 7) * 0x11D);
                r ^= ((b >> i) & 1) * a;
            }
            return (byte)r;
        }

        // ------------------------------------------------------------- function patterns

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                // Timing pattern: alternating modules on row 6 and column 6, dark at even
                // coordinates. Reserved here and drawn in one pass.
                bool dark = (i & 1) == 0;
                _modules[i][6] = dark; SetFunction(6, i);
                _modules[6][i] = dark; SetFunction(i, 6);
            }
            DrawFinder(0, 0); DrawFinder(Size - 7, 0); DrawFinder(0, Size - 7);
            DrawAlignment();
            DrawFormatPlaceholders();
            DrawVersionInfo();
        }

        private void SetFunction(int x, int y) { _isFunction[y][x] = true; }

        private void DrawFinder(int cx, int cy)
        {
            for (int dy = -1; dy <= 7; dy++)
            {
                for (int dx = -1; dx <= 7; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= Size || y < 0 || y >= Size) continue;
                    int dist = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    // 7x7 dark border (dist 3), white ring (dist 2), 3x3 dark centre (dist <= 1);
                    // dist 4 is the one-module white separator around the whole square.
                    bool dark = dist != 2 && dist != 4;
                    _modules[y][x] = dark;
                    SetFunction(x, y);
                }
            }
        }

        private void DrawAlignment()
        {
            int[] centres = AlignmentGrid[Version - 1];
            foreach (int cy in centres)
                foreach (int cx in centres)
                {
                    // Skip the three corners - the finder patterns and separators live there.
                    if ((cx == 6 && cy == 6) ||
                        (cx == 6 && cy == centres[centres.Length - 1]) ||
                        (cx == centres[centres.Length - 1] && cy == 6)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x < 0 || x >= Size || y < 0 || y >= Size || _isFunction[y][x]) continue;
                            int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                            _modules[y][x] = dist != 1;
                            SetFunction(x, y);
                        }
                }
        }

        private void DrawFormatPlaceholders()
        {
            for (int i = 0; i <= 8; i++)
            {
                if (i != 6) { SetFunction(8, i); SetFunction(i, 8); }
            }
            SetFunction(8, 8);
            for (int i = 0; i < 8; i++)
            {
                SetFunction(Size - 1 - i, 8);
                SetFunction(8, Size - 1 - i);
            }
        }

        private void DrawFormatBits(int mask)
        {
            int data = (0x01 << 3) | mask;   // EC level L (01), then the 3 mask bits
            // Format string: 2 bits EC (L=01) + 3 bits mask, BCH(15,5) with mask 0x5412.
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;

            bool GetBit(int i) { return ((bits >> i) & 1) != 0; }

            // First copy. Coordinates follow the QR specification as (x, y); the module
            // array is indexed [y][x].
            for (int i = 0; i <= 5; i++) _modules[i][8] = GetBit(i);
            _modules[7][8] = GetBit(6);
            _modules[8][8] = GetBit(7);
            _modules[8][7] = GetBit(8);
            for (int i = 9; i < 15; i++) _modules[8][14 - i] = GetBit(i);

            // Second copy.
            for (int i = 0; i < 8; i++) _modules[8][Size - 1 - i] = GetBit(i);
            for (int i = 8; i < 15; i++) _modules[Size - 15 + i][8] = GetBit(i);
            /* The dark module sits at row Size-8, column 8 - bottom left of the alignment
               area. The old line wrote [8][Size-8], the top-right corner, where it stomped a
               format bit instead of adding the module: scanners refused the code. */
            _modules[Size - 8][8] = true;   // the always-dark module
        }

        private void DrawVersionInfo()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (Version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool dark = ((bits >> i) & 1) != 0;
                int a = Size - 11 + i % 3, b = i / 3;
                _modules[b][a] = dark;
                _modules[a][b] = dark;
                SetFunction(b, a); SetFunction(a, b);
            }
        }

        /// <summary>Renders the code as a quiet-zone bitmap with crisp squares.</summary>
        public Bitmap ToBitmap(int scale, int quietZone)
        {
            int dim = (Size + quietZone * 2) * scale;
            var bmp = new Bitmap(dim, dim, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (var brush = new SolidBrush(Color.Black))
                {
                    for (int y = 0; y < Size; y++)
                        for (int x = 0; x < Size; x++)
                            if (_modules[y][x])
                                g.FillRectangle(brush, (x + quietZone) * scale, (y + quietZone) * scale, scale, scale);
                }
            }
            return bmp;
        }
    }
}
