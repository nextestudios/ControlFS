using ControlFS.Core.Actions;

namespace ControlFS.Core.Input.Mapping;

public enum MappingPhase
{
    /// <summary>Tudo solto por um instante: mede o neutro e o ruído de cada eixo.</summary>
    Neutral,
    /// <summary>Aguardando a entrada do passo atual.</summary>
    Capture,
    /// <summary>Aguardando soltar tudo antes do próximo passo (uma pressão nunca vale para dois passos).</summary>
    WaitRelease,
    /// <summary>Modo de teste: o rascunho já comanda a tela; nada foi salvo.</summary>
    Review,
    Finished,
}

public enum MappingEndReason
{
    None,
    Saved,
    Cancelled,
    TimedOut,
    Disconnected,
}

public sealed record MappingWizardOptions
{
    public TimeSpan NeutralDuration { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>Sem nenhuma entrada por este tempo, o assistente é cancelado sem salvar (ninguém fica preso nele).</summary>
    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ReviewTimeout { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Distância mínima do neutro para um eixo contar como escolha.</summary>
    public double CaptureThreshold { get; init; } = 0.6;
    /// <summary>Movimento que reinicia a medição do neutro.</summary>
    public double NeutralTolerance { get; init; } = 0.3;
    public double MinDeadzone { get; init; } = 0.2;
    public double MaxDeadzone { get; init; } = 0.6;

    public static MappingWizardOptions Default { get; } = new();
}

/// <summary>
/// Máquina de estados do assistente de mapeamento, dirigida pelos eventos crus do próprio joystick e por comandos do
/// teclado (pular, refazer, cancelar). Neutro → um passo por controle (cada um exige soltar tudo antes do próximo) →
/// teste. Só produz um perfil; salvar (e confirmar a substituição de um perfil existente) é da camada de aplicação.
/// O relógio vem de fora (<c>now</c>) para ser determinístico. Não é thread-safe.
/// </summary>
public sealed class ControllerMappingWizard
{
    private readonly MappingWizardOptions _options;
    private readonly Dictionary<PhysicalControl, RawBinding> _bindings = [];
    private readonly Dictionary<int, AxisCalibration> _calibration = [];
    private readonly Dictionary<int, double> _axes = [];
    private readonly Dictionary<int, (double Min, double Max)> _noise = [];
    private readonly HashSet<int> _buttonsDown = [];
    private readonly Dictionary<int, int> _hats = [];
    private TimeSpan _neutralSince;
    private TimeSpan _deadline;
    private bool _advanceOnRelease;
    private bool _returnToReview;
    private ControllerProfileTranslator? _test;

    public ControllerMappingWizard(string deviceName, ControllerMatch match, IReadOnlyList<MappingTarget> targets, MappingWizardOptions? options = null)
    {
        DeviceName = deviceName;
        Match = match;
        Targets = targets;
        _options = options ?? MappingWizardOptions.Default;
        BackControl = targets.Where(t => !t.Optional).Select(t => t.Control).LastOrDefault();
    }

    public string DeviceName { get; }
    public ControllerMatch Match { get; }
    public IReadOnlyList<MappingTarget> Targets { get; }
    public MappingPhase Phase { get; private set; } = MappingPhase.Neutral;
    public MappingEndReason EndReason { get; private set; }
    public int StepIndex { get; private set; }
    public MappingTarget? Current => Phase is MappingPhase.Capture or MappingPhase.WaitRelease && StepIndex < Targets.Count ? Targets[StepIndex] : null;
    public IReadOnlyDictionary<PhysicalControl, RawBinding> Bindings => _bindings;
    /// <summary>Retorno do último evento (entrada aceita, repetida, pulada…), para a tela.</summary>
    public string? Feedback { get; private set; }
    /// <summary>No modo de teste: o último controle reconhecido pelo rascunho.</summary>
    public PhysicalControl? LastTested { get; private set; }
    public bool IsActive => Phase != MappingPhase.Finished;
    public bool CanSkip => Current is { Optional: true } && !_advanceOnRelease;
    public bool CanRedoPrevious => Phase is MappingPhase.Capture or MappingPhase.WaitRelease && (StepIndex > 0 || _advanceOnRelease) && !_returnToReview;

    /// <summary>Último passo obrigatório (voltar): durante os opcionais, apertá-lo pula o passo sem usar o teclado.</summary>
    public PhysicalControl BackControl { get; }

    public void Start(RawJoystickState state, TimeSpan now)
    {
        for (var i = 0; i < state.Axes.Count; i++) _axes[i] = Math.Clamp(state.Axes[i], -1, 1);
        for (var i = 0; i < state.Buttons.Count; i++)
            if (state.Buttons[i]) _buttonsDown.Add(i);
        for (var i = 0; i < state.Hats.Count; i++) _hats[i] = state.Hats[i];
        Phase = MappingPhase.Neutral;
        RestartNeutral(now);
        Touch(now);
    }

    /// <summary>
    /// Evento cru do joystick sendo mapeado. No modo de teste, o rascunho traduz o evento e <paramref name="onTest"/>
    /// recebe as transições (para a tela obedecer ao mapeamento antes de salvar).
    /// </summary>
    public void OnRaw(RawInputEvent e, TimeSpan now, Action<PhysicalControl, bool>? onTest = null)
    {
        if (!IsActive || e.Index is < 0 or > ControllerProfileSerializer.MaxRawIndex) return;
        Track(e);
        if (IsActivity(e)) Touch(now);
        switch (Phase)
        {
            case MappingPhase.Neutral:
                OnNeutral(e, now);
                break;
            case MappingPhase.Capture:
                OnCapture(e, now);
                break;
            case MappingPhase.WaitRelease:
                CheckRelease(now);
                break;
            case MappingPhase.Review:
                _test?.Apply(e, (control, pressed) =>
                {
                    if (pressed) LastTested = control;
                    onTest?.Invoke(control, pressed);
                });
                break;
        }
    }

    /// <summary>Avança o tempo: conclui a medição do neutro e cancela por inatividade. Retorna true se algo mudou.</summary>
    public bool Tick(TimeSpan now)
    {
        if (!IsActive) return false;
        if (now >= _deadline)
        {
            End(MappingEndReason.TimedOut);
            return true;
        }
        if (Phase == MappingPhase.Neutral && now - _neutralSince >= _options.NeutralDuration)
        {
            if (!AtRest(ignoreAxes: true))
            {
                RestartNeutral(now);
                return false;
            }
            FinishNeutral();
            Phase = MappingPhase.Capture;
            StepIndex = 0;
            Touch(now);
            return true;
        }
        if (Phase == MappingPhase.WaitRelease) return CheckRelease(now);
        return false;
    }

    /// <summary>Qualquer comando do teclado conta como atividade (não expira enquanto a pessoa age).</summary>
    public void Touch(TimeSpan now) => _deadline = now + (Phase == MappingPhase.Review ? _options.ReviewTimeout : _options.StepTimeout);

    /// <summary>Pula o passo opcional atual.</summary>
    public void Skip(TimeSpan now)
    {
        if (!CanSkip) return;
        Feedback = $"{Current!.Label}: sem botão";
        _advanceOnRelease = true;
        Phase = MappingPhase.WaitRelease;
        Touch(now);
        CheckRelease(now);
    }

    /// <summary>Refaz o passo recém-capturado ou, se o atual ainda está vazio, volta ao anterior.</summary>
    public void RedoPrevious(TimeSpan now)
    {
        if (!CanRedoPrevious) return;
        if (!_advanceOnRelease) StepIndex--;
        _bindings.Remove(Targets[StepIndex].Control);
        _advanceOnRelease = false;
        Feedback = $"Refazendo: {Targets[StepIndex].Label}";
        Phase = MappingPhase.WaitRelease;
        Touch(now);
        CheckRelease(now);
    }

    /// <summary>No modo de teste: refaz um passo e volta ao teste.</summary>
    public void Redo(PhysicalControl control, TimeSpan now)
    {
        if (Phase != MappingPhase.Review) return;
        var index = -1;
        for (var i = 0; i < Targets.Count; i++)
            if (Targets[i].Control == control) index = i;
        if (index < 0) return;
        _bindings.Remove(control);
        StepIndex = index;
        _returnToReview = true;
        _advanceOnRelease = false;
        _test = null;
        LastTested = null;
        Feedback = $"Refazendo: {Targets[index].Label}";
        Phase = MappingPhase.WaitRelease;
        Touch(now);
        CheckRelease(now);
    }

    /// <summary>Cancelamento seguro: descarta o rascunho; nenhum perfil salvo é tocado.</summary>
    public void Cancel(MappingEndReason reason = MappingEndReason.Cancelled) => End(reason);

    public void MarkSaved() => End(MappingEndReason.Saved);

    /// <summary>O rascunho como perfil (somente no teste, quando todos os passos obrigatórios têm entrada).</summary>
    public ControllerProfile BuildProfile()
    {
        if (Phase != MappingPhase.Review) throw new InvalidOperationException("O perfil só existe depois de todos os passos.");
        var axes = _bindings.Values.Where(b => b.Kind == RawInputKind.Axis).Select(b => b.Index).Distinct().Order()
            .Select(i => _calibration.TryGetValue(i, out var c) ? c : new AxisCalibration(i, 0, AxisCalibration.DefaultDeadzone))
            .ToList();
        return new ControllerProfile(ControllerProfileSerializer.CleanName(DeviceName), Match, new Dictionary<PhysicalControl, RawBinding>(_bindings), axes);
    }

    private void End(MappingEndReason reason)
    {
        if (!IsActive) return;
        EndReason = reason;
        Phase = MappingPhase.Finished;
        _test = null;
    }

    private void OnNeutral(RawInputEvent e, TimeSpan now)
    {
        if (!AtRest(ignoreAxes: true))
        {
            Feedback = "Solte todos os botões e alavancas.";
            RestartNeutral(now);
            return;
        }
        if (e.Kind != RawInputKind.Axis) return;
        var (min, max) = _noise.TryGetValue(e.Index, out var n) ? n : (e.Value, e.Value);
        min = Math.Min(min, e.Value);
        max = Math.Max(max, e.Value);
        if (max - min > 2 * _options.NeutralTolerance)
        {
            Feedback = "Solte todos os botões e alavancas.";
            RestartNeutral(now);
            return;
        }
        _noise[e.Index] = (min, max);
    }

    private void RestartNeutral(TimeSpan now)
    {
        _neutralSince = now;
        _noise.Clear();
        foreach (var (index, value) in _axes) _noise[index] = (value, value);
    }

    private void FinishNeutral()
    {
        foreach (var (index, (min, max)) in _noise)
        {
            var noise = (max - min) / 2;
            var deadzone = Math.Clamp(noise + _options.MinDeadzone, _options.MinDeadzone, _options.MaxDeadzone);
            _calibration[index] = new AxisCalibration(index, Math.Round((min + max) / 2, 3), Math.Round(deadzone, 3));
        }
        Feedback = null;
    }

    private void OnCapture(RawInputEvent e, TimeSpan now)
    {
        if (Candidate(e) is not { } candidate) return;
        var target = Targets[StepIndex];
        foreach (var (control, bound) in _bindings)
        {
            if (bound != candidate) continue;
            if (control == BackControl && target.Optional)
            {
                // Voltar (já ligado) pula o passo opcional: o próprio controle conduz o assistente.
                Skip(now);
                return;
            }
            Feedback = $"Essa entrada já é \"{Label(control)}\". Solte e use outra.";
            _advanceOnRelease = false;
            Phase = MappingPhase.WaitRelease;
            return;
        }
        _bindings[target.Control] = candidate;
        if (candidate.Kind == RawInputKind.Axis && !_calibration.ContainsKey(candidate.Index))
            _calibration[candidate.Index] = new AxisCalibration(candidate.Index, 0, AxisCalibration.DefaultDeadzone);
        Feedback = $"{target.Label}: {candidate.Describe()}";
        _advanceOnRelease = true;
        Phase = MappingPhase.WaitRelease;
    }

    private RawBinding? Candidate(RawInputEvent e)
    {
        switch (e.Kind)
        {
            case RawInputKind.Button:
                return e.Value >= 0.5 ? new RawBinding(RawInputKind.Button, e.Index, 0) : null;
            case RawInputKind.Hat:
                var mask = (int)e.Value;
                return HatDirections.IsSingle(mask) ? new RawBinding(RawInputKind.Hat, e.Index, mask) : null;
            default:
                var axis = AxisFor(e.Index);
                var distance = e.Value - axis.Neutral;
                var threshold = Math.Max(_options.CaptureThreshold, axis.ActivationThreshold);
                return Math.Abs(distance) >= threshold ? new RawBinding(RawInputKind.Axis, e.Index, Math.Sign(distance)) : null;
        }
    }

    private bool CheckRelease(TimeSpan now)
    {
        if (Phase != MappingPhase.WaitRelease || !AtRest(ignoreAxes: false)) return false;
        if (!_advanceOnRelease)
        {
            Phase = MappingPhase.Capture;
            return true;
        }
        _advanceOnRelease = false;
        if (_returnToReview || StepIndex + 1 >= Targets.Count)
        {
            _returnToReview = false;
            EnterReview(now);
            return true;
        }
        StepIndex++;
        Phase = MappingPhase.Capture;
        Touch(now);
        return true;
    }

    private void EnterReview(TimeSpan now)
    {
        Phase = MappingPhase.Review;
        StepIndex = Targets.Count;
        _test = new ControllerProfileTranslator(BuildProfile());
        LastTested = null;
        Feedback = null;
        Touch(now);
    }

    private void Track(RawInputEvent e)
    {
        switch (e.Kind)
        {
            case RawInputKind.Button:
                if (e.Value >= 0.5) _buttonsDown.Add(e.Index); else _buttonsDown.Remove(e.Index);
                break;
            case RawInputKind.Hat:
                _hats[e.Index] = (int)e.Value;
                break;
            default:
                _axes[e.Index] = e.Value;
                break;
        }
    }

    private bool IsActivity(RawInputEvent e) => e.Kind != RawInputKind.Axis || Math.Abs(e.Value - AxisFor(e.Index).Neutral) > AxisFor(e.Index).Deadzone;

    private bool AtRest(bool ignoreAxes)
    {
        if (_buttonsDown.Count > 0) return false;
        foreach (var hat in _hats.Values)
            if (hat != HatDirections.Centered) return false;
        if (ignoreAxes) return true;
        foreach (var (index, value) in _axes)
        {
            var axis = AxisFor(index);
            if (Math.Abs(value - axis.Neutral) > axis.Deadzone) return false;
        }
        return true;
    }

    private AxisCalibration AxisFor(int index) =>
        _calibration.TryGetValue(index, out var c) ? c : new AxisCalibration(index, 0, AxisCalibration.DefaultDeadzone);

    private string Label(PhysicalControl control)
    {
        foreach (var t in Targets)
            if (t.Control == control) return t.Label;
        return control.ToString();
    }
}
