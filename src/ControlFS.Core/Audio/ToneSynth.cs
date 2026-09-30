namespace ControlFS.Core.Audio;

/// <summary>
/// Os sons do controle, sintetizados aqui (#276, #287): toques curtos de timbre "vidro" (fundamental com dois harmônicos
/// suaves), ataque arredondado e queda exponencial, sem nenhum arquivo de terceiros. A sensação discreta e limpa da interface
/// do PS5 é só referência de estilo; nada foi copiado. Saem como WAV PCM de 16 bits, mono, 44,1 kHz, prontos para tocar da memória.
/// Todos têm a mesma energia (RMS), então nenhum som "grita" mais que outro, e começam e terminam em silêncio (sem estalo).
/// </summary>
public static class ToneSynth
{
    public const int SampleRate = 44100;

    /// <summary>RMS de cada som no volume 100 (~ -21 dBFS): presente, mas nunca alto.</summary>
    private const double TargetRms = 0.09;

    /// <summary>Pico máximo (60% do fundo de escala) mesmo no volume 100: margem para o som nunca saturar.</summary>
    private const double PeakCap = 0.6;

    /// <summary>Uma nota: frequência, começo, duração total e constante de queda (tempo para cair a ~37%).</summary>
    private readonly record struct Note(double Hz, double StartMs, double Ms, double DecayMs);

    // Escala pentatônica maior (sem dissonância por mais que se repita): E5 659, G#5 831, B5 988, D6 1175.
    private static Note[] Notes(SoundCue cue) => cue switch
    {
        SoundCue.Move => [new(988, 0, 45, 11)],
        SoundCue.Select => [new(1319, 0, 70, 16)],
        SoundCue.Confirm => [new(659, 0, 80, 22), new(988, 55, 130, 34)],   // sobe
        SoundCue.Back => [new(988, 0, 80, 22), new(659, 55, 130, 34)],      // desce
        _ => [new(988, 0, 45, 11)],
    };

    /// <summary>WAV do som no volume de 1 a 100 (curva perceptiva: o volume baixo é mesmo baixo).</summary>
    public static byte[] Render(SoundCue cue, int volume)
    {
        var notes = Notes(cue);
        var length = (int)(SampleRate * notes.Max(n => n.StartMs + n.Ms) / 1000.0) + 1;
        var mix = new double[length];
        foreach (var note in notes)
        {
            var start = (int)(SampleRate * note.StartMs / 1000.0);
            var count = (int)(SampleRate * note.Ms / 1000.0);
            var attack = Math.Max(1, (int)(SampleRate * 0.004)); // 4 ms: sem estalo
            var release = Math.Max(1, (int)(SampleRate * 0.012)); // 12 ms finais até o silêncio exato
            for (var i = 0; i < count && start + i < length; i++)
            {
                var t = (double)i / SampleRate;
                var phase = 2 * Math.PI * note.Hz * t;
                var tone = Math.Sin(phase) + (0.22 * Math.Sin(2 * phase)) + (0.06 * Math.Sin(3 * phase));
                var envelope = Math.Exp(-(i * 1000.0 / SampleRate) / note.DecayMs);
                if (i < attack) envelope *= 0.5 - (0.5 * Math.Cos(Math.PI * i / attack));
                if (i >= count - release) envelope *= 0.5 + (0.5 * Math.Cos(Math.PI * (i - (count - release)) / release));
                mix[start + i] += tone * envelope;
            }
        }
        // Mesma energia para todos os sons; o volume escolhido escala com curva 1,5 (25% soa bem mais baixo que 100%).
        var rms = Math.Sqrt(mix.Sum(v => v * v) / mix.Length);
        var level = Math.Pow(Math.Clamp(volume, 1, 100) / 100.0, 1.5);
        var gain = rms > 0 ? TargetRms * level / rms : 0;
        var peak = mix.Max(Math.Abs);
        if (peak * gain > PeakCap) gain = PeakCap / peak;

        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        var dataBytes = length * 2;
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
        foreach (var value in mix) writer.Write((short)Math.Round(Math.Clamp(value * gain, -1, 1) * short.MaxValue));
        writer.Flush();
        return memory.ToArray();
    }
}
