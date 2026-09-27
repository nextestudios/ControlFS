using ControlFS.Core.Actions;

namespace ControlFS.Core.Input.Mapping;

/// <summary>
/// Perfil mapeado sem Ações (Norte) e/ou sem Menu (Start), como um controle de direcional e dois botões: segurar
/// Confirmar abre Ações e segurar Voltar abre o Menu. Só vale para os que faltam no perfil.
/// </summary>
public sealed record LongPressFallback(bool ConfirmOpensActions, bool BackOpensMenu)
{
    /// <summary>Tempo que Confirmar/Voltar precisam ficar pressionados para virar Ações/Menu.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(600);

    /// <summary>null quando o perfil já liga Norte e Start (nada muda).</summary>
    public static LongPressFallback? For(ControllerProfile profile)
    {
        var actions = !profile.Bindings.ContainsKey(PhysicalControl.North);
        var menu = !profile.Bindings.ContainsKey(PhysicalControl.Start);
        return actions || menu ? new(actions, menu) : null;
    }
}

/// <summary>
/// Aplica o <see cref="LongPressFallback"/> sobre os controles físicos que o perfil produz, antes do
/// <see cref="InputRouter"/>. Com o substituto ligado, Confirmar/Voltar só saem ao soltar (pressão curta: o próprio
/// botão, como antes); mantidos por <see cref="LongPressFallback.HoldTime"/>, viram uma pressão de Norte/Start e o
/// soltar não gera mais nada. Os demais controles passam direto. Não é thread-safe.
/// </summary>
public sealed class LongPressTranslator
{
    private readonly Dictionary<PhysicalControl, PhysicalControl> _substitutes = [];
    private readonly Dictionary<PhysicalControl, (TimeSpan Since, bool Fired)> _held = [];

    public LongPressTranslator(LongPressFallback fallback, ConfirmBackConvention convention)
    {
        Fallback = fallback;
        var confirm = convention == ConfirmBackConvention.SouthConfirms ? PhysicalControl.South : PhysicalControl.East;
        var back = convention == ConfirmBackConvention.SouthConfirms ? PhysicalControl.East : PhysicalControl.South;
        if (fallback.ConfirmOpensActions) _substitutes[confirm] = PhysicalControl.North;
        if (fallback.BackOpensMenu) _substitutes[back] = PhysicalControl.Start;
    }

    public LongPressFallback Fallback { get; }

    public void Apply(PhysicalControl control, bool pressed, TimeSpan now, Action<PhysicalControl, bool> emit)
    {
        if (!_substitutes.ContainsKey(control))
        {
            emit(control, pressed);
            return;
        }
        if (pressed)
        {
            _held.TryAdd(control, (now, false));
            return;
        }
        if (!_held.Remove(control, out var state) || state.Fired) return;
        emit(control, true); // pressão curta: o botão de sempre
        emit(control, false);
    }

    /// <summary>Chamado pelo laço de entrada: dispara Ações/Menu para Confirmar/Voltar mantidos.</summary>
    public void Tick(TimeSpan now, Action<PhysicalControl, bool> emit)
    {
        foreach (var (control, state) in _held.ToArray())
        {
            if (state.Fired || now - state.Since < LongPressFallback.HoldTime) continue;
            _held[control] = state with { Fired = true };
            var substitute = _substitutes[control];
            emit(substitute, true);
            emit(substitute, false);
        }
    }

    /// <summary>Troca de perfil ou desconexão: esquece o que estava mantido sem gerar ações.</summary>
    public void Reset() => _held.Clear();
}
