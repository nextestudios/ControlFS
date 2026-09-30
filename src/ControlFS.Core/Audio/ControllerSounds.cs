using ControlFS.Core.Actions;

namespace ControlFS.Core.Audio;

/// <summary>Os sons do controle (#276): curtos e distintos, para ouvir o que aconteceu sem olhar a tela.</summary>
public enum SoundCue
{
    /// <summary>O foco andou (direcional, páginas, troca de região ou de painel).</summary>
    Move,

    /// <summary>Confirmou ou abriu (Confirmar, menus, busca, trocar exibição).</summary>
    Confirm,

    /// <summary>Voltou ou cancelou.</summary>
    Back,

    /// <summary>Marcou ou desmarcou um item.</summary>
    Select,
}

/// <summary>Toca um som pronto, sem nunca travar quem chama (sem placa de som, não faz nada).</summary>
public interface ISoundPlayer
{
    /// <param name="volume">1 a 100.</param>
    void Play(SoundCue cue, int volume);
}

/// <summary>
/// Liga as ações vindas do controle aos sons (#276). Só o controle chama (o teclado não toca), nunca muda o que a ação faz e
/// nunca atrasa o controle: tocar é assíncrono e falha em silêncio. Segurar o direcional não vira metralhadora: um mesmo
/// som não repete antes do intervalo mínimo. Volume 0 (padrão) = desligado: nada é tocado nem preparado.
/// </summary>
public sealed class ControllerSounds(ISoundPlayer player, Func<int> volume, Func<TimeSpan> clock)
{
    private readonly Dictionary<SoundCue, TimeSpan> _last = [];

    /// <summary>Intervalo mínimo entre dois sons iguais: o movimento repete rápido ao segurar, então é o mais espaçado.</summary>
    public static TimeSpan MinGap(SoundCue cue) => cue == SoundCue.Move ? TimeSpan.FromMilliseconds(80) : TimeSpan.FromMilliseconds(120);

    /// <summary>O som de uma ação, ou <c>null</c> quando ela é silenciosa (rolagem do analógico, troca de painel sem efeito…).</summary>
    public static SoundCue? CueFor(InputAction action) => action switch
    {
        InputAction.NavigateUp or InputAction.NavigateDown or InputAction.NavigateLeft or InputAction.NavigateRight
            or InputAction.PageUp or InputAction.PageDown or InputAction.PreviousRegion or InputAction.NextRegion or InputAction.SwitchPane => SoundCue.Move,
        InputAction.Confirm or InputAction.OpenContextMenu or InputAction.OpenAppMenu or InputAction.Search or InputAction.ChangeView => SoundCue.Confirm,
        InputAction.Back => SoundCue.Back,
        InputAction.ToggleSelection => SoundCue.Select,
        _ => null, // rolagem contínua do analógico e o que não tiver som próprio
    };

    public void OnAction(InputAction action)
    {
        var level = Math.Clamp(volume(), 0, 100);
        if (level == 0 || CueFor(action) is not { } cue) return;
        var now = clock();
        if (_last.TryGetValue(cue, out var previous) && now - previous < MinGap(cue)) return;
        _last[cue] = now;
        try
        {
            player.Play(cue, level);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            // Som é só um extra: qualquer falha do sistema de áudio fica em silêncio.
        }
    }
}
