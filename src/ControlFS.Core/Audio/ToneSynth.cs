namespace ControlFS.Core.Audio;

/// <summary>
/// Os sons do controle, sintetizados aqui (#276): tons senoidais curtos com ataque e queda suaves, sem nenhum arquivo de
/// terceiros (os sons da interface do PS5 são só uma referência de sensação, não foram copiados). Saem como WAV PCM de
/// 16 bits, mono, 44,1 kHz, prontos para tocar da memória.
/// </summary>
public static class ToneSynth
{
    public const int SampleRate = 44100;

    /// <summary>Cada som: notas (frequência em Hz, duração em ms), sem pausa entre elas.</summary>
    private static (double Hz, double Ms)[] Notes(SoundCue cue) => cue switch
    {
        SoundCue.Move => [(1320, 28)],
        SoundCue.Select => [(880, 55)],
        SoundCue.Confirm => [(660, 55), (990, 85)],   // sobe
        SoundCue.Back => [(660, 55), (440, 85)],      // desce
        _ => [(880, 40)],
    };

    /// <summary>WAV do som no volume de 1 a 100 (o pico fica em até ~45% do máximo: discreto mesmo no volume 100).</summary>
    public static byte[] Render(SoundCue cue, int volume)
    {
        var gain = 0.45 * Math.Clamp(volume, 1, 100) / 100.0;
        var samples = new List<short>();
        foreach (var (hz, ms) in Notes(cue))
        {
            var count = (int)(SampleRate * ms / 1000.0);
            var fade = Math.Min(count / 2, (int)(SampleRate * 0.006)); // 6 ms de entrada e de saída: sem estalo
            for (var i = 0; i < count; i++)
            {
                var envelope = i < fade ? (double)i / fade : i >= count - fade ? (double)(count - 1 - i) / fade : 1.0;
                // Queda suave ao longo do som: o fim é mais fraco que o começo, como um toque.
                var decay = 1.0 - (0.6 * i / count);
                samples.Add((short)Math.Round(Math.Sin(2 * Math.PI * hz * i / SampleRate) * envelope * decay * gain * short.MaxValue));
            }
        }
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        var dataBytes = samples.Count * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);   // PCM
        writer.Write((short)1);   // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return memory.ToArray();
    }
}
