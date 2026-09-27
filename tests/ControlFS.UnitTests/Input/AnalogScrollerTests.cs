using ControlFS.Core.Actions;
using ControlFS.Core.Input;

namespace ControlFS.UnitTests.Input;

public class AnalogScrollerTests
{
    private readonly AnalogScroller _scroller = new(InputSettings.Default);
    private readonly List<InputAction> _steps = [];

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    /// <summary>Mantém (x, y) de <paramref name="from"/> a <paramref name="to"/> ms, com um tick a cada quadro de 60 Hz.</summary>
    private int Hold(double x, double y, int from, int to)
    {
        var before = _steps.Count;
        _scroller.Update(x, y, Ms(from));
        for (var t = from; t <= to; t += 16) _scroller.Tick(Ms(t), _steps.Add);
        return _steps.Count - before;
    }

    [Fact]
    public void Drift_never_scrolls_and_center_stops_at_once()
    {
        Assert.Equal(0, Hold(0.15, -0.2, 0, 3000)); // ruído dentro da zona morta
        Assert.True(Hold(0, 1, 3000, 4000) > 5);
        Assert.Equal(0, Hold(0.05, 0.1, 4000, 6000)); // de volta ao centro: nada do que estava acumulado sai depois
        Assert.All(_steps, s => Assert.Equal(InputAction.ScrollDown, s));
    }

    [Fact]
    public void Short_tap_scrolls_one_step_and_deflection_sets_the_rate_which_grows_when_sustained()
    {
        Assert.Equal(1, Hold(0, -0.5, 0, 50)); // toque curto: um passo
        Assert.Equal([InputAction.ScrollUp], _steps);
        Hold(0, 0, 60, 100);

        var light = Hold(0, 0.45, 1000, 2000);
        Hold(0, 0, 2001, 2100);
        var full = Hold(0, 1, 3000, 4000);
        Assert.True(full > light * 2, $"leve: {light}, cheio: {full}");

        Hold(0, 0, 5000, 5100);
        _scroller.Update(0, 1, Ms(6000));
        Assert.True(_scroller.Rate(Ms(7600)) > _scroller.Rate(Ms(6000)) * 1.5); // aceleração progressiva enquanto mantido
    }

    [Fact]
    public void Horizontal_needs_a_dominant_axis_and_latch_waits_for_center()
    {
        Hold(0.45, 0.5, 0, 500); // quase diagonal: fica no vertical
        Assert.All(_steps, s => Assert.Equal(InputAction.ScrollDown, s));
        _steps.Clear();
        Hold(0.9, 0.1, 500, 1000);
        Assert.Contains(InputAction.ScrollRight, _steps);

        _scroller.Latch(); // R3 pressionado ou modal aberto
        Assert.Equal(0, Hold(0.9, 0.1, 1000, 2000));
        Assert.Equal(0, Hold(0, 0, 2000, 2100));
        Assert.True(Hold(-0.9, 0, 2100, 2500) > 0); // voltou ao centro: um gesto novo rola
        Assert.Equal(InputAction.ScrollLeft, _steps[^1]);
    }
}
