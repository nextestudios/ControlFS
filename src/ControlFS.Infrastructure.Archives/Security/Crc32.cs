namespace ControlFS.Infrastructure.Archives.Security;

/// <summary>CRC-32 (IEEE 802.3, polinômio refletido 0xEDB88320), usado para verificar entradas ZIP.</summary>
internal sealed class Crc32
{
    private static readonly uint[] Table = BuildTable();
    private uint _state = 0xFFFFFFFFu;

    public uint Value => ~_state;

    public void Append(ReadOnlySpan<byte> data)
    {
        var crc = _state;
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        _state = crc;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var c = new Crc32();
        c.Append(data);
        return c.Value;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
