namespace ControlFS.Core.Policies;

/// <summary>O que fazer com o processo numa mudança de estado de <see cref="BackgroundModePolicy"/>.</summary>
[Flags]
public enum BackgroundEffects
{
    None = 0,

    /// <summary>Prioridade abaixo do normal e modo de eficiência (EcoQoS) do Windows.</summary>
    Lower = 1,

    /// <summary>Volta à prioridade normal, sem limitação de energia.</summary>
    Restore = 2,

    /// <summary>Coleta e compacta a memória gerenciada e devolve ao Windows as páginas que o app não está usando.</summary>
    Trim = 4,
}

/// <summary>
/// "Leve em segundo plano" (docs/performance.md): com a janela inativa ou minimizada — o usuário foi jogar —, o ControlFS
/// cede CPU ao jogo (prioridade abaixo do normal) e, depois de <see cref="TrimDelay"/> ainda em segundo plano, devolve a
/// memória livre ao Windows uma vez. Voltar à janela restaura a prioridade na hora. Mídia tocando (o usuário ouve música
/// enquanto usa outra janela) mantém tudo como está. Sem relógio próprio: quem usa chama <see cref="Update"/> nas
/// mudanças e quando <see cref="TrimDue"/> vence.
/// </summary>
public sealed class BackgroundModePolicy
{
    public static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(5);

    private bool _lowered;
    private bool _trimmed;
    private TimeSpan? _backgroundSince;

    /// <summary>Quando a memória deve ser devolvida (null: nada agendado).</summary>
    public TimeSpan? TrimDue => _lowered && !_trimmed && _backgroundSince is { } since ? since + TrimDelay : null;

    public bool IsLowered => _lowered;

    /// <param name="windowActive">A janela do ControlFS está ativa e não minimizada.</param>
    /// <param name="enabled">A preferência "Leve em segundo plano".</param>
    /// <param name="mediaPlaying">Áudio ou vídeo tocando no ControlFS.</param>
    /// <param name="now">Relógio monotônico.</param>
    public BackgroundEffects Update(bool windowActive, bool enabled, bool mediaPlaying, TimeSpan now)
    {
        var wanted = !windowActive && enabled && !mediaPlaying;
        if (!wanted)
        {
            _backgroundSince = null;
            _trimmed = false;
            if (!_lowered) return BackgroundEffects.None;
            _lowered = false;
            return BackgroundEffects.Restore;
        }
        if (!_lowered)
        {
            _lowered = true;
            _trimmed = false;
            _backgroundSince = now;
            return BackgroundEffects.Lower;
        }
        if (!_trimmed && now - _backgroundSince >= TrimDelay)
        {
            _trimmed = true;
            return BackgroundEffects.Trim;
        }
        return BackgroundEffects.None;
    }
}
