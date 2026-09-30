using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Audio;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Toca os sons do controle (#276) pelo dispositivo de áudio padrão do Windows (winmm PlaySound, da memória e assíncrono:
/// volta na hora e um som novo substitui o anterior). Cada som é sintetizado uma vez por volume e fica na memória
/// nativa enquanto o app roda. Sem dispositivo de som, PlaySound só devolve falso: nada quebra.
/// </summary>
public sealed partial class WindowsSoundPlayer : ISoundPlayer, IDisposable
{
    private const uint SndAsync = 0x1, SndNoDefault = 0x2, SndMemory = 0x4;
    private readonly Dictionary<(SoundCue, int), nint> _sounds = [];
    private bool _disposed;

    public void Play(SoundCue cue, int volume)
    {
        if (!OperatingSystem.IsWindows() || _disposed) return;
        // Volumes em degraus de 5: poucos WAVs diferentes na memória.
        var level = Math.Clamp((volume + 4) / 5 * 5, 5, 100);
        if (!_sounds.TryGetValue((cue, level), out var memory))
        {
            var wav = ToneSynth.Render(cue, level);
            memory = Marshal.AllocHGlobal(wav.Length);
            Marshal.Copy(wav, 0, memory, wav.Length);
            _sounds[(cue, level)] = memory;
        }
        _ = PlaySoundW(memory, 0, SndMemory | SndAsync | SndNoDefault);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (OperatingSystem.IsWindows()) _ = PlaySoundW(0, 0, 0); // para o som em curso antes de liberar a memória
        foreach (var memory in _sounds.Values) Marshal.FreeHGlobal(memory);
        _sounds.Clear();
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool PlaySoundW(nint pszSound, nint hmod, uint fdwSound);
}
