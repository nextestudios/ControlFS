using ControlFS.Core.Input;

namespace ControlFS.UnitTests.Input;

public class StickNormalizerTests
{
    private readonly StickNormalizer _stick = new(InputSettings.Default);

    [Fact]
    public void Drift_inside_deadzone_produces_nothing()
    {
        Assert.Equal(StickDirection.None, _stick.Update(0.12, -0.15));
        Assert.Equal(StickDirection.None, _stick.Update(-0.2, 0.1));
    }

    [Fact]
    public void Activation_requires_threshold()
    {
        Assert.Equal(StickDirection.None, _stick.Update(0, 0.5));
        Assert.Equal(StickDirection.Down, _stick.Update(0, 0.6));
    }

    [Fact]
    public void Hysteresis_keeps_direction_until_release_threshold()
    {
        Assert.Equal(StickDirection.Up, _stick.Update(0, -0.8));
        Assert.Equal(StickDirection.Up, _stick.Update(0, -0.45)); // abaixo da ativação, acima da liberação
        Assert.Equal(StickDirection.None, _stick.Update(0, -0.35));
    }

    [Fact]
    public void Ambiguous_diagonal_does_not_activate()
    {
        Assert.Equal(StickDirection.None, _stick.Update(0.7, 0.7));
    }

    [Fact]
    public void Slight_diagonal_does_not_switch_active_direction()
    {
        Assert.Equal(StickDirection.Down, _stick.Update(0.1, 0.9));
        Assert.Equal(StickDirection.Down, _stick.Update(0.6, 0.7)); // lateral forte, mas não dominante
        Assert.Equal(StickDirection.Right, _stick.Update(0.95, 0.3));
    }
}
