using System.Reflection;

namespace ControlFS.Core.Audio;

/// <summary>
/// Os sons do controle (#276, #287): quatro toques curtos do pacote "Interface Sounds" de Kenney (licença CC0, domínio público,
/// redistribuição livre; origem e licença em assets/sounds e THIRD_PARTY_NOTICES.md), já aparados, sem estalo e com a mesma energia
/// (tools/make-sounds.py). Ficam embutidos no assembly; aqui só se aplica o volume do usuário, sempre para baixo a partir do nível
/// mestre (curva perceptiva: 25% soa bem mais baixo que 100%). A interface do PS5 é só referência de sensação: nada da Sony foi usado.
/// </summary>
public static class SoundBank
{
    public const int SampleRate = 44100;
    private const int HeaderBytes = 44;

    private static readonly Dictionary<SoundCue, byte[]> Masters = [];

    private static string FileFor(SoundCue cue) => cue switch
    {
        SoundCue.Move => "move",
        SoundCue.Select => "select",
        SoundCue.Confirm => "confirm",
        _ => "back",
    };

    /// <summary>O WAV do som (PCM 16 bits, mono, 44,1 kHz) no volume de 1 a 100.</summary>
    public static byte[] Render(SoundCue cue, int volume)
    {
        var master = Master(cue);
        var wav = (byte[])master.Clone();
        var gain = Math.Pow(Math.Clamp(volume, 1, 100) / 100.0, 1.5);
        for (var i = HeaderBytes; i + 1 < wav.Length; i += 2)
        {
            var sample = (short)Math.Round(BitConverter.ToInt16(master, i) * gain);
            wav[i] = (byte)(sample & 0xFF);
            wav[i + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return wav;
    }

    private static byte[] Master(SoundCue cue)
    {
        lock (Masters)
        {
            if (Masters.TryGetValue(cue, out var cached)) return cached;
            var name = $"sounds/{FileFor(cue)}.wav";
            using var stream = typeof(SoundBank).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Som embutido ausente: {name}");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length <= HeaderBytes || bytes[36] != 'd' || bytes[37] != 'a' || bytes[38] != 't' || bytes[39] != 'a')
                throw new InvalidOperationException($"Som embutido inválido (esperado WAV PCM de 44 bytes de cabeçalho): {name}");
            return Masters[cue] = bytes;
        }
    }
}
