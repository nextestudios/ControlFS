using ControlFS.Core.Actions;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;

namespace ControlFS.UnitTests.Input;

public class ControllerMappingWizardTests
{
    private static readonly ControllerMatch Match = new("03000000790000000600000000000000", 0x0079, 0x0006);
    private TimeSpan _now;

    private ControllerMappingWizard Start(RawJoystickState state)
    {
        var wizard = new ControllerMappingWizard("Generic USB Joystick", Match, MappingTargets.For(ConfirmBackConvention.SouthConfirms));
        wizard.Start(state, _now);
        return wizard;
    }

    private void Advance(ControllerMappingWizard wizard, double seconds)
    {
        _now += TimeSpan.FromSeconds(seconds);
        wizard.Tick(_now);
    }

    private void Tap(ControllerMappingWizard wizard, RawInputEvent down, RawInputEvent up)
    {
        wizard.OnRaw(down, _now);
        wizard.OnRaw(up, _now);
    }

    private void Button(ControllerMappingWizard w, int index) => Tap(w, RawInputEvent.Button(index, true), RawInputEvent.Button(index, false));

    private void Hat(ControllerMappingWizard w, int mask) => Tap(w, RawInputEvent.Hat(0, mask), RawInputEvent.Hat(0, HatDirections.Centered));

    [Fact]
    public void Maps_hat_and_buttons_one_press_per_step_rejects_reused_input_and_skips_optionals_with_back()
    {
        // Botão 2 ainda pressionado (o usuário segurou para abrir o assistente): o neutro espera soltar.
        var w = Start(new RawJoystickState([0.0], [false, false, true], [0]));
        Advance(w, 1.5);
        Assert.Equal(MappingPhase.Neutral, w.Phase);
        w.OnRaw(RawInputEvent.Button(2, false), _now);
        Advance(w, 1.1);
        Assert.Equal(MappingPhase.Capture, w.Phase);

        // Segurar o hat não vale para dois passos: só avança depois de soltar.
        w.OnRaw(RawInputEvent.Hat(0, HatDirections.Up), _now);
        Assert.Equal(MappingPhase.WaitRelease, w.Phase);
        Advance(w, 0.5);
        Assert.Equal(0, w.StepIndex);
        w.OnRaw(RawInputEvent.Hat(0, HatDirections.Centered), _now);
        Assert.Equal(1, w.StepIndex);

        w.OnRaw(RawInputEvent.Hat(0, HatDirections.Down | HatDirections.Right), _now); // diagonal é ignorada
        Assert.Equal(MappingPhase.Capture, w.Phase);
        w.OnRaw(RawInputEvent.Hat(0, HatDirections.Centered), _now);
        Hat(w, HatDirections.Down);
        Hat(w, HatDirections.Left);
        Hat(w, HatDirections.Right);
        Button(w, 0); // confirmar

        Button(w, 0); // voltar no mesmo botão: recusado, o passo continua
        Assert.Equal(PhysicalControl.East, w.Current!.Control);
        Assert.Contains("já é", w.Feedback, StringComparison.Ordinal);
        Button(w, 1); // voltar

        Assert.Equal(PhysicalControl.North, w.Current!.Control);
        Assert.True(w.CanSkip);
        while (w.Phase != MappingPhase.Review) Button(w, 1); // voltar pula cada opcional pelo próprio controle

        var profile = w.BuildProfile();
        ControllerProfileSerializer.Validate(profile);
        Assert.Equal(6, profile.Bindings.Count);
        Assert.Equal(new RawBinding(RawInputKind.Hat, 0, HatDirections.Up), profile.Bindings[PhysicalControl.DPadUp]);
        Assert.Equal(new RawBinding(RawInputKind.Button, 1, 0), profile.Bindings[PhysicalControl.East]);

        // Modo de teste: o rascunho já traduz a entrada antes de salvar.
        var pressed = new List<PhysicalControl>();
        w.OnRaw(RawInputEvent.Button(0, true), _now, (c, down) => { if (down) pressed.Add(c); });
        Assert.Equal([PhysicalControl.South], pressed);
        Assert.Equal(PhysicalControl.South, w.LastTested);
    }

    [Fact]
    public void Calibrates_neutral_and_deadzone_per_axis_and_records_inverted_axis_direction()
    {
        // Eixo 0: analógico com ruído em torno de 0,1. Eixo 1: gatilho que repousa em -1.
        var w = Start(new RawJoystickState([0.1, -1.0], [], []));
        w.OnRaw(RawInputEvent.Axis(0, 0.18), _now);
        w.OnRaw(RawInputEvent.Axis(0, 0.02), _now);
        Advance(w, 1.1);
        Assert.Equal(MappingPhase.Capture, w.Phase);

        // "Cima" num eixo que cresce para cima (invertido em relação ao SDL): sentido +1.
        w.OnRaw(RawInputEvent.Axis(0, 0.5), _now); // perto demais do neutro: ainda não conta
        Assert.Equal(MappingPhase.Capture, w.Phase);
        w.OnRaw(RawInputEvent.Axis(0, 0.9), _now);
        w.OnRaw(RawInputEvent.Axis(0, 0.45), _now); // ainda fora da zona morta calibrada: não avança
        Assert.Equal(MappingPhase.WaitRelease, w.Phase);
        w.OnRaw(RawInputEvent.Axis(0, 0.12), _now);
        Assert.Equal(MappingPhase.Capture, w.Phase);
        Assert.Equal(new RawBinding(RawInputKind.Axis, 0, +1), w.Bindings[PhysicalControl.DPadUp]);

        // O gatilho em repouso (-1) não é confundido com uma escolha; apertá-lo até o fim, sim.
        w.OnRaw(RawInputEvent.Axis(1, -0.7), _now);
        Assert.Equal(MappingPhase.Capture, w.Phase);
        w.OnRaw(RawInputEvent.Axis(1, 1.0), _now);
        w.OnRaw(RawInputEvent.Axis(1, -1.0), _now);
        Assert.Equal(new RawBinding(RawInputKind.Axis, 1, +1), w.Bindings[PhysicalControl.DPadDown]);

        // Refazer: o passo anterior volta a ficar vazio e é capturado de novo.
        w.RedoPrevious(_now);
        Assert.Equal(PhysicalControl.DPadDown, w.Current!.Control);
        Assert.False(w.Bindings.ContainsKey(PhysicalControl.DPadDown));

        w.OnRaw(RawInputEvent.Axis(0, -0.9), _now);
        w.OnRaw(RawInputEvent.Axis(0, 0.1), _now);
        Assert.Equal(new RawBinding(RawInputKind.Axis, 0, -1), w.Bindings[PhysicalControl.DPadDown]);
    }

    [Fact]
    public void Inactivity_cancels_without_a_profile_and_the_neutral_step_can_not_hang()
    {
        var w = Start(new RawJoystickState([], [true], [])); // botão preso: o neutro nunca termina
        Advance(w, 10);
        Assert.Equal(MappingPhase.Neutral, w.Phase);
        Advance(w, 10.1);
        Assert.False(w.IsActive);
        Assert.Equal(MappingEndReason.TimedOut, w.EndReason);
        Assert.Throws<InvalidOperationException>(() => w.BuildProfile());
    }

    [Fact]
    public void Translator_uses_calibrated_neutral_with_hysteresis_and_ignores_hat_diagonals()
    {
        var profile = new ControllerProfile("Pad", Match, new Dictionary<PhysicalControl, RawBinding>
        {
            [PhysicalControl.RightTrigger] = new(RawInputKind.Axis, 2, +1),
            [PhysicalControl.DPadUp] = new(RawInputKind.Hat, 0, HatDirections.Up),
        }, [new AxisCalibration(2, -1, 0.2)]);
        var t = new ControllerProfileTranslator(profile);
        var events = new List<(PhysicalControl, bool)>();
        void Feed(RawInputEvent e) => t.Apply(e, (c, p) => events.Add((c, p)));

        Feed(RawInputEvent.Axis(2, -0.6)); // 0,4 do neutro: abaixo do limiar (0,45)
        Assert.Empty(events);
        Feed(RawInputEvent.Axis(2, -0.5));
        Feed(RawInputEvent.Axis(2, -0.75)); // 0,25: continua pressionado (histerese)
        Assert.Equal([(PhysicalControl.RightTrigger, true)], events);
        Feed(RawInputEvent.Axis(2, -0.85));
        Assert.Equal((PhysicalControl.RightTrigger, false), events[^1]);

        events.Clear();
        Feed(RawInputEvent.Hat(0, HatDirections.Up | HatDirections.Left));
        Assert.Empty(events);
        Feed(RawInputEvent.Hat(0, HatDirections.Up));
        Feed(RawInputEvent.Hat(0, HatDirections.Up | HatDirections.Right));
        Assert.Equal([(PhysicalControl.DPadUp, true), (PhysicalControl.DPadUp, false)], events);
    }

    [Fact]
    public void Two_button_profile_opens_actions_and_menu_by_holding_confirm_and_back()
    {
        var required = new Dictionary<PhysicalControl, RawBinding>
        {
            [PhysicalControl.DPadUp] = new(RawInputKind.Hat, 0, HatDirections.Up),
            [PhysicalControl.DPadDown] = new(RawInputKind.Hat, 0, HatDirections.Down),
            [PhysicalControl.DPadLeft] = new(RawInputKind.Hat, 0, HatDirections.Left),
            [PhysicalControl.DPadRight] = new(RawInputKind.Hat, 0, HatDirections.Right),
            [PhysicalControl.South] = new(RawInputKind.Button, 0, 0),
            [PhysicalControl.East] = new(RawInputKind.Button, 1, 0),
        };
        var profile = new ControllerProfile("Pad", Match, required, []);
        var fallback = Assert.IsType<LongPressFallback>(LongPressFallback.For(profile));
        Assert.Null(LongPressFallback.For(profile with
        {
            Bindings = new Dictionary<PhysicalControl, RawBinding>(required)
            {
                [PhysicalControl.North] = new(RawInputKind.Button, 2, 0),
                [PhysicalControl.Start] = new(RawInputKind.Button, 3, 0),
            },
        }));

        var actions = new List<InputAction>();
        var router = new InputRouter(new ActionMap(ConfirmBackConvention.SouthConfirms), InputSettings.Default, actions.Add);
        var translator = new ControllerProfileTranslator(profile);
        var longPress = new LongPressTranslator(fallback, ConfirmBackConvention.SouthConfirms);
        void Emit(PhysicalControl c, bool p) => router.OnControl("sdl:7", c, p, _now);
        void Feed(RawInputEvent e) => translator.Apply(e, (c, p) => longPress.Apply(c, p, _now, Emit));
        void Wait(double seconds)
        {
            _now += TimeSpan.FromSeconds(seconds);
            longPress.Tick(_now, Emit);
        }

        // Pressões curtas não mudam: Confirmar e Voltar saem ao soltar; direções passam direto.
        Feed(RawInputEvent.Button(0, true));
        Wait(0.3);
        Assert.Empty(actions);
        Feed(RawInputEvent.Button(0, false));
        Feed(RawInputEvent.Button(1, true));
        Feed(RawInputEvent.Button(1, false));
        Feed(RawInputEvent.Hat(0, HatDirections.Down));
        Assert.Equal([InputAction.Confirm, InputAction.Back, InputAction.NavigateDown], actions);
        Feed(RawInputEvent.Hat(0, HatDirections.Centered));

        // Mantidos por 0,6 s: Ações e Menu, uma vez só; soltar depois não confirma nem volta.
        actions.Clear();
        Feed(RawInputEvent.Button(0, true));
        Wait(0.61);
        Wait(1);
        Feed(RawInputEvent.Button(0, false));
        Feed(RawInputEvent.Button(1, true));
        Wait(0.61);
        Feed(RawInputEvent.Button(1, false));
        Assert.Equal([InputAction.OpenContextMenu, InputAction.OpenAppMenu], actions);
    }
}
