using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ControlFS.Infrastructure.Remote;

/// <summary>
/// Chaves e quadros do canal com o celular (#223). Uma chave aleatória de 32 bytes por pareamento (no fragmento do QR
/// Code) gera, por HKDF-SHA256, uma chave para cada sentido; cada quadro é AES-256-GCM com um contador de 64 bits como
/// nonce, que precisa ser exatamente o próximo (repetido, fora de ordem ou adulterado: a conexão cai).
/// A página do celular faz o mesmo com @noble/ciphers e @noble/hashes (ver Companion/phone.html).
/// </summary>
internal static class PhoneCrypto
{
    public const int KeyLength = 32;
    public const int SessionIdLength = 16;
    public const int CounterLength = 8;
    public const int TagLength = 16;
    public const int NonceLength = 12;

    /// <summary>Menor quadro possível: contador + etiqueta (texto vazio não é mensagem válida, mas o tamanho é).</summary>
    public const int Overhead = CounterLength + TagLength;

    /// <summary>Dados associados de todo quadro: versão do protocolo (mudar o formato muda isto).</summary>
    public static ReadOnlySpan<byte> AssociatedData => "ControlFS/1"u8;

    public static (byte[] PhoneToPc, byte[] PcToPhone) DeriveKeys(ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> sessionId) =>
        (Derive(sessionKey, sessionId, "ControlFS phone v1 phone-to-pc", KeyLength), Derive(sessionKey, sessionId, "ControlFS phone v1 pc-to-phone", KeyLength));

    /// <summary>
    /// Código de 6 dígitos que o PC e o celular mostram: depende da chave e do número aleatório que o celular mandou ao
    /// conectar, então só coincide no aparelho que realmente abriu esta conexão.
    /// </summary>
    public static string VerificationCode(ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> phoneNonce)
    {
        var bytes = Derive(sessionKey, phoneNonce, "ControlFS phone v1 code", 4);
        var value = BinaryPrimitives.ReadUInt32BigEndian(bytes) % 1_000_000;
        var digits = value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        return digits[..3] + " " + digits[3..];
    }

    private static byte[] Derive(ReadOnlySpan<byte> key, ReadOnlySpan<byte> salt, string info, int length)
    {
        var output = new byte[length];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, output, salt, Encoding.ASCII.GetBytes(info));
        return output;
    }

    internal static void Nonce(ulong counter, Span<byte> nonce)
    {
        nonce[..4].Clear();
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], counter);
    }
}

/// <summary>Cifra os quadros de um sentido: contador 0, 1, 2… (nunca reutiliza um nonce com a mesma chave).</summary>
internal sealed class FrameSealer(byte[] key) : IDisposable
{
    private readonly AesGcm _aead = new(key, PhoneCrypto.TagLength);
    private ulong _next;

    /// <summary>contador (8 bytes, big-endian) ‖ texto cifrado ‖ etiqueta (16 bytes).</summary>
    public byte[] Seal(ReadOnlySpan<byte> plaintext)
    {
        var counter = _next++;
        var frame = new byte[PhoneCrypto.Overhead + plaintext.Length];
        BinaryPrimitives.WriteUInt64BigEndian(frame, counter);
        Span<byte> nonce = stackalloc byte[PhoneCrypto.NonceLength];
        PhoneCrypto.Nonce(counter, nonce);
        _aead.Encrypt(nonce, plaintext, frame.AsSpan(PhoneCrypto.CounterLength, plaintext.Length), frame.AsSpan(PhoneCrypto.CounterLength + plaintext.Length), PhoneCrypto.AssociatedData);
        return frame;
    }

    public void Dispose() => _aead.Dispose();
}

/// <summary>
/// Confere e decifra os quadros de um sentido. Aceita só o contador esperado; a primeira falha trava o objeto (a conexão
/// deve cair: não há segunda chance para quem adulterou ou repetiu um quadro).
/// </summary>
internal sealed class FrameOpener(byte[] key) : IDisposable
{
    private readonly AesGcm _aead = new(key, PhoneCrypto.TagLength);
    private ulong _expected;

    public bool Failed { get; private set; }

    public bool TryOpen(ReadOnlySpan<byte> frame, out byte[] plaintext)
    {
        plaintext = [];
        if (Failed || frame.Length < PhoneCrypto.Overhead) return Fail();
        var counter = BinaryPrimitives.ReadUInt64BigEndian(frame);
        if (counter != _expected) return Fail(); // repetido ou fora de ordem
        Span<byte> nonce = stackalloc byte[PhoneCrypto.NonceLength];
        PhoneCrypto.Nonce(counter, nonce);
        var length = frame.Length - PhoneCrypto.Overhead;
        var output = new byte[length];
        try
        {
            _aead.Decrypt(nonce, frame.Slice(PhoneCrypto.CounterLength, length), frame[(PhoneCrypto.CounterLength + length)..], output, PhoneCrypto.AssociatedData);
        }
        catch (CryptographicException)
        {
            return Fail();
        }
        _expected++;
        plaintext = output;
        return true;
    }

    private bool Fail()
    {
        Failed = true;
        return false;
    }

    public void Dispose() => _aead.Dispose();
}
