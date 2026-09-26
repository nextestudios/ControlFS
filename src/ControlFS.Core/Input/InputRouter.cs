using ControlFS.Core.Actions;

namespace ControlFS.Core.Input;

/// <summary>
/// Roteia eventos de controles físicos para ações semânticas:
/// arbitra o dispositivo ativo, aplica o mapeamento, repete somente navegação, e suprime
/// botões mantidos durante trocas de contexto (um botão que abriu um diálogo não o aceita).
/// Não é thread-safe: deve ser usado na mesma thread que processa eventos de entrada (UI).
/// </summary>
public sealed class InputRouter
{
    private readonly Dictionary<PhysicalControl, HeldState> _held = [];
    private readonly HashSet<PhysicalControl> _latched = [];
    private readonly Action<InputAction> _emit;
    private ActionMap _map;
    private InputSettings _settings;

    public InputRouter(ActionMap map, InputSettings settings, Action<InputAction> emit)
    {
        _map = map;
        _settings = settings;
        _emit = emit;
    }

    public string? ActiveDeviceKey { get; private set; }

    /// <summary>Quando falso (ex.: confirmação sensível aberta), outro dispositivo não assume o controle.</summary>
    public bool AllowAutomaticActivation { get; set; } = true;

    public bool IsSuspended { get; private set; }

    public event Action<string?>? ActiveDeviceChanged;

    public void UpdateMap(ActionMap map) => _map = map;

    public void UpdateSettings(InputSettings settings) => _settings = settings;

    /// <summary>Evento digital de um controle físico.</summary>
    public void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan now)
    {
        if (IsSuspended) return;

        if (ActiveDeviceKey is null)
        {
            if (!pressed || !AllowAutomaticActivation) return;
            SetActive(deviceKey);
        }
        else if (!string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal))
        {
            return; // somente um controle comanda a UI
        }

        if (!pressed)
        {
            _held.Remove(control);
            _latched.Remove(control);
            return;
        }

        if (_held.ContainsKey(control) || _latched.Contains(control)) return; // sem nova transição

        var action = _map.Resolve(control);
        if (action is null) return;
        _held[control] = new HeldState(action.Value, now + _settings.RepeatInitialDelay, _settings.RepeatStartInterval);
        _emit(action.Value);
    }

    /// <summary>Chamado periodicamente pelo laço de entrada; gera repetições de navegação.</summary>
    public void Tick(TimeSpan now)
    {
        if (IsSuspended) return;
        foreach (var control in _held.Keys.ToArray())
        {
            var state = _held[control];
            if (!state.Action.IsRepeatable() || now < state.NextRepeat) continue;
            _emit(state.Action);
            // A ação pode ter trocado o contexto (LatchHeld): não reative um controle travado.
            if (!_held.ContainsKey(control)) continue;
            var nextInterval = TimeSpan.FromTicks(Math.Max(_settings.RepeatMinInterval.Ticks, (long)(state.Interval.Ticks * _settings.RepeatAcceleration)));
            _held[control] = state with { NextRepeat = now + state.Interval, Interval = nextInterval };
        }
    }

    /// <summary>
    /// Troca de contexto (modal aberto/fechado): tudo que está mantido fica travado até ser solto.
    /// </summary>
    public void LatchHeld()
    {
        foreach (var control in _held.Keys) _latched.Add(control);
        _held.Clear();
    }

    /// <summary>Janela perdeu o foco: não roteia nada e esquece estados mantidos.</summary>
    public void Suspend()
    {
        IsSuspended = true;
        _held.Clear();
        _latched.Clear();
    }

    /// <summary>Janela recuperou o foco: estados limpos; somente novas transições geram ações.</summary>
    public void Resume()
    {
        _held.Clear();
        _latched.Clear();
        IsSuspended = false;
    }

    public void OnDeviceRemoved(string deviceKey)
    {
        if (!string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal)) return;
        _held.Clear();
        _latched.Clear();
        SetActive(null);
    }

    /// <summary>Troca explícita de dispositivo ativo (ação documentada na UI).</summary>
    public void SelectActiveDevice(string? deviceKey)
    {
        _held.Clear();
        _latched.Clear();
        SetActive(deviceKey);
    }

    private void SetActive(string? deviceKey)
    {
        if (string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal)) return;
        ActiveDeviceKey = deviceKey;
        ActiveDeviceChanged?.Invoke(deviceKey);
    }

    private readonly record struct HeldState(InputAction Action, TimeSpan NextRepeat, TimeSpan Interval);
}
