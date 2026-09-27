using ControlFS.Core.Actions;

namespace ControlFS.Core.Input.Mapping;

/// <summary>
/// Aplica um perfil a eventos crus e produz transições digitais de <see cref="PhysicalControl"/> (as mesmas dos
/// gamepads, que seguem para o <see cref="InputRouter"/>). Eixos usam o neutro e a zona morta calibrados, com histerese;
/// hats só contam direções simples (diagonal não navega). Não é thread-safe.
/// </summary>
public sealed class ControllerProfileTranslator
{
    private readonly ControllerProfile _profile;
    private readonly HashSet<PhysicalControl> _down = [];
    private readonly Dictionary<(RawInputKind, int), List<(PhysicalControl Control, RawBinding Binding)>> _byInput = [];

    public ControllerProfileTranslator(ControllerProfile profile)
    {
        _profile = profile;
        foreach (var (control, binding) in profile.Bindings)
        {
            var key = (binding.Kind, binding.Index);
            if (!_byInput.TryGetValue(key, out var list)) _byInput[key] = list = [];
            list.Add((control, binding));
        }
    }

    public ControllerProfile Profile => _profile;

    public void Apply(RawInputEvent e, Action<PhysicalControl, bool> emit)
    {
        if (!_byInput.TryGetValue((e.Kind, e.Index), out var bindings)) return;
        foreach (var (control, binding) in bindings)
        {
            var wasDown = _down.Contains(control);
            var isDown = e.Kind switch
            {
                RawInputKind.Button => e.Value >= 0.5,
                RawInputKind.Hat => (int)e.Value == binding.Direction,
                _ => AxisDown(binding, e.Value, wasDown),
            };
            if (isDown == wasDown) continue;
            if (isDown) _down.Add(control); else _down.Remove(control);
            emit(control, isDown);
        }
    }

    /// <summary>Solta tudo (troca de perfil, desconexão).</summary>
    public void ReleaseAll(Action<PhysicalControl, bool> emit)
    {
        foreach (var control in _down.ToArray()) emit(control, false);
        _down.Clear();
    }

    private bool AxisDown(RawBinding binding, double value, bool wasDown)
    {
        var axis = _profile.AxisFor(binding.Index);
        var distance = (value - axis.Neutral) * binding.Direction;
        return wasDown ? distance > axis.Deadzone : distance >= axis.ActivationThreshold;
    }
}
