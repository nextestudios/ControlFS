using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ControlFS.UnitTests.Support;

/// <summary>
/// Gera ZIPs com criptografia WinZip AES (AE-1/AE-2, AES-128/192/256) seguindo a especificação pública da WinZip
/// (https://www.winzip.com/en/support/aes-encryption/): PBKDF2-HMAC-SHA1 com 1000 iterações, AES-CTR com contador
/// little-endian iniciado em 1 e código de autenticação HMAC-SHA1 de 10 bytes. O BCL não cria ZIP AES; o leitor
/// testado (SharpCompress) é independente deste gerador. Os sais são derivados do nome para o teste ser determinístico.
/// </summary>
public static class AesZipFixtures
{
    public const ushort AesMethod = 99;

    public sealed record Options(int AeVersion, int KeyBits, bool Deflate = true, bool CorruptPayload = false);

    public static string Create(string path, string password, Options options, params (string Name, byte[] Data)[] entries)
    {
        var strength = options.KeyBits switch { 128 => 1, 192 => 2, 256 => 3, _ => throw new ArgumentOutOfRangeException(nameof(options)) };
        var keyLength = options.KeyBits / 8;
        var saltLength = keyLength / 2;
        var method = (ushort)(options.Deflate ? 8 : 0);
        var central = new MemoryStream();
        using var file = File.Create(path);
        foreach (var (name, data) in entries)
        {
            var packed = options.Deflate ? Deflate(data) : data;
            var salt = SHA256.HashData(Encoding.UTF8.GetBytes(name))[..saltLength];
            var derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 1000, HashAlgorithmName.SHA1, keyLength * 2 + 2);
            var cipher = Ctr(derived[..keyLength], packed);
#pragma warning disable CA5350 // HMAC-SHA1 é exigido pelo formato WinZip AES; aqui só gera fixtures de teste
            var mac = HMACSHA1.HashData(derived[keyLength..(keyLength * 2)], cipher)[..10];
#pragma warning restore CA5350
            if (options.CorruptPayload && cipher.Length > 0) cipher[0] ^= 0x01; // depois do MAC: simula dados adulterados
            var payload = (uint)(saltLength + 2 + cipher.Length + 10);
            var crc = options.AeVersion == 1 ? Crc32(data) : 0u; // AE-2 não guarda CRC
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var extra = new byte[11];
            BinaryPrimitives.WriteUInt16LittleEndian(extra, 0x9901);
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), 7);
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(4), (ushort)options.AeVersion);
            extra[6] = (byte)'A';
            extra[7] = (byte)'E';
            extra[8] = (byte)strength;
            BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(9), method);

            var offset = (uint)file.Position;
            var header = Header(0x04034b50, nameBytes, extra, crc, payload, (uint)data.Length, offset, central: false);
            file.Write(header);
            file.Write(salt);
            file.Write(derived.AsSpan(keyLength * 2, 2)); // verificador de senha
            file.Write(cipher);
            file.Write(mac);
            central.Write(Header(0x02014b50, nameBytes, extra, crc, payload, (uint)data.Length, offset, central: true));
        }
        var centralOffset = (uint)file.Position;
        central.Position = 0;
        central.CopyTo(file);
        var end = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), (ushort)entries.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), (ushort)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), (uint)central.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), centralOffset);
        file.Write(end);
        return path;
    }

    private static byte[] Header(uint signature, byte[] name, byte[] extra, uint crc, uint compressed, uint size, uint offset, bool central)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(signature);
        if (central) w.Write((ushort)51); // versão que criou
        w.Write((ushort)51); // versão necessária (AES)
        w.Write((ushort)0x0801); // criptografado + nomes UTF-8
        w.Write(AesMethod);
        w.Write((ushort)0); // hora DOS
        w.Write((ushort)(((2024 - 1980) << 9) | (1 << 5) | 1)); // data DOS
        w.Write(crc);
        w.Write(compressed);
        w.Write(size);
        w.Write((ushort)name.Length);
        w.Write((ushort)extra.Length);
        if (central)
        {
            w.Write((ushort)0); // comentário
            w.Write((ushort)0); // disco
            w.Write((ushort)0); // atributos internos
            w.Write(0u); // atributos externos
            w.Write(offset);
        }
        w.Write(name);
        w.Write(extra);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data);
        return ms.ToArray();
    }

    private static byte[] Ctr(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var output = new byte[data.Length];
        var counter = new byte[16];
        for (var block = 0; block * 16 < data.Length; block++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(counter, (ulong)block + 1);
            var stream = aes.EncryptEcb(counter, PaddingMode.None);
            for (var i = block * 16; i < Math.Min(data.Length, block * 16 + 16); i++)
                output[i] = (byte)(data[i] ^ stream[i - block * 16]);
        }
        return output;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
