namespace ControlFS.Core.Audio;

/// <summary>
/// Os sons do controle (#276, #287): os mesmos toques do Console Mode (https://github.com/lippdev/consolemode, AGPL-3.0, da mesma
/// equipe; <c>UiSoundSynth</c>), montados em código, então o app não leva nenhum arquivo de áudio: toques curtos e macios (uma
/// senoide com uma oitava baixinha, ataque rápido e queda) em volume baixo, na linha de um menu de console. Mover, confirmar (sobe)
/// e voltar (desce) são os do Console Mode; marcar é um quarto toque no mesmo estilo. A interface do PS5 é só referência de
/// sensação. Saem como WAV PCM de 16 bits, mono, 44,1 kHz, prontos para tocar da memória.
/// </summary>
public static class SoundBank
{
    public const int SampleRate = 44100;

    /// <summary>Uma nota: vai de <c>From</c> a <c>To</c> Hz durante <c>Ms</c>.</summary>
    private readonly record struct Note(double From, double To, int Ms, double Gain);

    private static Note[] Notes(SoundCue cue) => cue switch
    {
        SoundCue.Move => [new(1150, 1000, 45, 0.30)],
        SoundCue.Select => [new(880, 940, 55, 0.30)],
        SoundCue.Confirm => [new(740, 740, 55, 0.32), new(1110, 1110, 95, 0.34)],
        _ => [new(820, 820, 50, 0.30), new(560, 540, 85, 0.30)],
    };

    /// <summary>
    /// O WAV do som no volume de 1 a 100. 50 ("médio") é o nível original do Console Mode; 100 é o dobro (nunca satura) e 25 a
    /// metade, em curva linear.
    /// </summary>
    public static byte[] Render(SoundCue cue, int volume)
    {
        var scale = Math.Clamp(volume, 1, 100) / 50.0;
        var samples = new List<float>();
        double phase = 0;
        foreach (var note in Notes(cue))
        {
            var count = SampleRate * note.Ms / 1000;
            var attack = SampleRate * 3 / 1000;
            var release = SampleRate * 6 / 1000;
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / count;
                var frequency = note.From + ((note.To - note.From) * t);
                phase += 2 * Math.PI * frequency / SampleRate;
                var tone = Math.Sin(phase) + (0.25 * Math.Sin(2 * phase));
                var envelope = Math.Exp(-3.2 * t)
                               * Math.Min(1.0, (double)i / attack)
                               * Math.Min(1.0, (double)(count - i) / release);
                samples.Add((float)(tone / 1.25 * note.Gain * envelope * scale));
            }
        }

        var dataLength = samples.Count * 2;
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);                 // cabeçalho PCM
        writer.Write((short)1);           // PCM
        writer.Write((short)1);           // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);     // bytes por segundo
        writer.Write((short)2);           // alinhamento do bloco
        writer.Write((short)16);          // bits por amostra
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }
}
