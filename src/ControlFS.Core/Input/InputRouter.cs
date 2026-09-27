using ControlFS.Core.Actions;

namespace ControlFS.Core.Input;

/// <summary>
/// Roteia eventos de controles físicos para ações semânticas:
/// arbitra o dispositivo ativo (outro controle assume ao apertar um botão enquanto o ativo está solto), aplica o mapeamento, repete somente navegação, e suprime
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

    /// <summary>
    /// Dispositivo escolhido explicitamente (Menu → Controle ativo): só ele comanda a UI e nenhum outro assume,
    /// nem com uma nova pressão. Evita ação dupla quando um remapeador expõe o controle físico e uma cópia virtual.
    /// </summary>
    public bool IsActiveDeviceLocked { get; private set; }

    /// <summary>
    /// Regra de repetição do contexto atual (ex.: teclado virtual repete apagar e cursor). Consultada a cada
    /// repetição, então parar de valer interrompe a repetição. Sem regra, só navegação repete.
    /// </summary>
    public Func<InputAction, bool>? RepeatPolicy { get; set; }

    public event Action<string?>? ActiveDeviceChanged;

    public void UpdateMap(ActionMap map) => _map = map;

    public void UpdateSettings(InputSettings settings) => _settings = settings;

    /// <summary>Evento digital de um controle físico.</summary>
    public void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan now)
    {
        if (IsSuspended) return;

        if (!Arbitrate(deviceKey, pressed)) return;

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

    /// <summary>
    /// Passo de rolagem do analógico direito (<see cref="AnalogScroller"/>): não passa pelo mapa nem repete (a taxa já vem
    /// do analógico). Inclinar nunca tira o comando de outro controle (um analógico apoiado ou com drift não "rouba" a UI);
    /// sem controle ativo, assume como uma pressão.
    /// </summary>
    public void OnScroll(string deviceKey, InputAction action)
    {
        if (IsSuspended || !action.IsScroll()) return;
        if (ActiveDeviceKey is not null && !string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal)) return;
        if (!Arbitrate(deviceKey, pressed: true)) return;
        _emit(action);
    }

    /// <summary>
    /// Só um controle comanda a UI. Outro assume com uma nova pressão, desde que o ativo não esteja segurando nada: evita
    /// disputa entre dois controles e ação dupla de um par físico+virtual. Falso: o evento deste dispositivo é ignorado.
    /// </summary>
    private bool Arbitrate(string deviceKey, bool pressed)
    {
        if (IsActiveDeviceLocked && !string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal)) return false;
        if (ActiveDeviceKey is null)
        {
            if (!pressed || !AllowAutomaticActivation) return false;
            SetActive(deviceKey);
        }
        else if (!string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal))
        {
            if (!pressed || !AllowAutomaticActivation || _held.Count > 0 || _latched.Count > 0) return false;
            SetActive(deviceKey);
        }
        return true;
    }

    /// <summary>Chamado periodicamente pelo laço de entrada; gera repetições de navegação.</summary>
    public void Tick(TimeSpan now)
    {
        if (IsSuspended) return;
        foreach (var control in _held.Keys.ToArray())
        {
            var state = _held[control];
            if (now < state.NextRepeat || !(RepeatPolicy?.Invoke(state.Action) ?? state.Action.IsRepeatable())) continue;
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
        IsActiveDeviceLocked = false; // o escolhido saiu: qualquer controle volta a poder assumir
        SetActive(null);
    }

    /// <summary>
    /// Troca explícita de dispositivo ativo (Menu → Controle ativo): o escolhido fica fixo até voltar ao automático.
    /// null volta ao automático; o ativo atual continua comandando até outro assumir com uma nova pressão.
    /// </summary>
    public void SelectActiveDevice(string? deviceKey)
    {
        _held.Clear();
        _latched.Clear();
        IsActiveDeviceLocked = deviceKey is not null;
        if (deviceKey is not null) SetActive(deviceKey);
    }

    private void SetActive(string? deviceKey)
    {
        if (string.Equals(ActiveDeviceKey, deviceKey, StringComparison.Ordinal)) return;
        ActiveDeviceKey = deviceKey;
        ActiveDeviceChanged?.Invoke(deviceKey);
    }

    private readonly record struct HeldState(InputAction Action, TimeSpan NextRepeat, TimeSpan Interval);
}
