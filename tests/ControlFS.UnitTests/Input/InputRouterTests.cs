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
    public void Only_active_device_drives_the_ui()
    {
        var r = Router();
        r.OnControl("pad-A", PhysicalControl.DPadDown, true, Ms(0));
        r.OnControl("pad-B", PhysicalControl.South, true, Ms(10));
        Assert.Equal("pad-A", r.ActiveDeviceKey);
        Assert.Equal([InputAction.NavigateDown], _actions);
        r.OnDeviceRemoved("pad-A");
        Assert.Null(r.ActiveDeviceKey);
        r.OnControl("pad-B", PhysicalControl.South, false, Ms(20));
        r.OnControl("pad-B", PhysicalControl.South, true, Ms(30));
        Assert.Equal("pad-B", r.ActiveDeviceKey);
        Assert.Equal(InputAction.Confirm, _actions[^1]);
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
        Assert.Equal("A", ButtonGlyphs.For(PhysicalControl.South, ButtonLabelStyle.Xbox));
        Assert.Equal("B", ButtonGlyphs.For(PhysicalControl.South, ButtonLabelStyle.Nintendo));
        Assert.Equal("✕", ButtonGlyphs.For(PhysicalControl.South, ButtonLabelStyle.PlayStation));
        Assert.Equal(PhysicalControl.East, new ActionMap(ConfirmBackConvention.EastConfirms).ControlFor(InputAction.Confirm));
    }
}
