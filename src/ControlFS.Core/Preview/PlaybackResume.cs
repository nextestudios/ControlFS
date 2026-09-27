using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ControlFS.Core.Preview;

/// <summary>Regras de "continuar de onde parou" (#170): o que conta como parcialmente assistido e a chave de cada arquivo.</summary>
public static class PlaybackResume
{
    /// <summary>Menos que isto do começo ou do fim não é "parcialmente assistido": não pergunta nem guarda.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    /// <summary>Posições guardadas no máximo (as mais antigas saem primeiro).</summary>
    public const int MaxEntries = 500;

    /// <summary>Chave do arquivo: caminho completo (sem diferenciar maiúsculas), tamanho e data de modificação.</summary>
    public static string Key(string fullPath, long length, DateTime lastWriteUtc)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{fullPath.ToUpperInvariant()}|{length}|{lastWriteUtc.Ticks}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Vale guardar/oferecer esta posição? Nem no começo, nem perto do fim (aí o vídeo foi visto).</summary>
    public static bool IsPartial(TimeSpan position, TimeSpan duration) =>
        position >= Margin && (duration <= TimeSpan.Zero || position <= duration - Margin);
}
