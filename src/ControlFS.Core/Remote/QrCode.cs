using System.Text;

namespace ControlFS.Core.Remote;

/// <summary>
/// Codificador mínimo de QR Code (ISO/IEC 18004) para o endereço de pareamento do celular (#223): modo byte, correção de
/// erros nível M, versões 1–10 (até 213 bytes), máscara escolhida pela penalidade da norma. Segue a estrutura do
/// gerador de referência de Project Nayuki (MIT), sem dependências. Só gera a matriz; a tela desenha os módulos.
/// </summary>
public static class QrCode
{
    public const int MaxVersion = 10;

    // Nível M, índice = versão (0 não é usado).
    private static readonly int[] EccCodewordsPerBlock = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] ErrorCorrectionBlocks = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];

    /// <summary>Formato do nível M nos bits de formato (L=1, M=0, Q=3, H=2).</summary>
    private const int FormatBitsM = 0;

    /// <summary>Matriz [linha, coluna]; true = módulo escuro. Sem a zona de silêncio (a tela acrescenta 4 módulos).</summary>
    /// <exception cref="ArgumentException">Texto longo demais para a versão 10.</exception>
    public static bool[,] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var data = Encoding.UTF8.GetBytes(text);
        var version = 1;
        for (; version <= MaxVersion; version++)
            if (DataCodewords(version) * 8 >= 4 + CountBits(version) + (data.Length * 8)) break;
        if (version > MaxVersion) throw new ArgumentException("Texto longo demais para o QR Code.", nameof(text));

        var capacityBits = DataCodewords(version) * 8;
        var bits = new List<bool>(capacityBits);
        Append(bits, 0b0100, 4); // modo byte
        Append(bits, data.Length, CountBits(version));
        foreach (var b in data) Append(bits, b, 8);
        Append(bits, 0, Math.Min(4, capacityBits - bits.Count)); // terminador
        Append(bits, 0, (8 - (bits.Count % 8)) % 8);
        for (var pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);

        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        return new Matrix(version).Build(AddEccAndInterleave(codewords, version));
    }

    private static int CountBits(int version) => version <= 9 ? 8 : 16;

    private static void Append(List<bool> bits, int value, int length)
    {
        for (var i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    private static int RawDataModules(int version)
    {
        var result = ((16 * version) + 128) * version + 64;
        if (version >= 2)
        {
            var align = (version / 7) + 2;
            result -= ((25 * align) - 10) * align - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    private static int DataCodewords(int version) => (RawDataModules(version) / 8) - (EccCodewordsPerBlock[version] * ErrorCorrectionBlocks[version]);

    private static byte[] AddEccAndInterleave(byte[] data, int version)
    {
        var blocks = ErrorCorrectionBlocks[version];
        var eccLength = EccCodewordsPerBlock[version];
        var raw = RawDataModules(version) / 8;
        var shortBlocks = blocks - (raw % blocks);
        var shortLength = raw / blocks;
        var divisor = ReedSolomonDivisor(eccLength);
        var all = new List<byte[]>(blocks);
        for (int i = 0, k = 0; i < blocks; i++)
        {
            var length = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
            var chunk = data.AsSpan(k, length).ToArray();
            k += length;
            var ecc = ReedSolomonRemainder(chunk, divisor);
            var block = new byte[shortLength + 1];
            chunk.CopyTo(block, 0);
            ecc.CopyTo(block, block.Length - eccLength); // blocos curtos deixam um byte vago antes da correção
            all.Add(block);
        }
        var result = new List<byte>(raw);
        for (var i = 0; i < all[0].Length; i++)
            for (var j = 0; j < all.Count; j++)
                if (i != shortLength - eccLength || j >= shortBlocks) result.Add(all[j][i]);
        return [.. result];
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    private static byte Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private sealed class Matrix(int version)
    {
        private readonly int _size = (version * 4) + 17;
        private readonly bool[,] _modules = new bool[(version * 4) + 17, (version * 4) + 17];
        private readonly bool[,] _function = new bool[(version * 4) + 17, (version * 4) + 17];

        public bool[,] Build(byte[] codewords)
        {
            DrawFunctionPatterns();
            DrawCodewords(codewords);
            var best = 0;
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                var penalty = Penalty();
                if (penalty < bestPenalty)
                {
                    best = mask;
                    bestPenalty = penalty;
                }
                ApplyMask(mask); // desfaz (XOR)
            }
            ApplyMask(best);
            DrawFormatBits(best);
            return _modules;
        }

        private void Set(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _function[y, x] = true;
        }

        private void DrawFunctionPatterns()
        {
            for (var i = 0; i < _size; i++)
            {
                Set(6, i, i % 2 == 0);
                Set(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(_size - 4, 3);
            DrawFinder(3, _size - 4);
            var positions = AlignmentPositions();
            var last = positions.Length - 1;
            for (var i = 0; i < positions.Length; i++)
                for (var j = 0; j < positions.Length; j++)
                    if (!((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0))) DrawAlignment(positions[i], positions[j]);
            DrawFormatBits(0); // reserva as áreas; o valor real vem depois da máscara
            DrawVersion();
        }

        private void DrawFinder(int x, int y)
        {
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < _size && yy >= 0 && yy < _size) Set(xx, yy, distance != 2 && distance != 4);
                }
        }

        private void DrawAlignment(int x, int y)
        {
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++) Set(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private int[] AlignmentPositions()
        {
            if (version == 1) return [];
            var count = (version / 7) + 2;
            var step = (int)Math.Ceiling(((version * 4) + 4) / (double)((count * 2) - 2)) * 2;
            var result = new List<int> { 6 };
            for (var pos = _size - 7; result.Count < count; pos -= step) result.Insert(1, pos);
            return [.. result];
        }

        private void DrawFormatBits(int mask)
        {
            var data = (FormatBitsM << 3) | mask;
            var rem = data;
            for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            var bits = ((data << 10) | rem) ^ 0x5412;
            for (var i = 0; i <= 5; i++) Set(8, i, Bit(bits, i));
            Set(8, 7, Bit(bits, 6));
            Set(8, 8, Bit(bits, 7));
            Set(7, 8, Bit(bits, 8));
            for (var i = 9; i < 15; i++) Set(14 - i, 8, Bit(bits, i));
            for (var i = 0; i < 8; i++) Set(_size - 1 - i, 8, Bit(bits, i));
            for (var i = 8; i < 15; i++) Set(8, _size - 15 + i, Bit(bits, i));
            Set(8, _size - 8, true); // módulo sempre escuro
        }

        private void DrawVersion()
        {
            if (version < 7) return;
            var rem = version;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = Bit(bits, i);
                int a = _size - 11 + (i % 3), b = i / 3;
                Set(a, b, bit);
                Set(b, a, bit);
            }
        }

        private void DrawCodewords(byte[] data)
        {
            var i = 0;
            for (var right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (var vert = 0; vert < _size; vert++)
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? _size - 1 - vert : vert;
                        if (_function[y, x] || i >= data.Length * 8) continue;
                        _modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
            }
        }

        private void ApplyMask(int mask)
        {
            for (var y = 0; y < _size; y++)
                for (var x = 0; x < _size; x++)
                {
                    if (_function[y, x]) continue;
                    var invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => ((x / 3) + (y / 2)) % 2 == 0,
                        5 => (x * y % 2) + (x * y % 3) == 0,
                        6 => ((x * y % 2) + (x * y % 3)) % 2 == 0,
                        _ => (((x + y) % 2) + (x * y % 3)) % 2 == 0,
                    };
                    if (invert) _modules[y, x] = !_modules[y, x];
                }
        }

        /// <summary>Penalidade da norma (regras 1–4): a máscara com menor valor é a mais fácil de ler.</summary>
        private int Penalty()
        {
            var penalty = 0;
            for (var line = 0; line < _size; line++)
            {
                penalty += RunPenalty(i => _modules[line, i]);
                penalty += RunPenalty(i => _modules[i, line]);
                penalty += FinderLikePenalty(i => _modules[line, i]);
                penalty += FinderLikePenalty(i => _modules[i, line]);
            }
            for (var y = 0; y < _size - 1; y++)
                for (var x = 0; x < _size - 1; x++)
                {
                    var c = _modules[y, x];
                    if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1]) penalty += 3;
                }
            var dark = 0;
            foreach (var module in _modules)
                if (module) dark++;
            var total = _size * _size;
            var k = (int)Math.Ceiling(Math.Abs((dark * 20) - (total * 10)) / (double)total) - 1;
            return penalty + (Math.Max(0, k) * 10);
        }

        private int RunPenalty(Func<int, bool> at)
        {
            var penalty = 0;
            var run = 1;
            for (var i = 1; i <= _size; i++)
            {
                if (i < _size && at(i) == at(i - 1))
                {
                    run++;
                    continue;
                }
                if (run >= 5) penalty += 3 + (run - 5);
                run = 1;
            }
            return penalty;
        }

        private static readonly bool[] FinderLike = [true, false, true, true, true, false, true];

        private int FinderLikePenalty(Func<int, bool> at)
        {
            var penalty = 0;
            for (var i = 0; i + FinderLike.Length <= _size; i++)
            {
                var match = true;
                for (var j = 0; j < FinderLike.Length && match; j++) match = at(i + j) == FinderLike[j];
                if (!match) continue;
                if (LightRun(at, i - 4, i) || LightRun(at, i + FinderLike.Length, i + FinderLike.Length + 4)) penalty += 40;
            }
            return penalty;
        }

        /// <summary>Quatro módulos claros (fora da matriz conta como claro: a zona de silêncio).</summary>
        private bool LightRun(Func<int, bool> at, int from, int to)
        {
            for (var i = from; i < to; i++)
                if (i >= 0 && i < _size && at(i)) return false;
            return true;
        }

        private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;
    }
}
