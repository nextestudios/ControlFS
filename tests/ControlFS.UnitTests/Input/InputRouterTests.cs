using ControlFS.Core.Actions;
using ControlFS.Core.Input;

namespace ControlFS.UnitTests.Input;

public class InputRouterTests
{
    private readonly List<InputAction> _actions = [];
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    private InputRouter Router(ConfirmBackConvention convention = ConfirmBackConvention.SouthConfirms) =>
        new(new ActionMap(convention), InputSettings.Default, _actions.Add);

    [Fact]
    public void Confirm_fires_once_per_press_and_never_repeats()
    {
        var r = Router();
        r.OnControl("p1", PhysicalControl.South, true, Ms(0));
        for (var t = 0; t < 3000; t += 16) r.Tick(Ms(t));
        r.OnControl("p1", PhysicalControl.South, true, Ms(3000)); // eventos duplicados de "pressionado" não geram ação
        Assert.Equal([InputAction.Confirm], _actions);
    }

    [Fact]
    public void Navigation_repeats_after_initial_delay_with_acceleration()
    {
        var r = Router();
        r.OnControl("p1", PhysicalControl.DPadDown, true, Ms(0));
        for (var t = 0; t <= 370; t += 10) r.Tick(Ms(t));
        Assert.Single(_actions);
        for (var t = 380; t <= 2000; t += 5) r.Tick(Ms(t));
        Assert.True(_actions.Count > 15, $"repetições: {_actions.Count}");
        Assert.All(_actions, a => Assert.Equal(InputAction.NavigateDown, a));
        r.OnControl("p1", PhysicalControl.DPadDown, false, Ms(2001));
        var count = _actions.Count;
        r.Tick(Ms(5000));
        Assert.Equal(count, _actions.Count);
    }

    [Fact]
    public void Held_button_is_latched_on_context_change_until_released()
    {
        var r = Router();
        r.OnControl("p1", PhysicalControl.South, true, Ms(0)); // abre um diálogo
        r.LatchHeld();
        r.OnControl("p1", PhysicalControl.South, true, Ms(50)); // "ainda pressionado"
        r.OnControl("p1", PhysicalControl.DPadDown, true, Ms(60));
        Assert.Equal([InputAction.Confirm, InputAction.NavigateDown], _actions);
        r.OnControl("p1", PhysicalControl.South, false, Ms(100));
        r.OnControl("p1", PhysicalControl.South, true, Ms(150)); // nova transição: agora aceita
        Assert.Equal(InputAction.Confirm, _actions[^1]);
        Assert.Equal(3, _actions.Count);
    }

    [Fact]
    public void Latched_navigation_does_not_repeat_into_new_context()
    {
        var r = Router();
        r.OnControl("p1", PhysicalControl.DPadDown, true, Ms(0));
        r.LatchHeld();
        for (var t = 0; t < 2000; t += 10) r.Tick(Ms(t));
        Assert.Single(_actions);
    }

    [Fact]
    public void Context_change_triggered_by_a_repeat_keeps_the_control_latched()
    {
        InputRouter? router = null;
        var emitted = 0;
        router = new InputRouter(new ActionMap(ConfirmBackConvention.SouthConfirms), InputSettings.Default, a =>
        {
            emitted++;
            if (emitted == 2) router!.LatchHeld(); // a primeira repetição abre um modal
        });
        router.OnControl("p1", PhysicalControl.DPadDown, true, Ms(0));
        for (var t = 0; t < 3000; t += 5) router.Tick(Ms(t));
        Assert.Equal(2, emitted);
    }

    [Fact]
    public void Another_device_takes_over_only_with_a_new_press_while_the_active_one_is_idle()
    {
        var r = Router();
        var changes = new List<string?>();
        r.ActiveDeviceChanged += changes.Add;
        r.OnControl("pad-A", PhysicalControl.DPadDown, true, Ms(0));
        r.OnControl("pad-B", PhysicalControl.South, true, Ms(10)); // A segura o direcional: B não assume
        Assert.Equal("pad-A", r.ActiveDeviceKey);
        Assert.Equal([InputAction.NavigateDown], _actions);

        r.OnControl("pad-A", PhysicalControl.DPadDown, false, Ms(20));
        r.OnControl("pad-B", PhysicalControl.South, false, Ms(25)); // soltar não ativa ninguém
        Assert.Equal("pad-A", r.ActiveDeviceKey);
        r.OnControl("pad-B", PhysicalControl.South, true, Ms(30)); // troca a quente
        Assert.Equal("pad-B", r.ActiveDeviceKey);
        Assert.Equal(InputAction.Confirm, _actions[^1]);
        Assert.Equal(["pad-A", "pad-B"], changes);

        r.OnDeviceRemoved("pad-B");
        Assert.Null(r.ActiveDeviceKey);
    }

    [Fact]
    public void Explicitly_selected_device_is_the_only_one_routed_until_automatic_or_removed()
    {
        var r = Router();
        r.OnControl("physical", PhysicalControl.South, true, Ms(0));
        r.OnControl("physical", PhysicalControl.South, false, Ms(10));
        r.SelectActiveDevice("virtual"); // Menu → Controle ativo
        Assert.True(r.IsActiveDeviceLocked);
        Assert.Equal("virtual", r.ActiveDeviceKey);

        // O par físico+virtual manda a mesma pressão pelos dois: só o escolhido age, e o outro não assume nem ocioso.
        r.OnControl("physical", PhysicalControl.DPadDown, true, Ms(20));
        r.OnControl("virtual", PhysicalControl.DPadDown, true, Ms(21));
        r.OnControl("physical", PhysicalControl.DPadDown, false, Ms(30));
        r.OnControl("virtual", PhysicalControl.DPadDown, false, Ms(31));
        r.OnControl("physical", PhysicalControl.East, true, Ms(40));
        Assert.Equal("virtual", r.ActiveDeviceKey);
        Assert.Equal([InputAction.Confirm, InputAction.NavigateDown], _actions);

        // Automático: o escolhido continua até outro assumir com uma nova pressão.
        r.OnControl("physical", PhysicalControl.East, false, Ms(50));
        r.SelectActiveDevice(null);
        Assert.False(r.IsActiveDeviceLocked);
        Assert.Equal("virtual", r.ActiveDeviceKey);
        r.OnControl("physical", PhysicalControl.East, true, Ms(60));
        Assert.Equal("physical", r.ActiveDeviceKey);

        // O escolhido desconectado não deixa a UI sem controle.
        r.SelectActiveDevice("virtual");
        r.OnDeviceRemoved("virtual");
        Assert.False(r.IsActiveDeviceLocked);
        r.OnControl("physical", PhysicalControl.South, true, Ms(70));
        Assert.Equal("physical", r.ActiveDeviceKey);
    }

    [Fact]
    public void Sensitive_context_blocks_automatic_device_takeover()
    {
        var r = Router();
        r.AllowAutomaticActivation = false;
        r.OnControl("pad-B", PhysicalControl.South, true, Ms(0));
        Assert.Empty(_actions);
        Assert.Null(r.ActiveDeviceKey);
    }

    [Fact]
    public void Suspended_router_ignores_input_and_resume_requires_new_press()
    {
        var r = Router();
        r.OnControl("p1", PhysicalControl.DPadUp, true, Ms(0));
        r.Suspend();
        r.OnControl("p1", PhysicalControl.South, true, Ms(10));
        r.Tick(Ms(2000));
        r.Resume();
        r.Tick(Ms(4000));
        Assert.Equal([InputAction.NavigateUp], _actions);
    }

    [Fact]
    public void East_confirms_convention_swaps_behavior_by_position()
    {
        var r = Router(ConfirmBackConvention.EastConfirms);
        r.OnControl("p1", PhysicalControl.East, true, Ms(0));
        r.OnControl("p1", PhysicalControl.South, true, Ms(10));
        Assert.Equal([InputAction.Confirm, InputAction.Back], _actions);
    }

    [Fact]
    public void Glyphs_follow_physical_position_not_letters()
    {
        Assert.Equal("A", ButtonGlyphs.For(PhysicalControl.South, ControllerFamily.Xbox));
        Assert.Equal("B", ButtonGlyphs.For(PhysicalControl.South, ControllerFamily.Nintendo));
        Assert.Equal("✕", ButtonGlyphs.For(PhysicalControl.South, ControllerFamily.PlayStation));
        Assert.Equal(PhysicalControl.East, new ActionMap(ConfirmBackConvention.EastConfirms).ControlFor(InputAction.Confirm));
    }
}
