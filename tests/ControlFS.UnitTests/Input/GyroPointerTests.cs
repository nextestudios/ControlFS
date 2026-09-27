using ControlFS.Core.Input;

namespace ControlFS.UnitTests.Input;

public class GyroPointerTests
{
    private const double Rad = Math.PI / 180;
    private readonly GyroPointer _gyro = new(GyroSettings.Default);
    private double _t;

    /// <summary>Leituras a 250 Hz (DualSense por USB) com (pitch, yaw) em °/s durante <paramref name="seconds"/>.</summary>
    private (double Dx, double Dy) Feed(double pitch, double yaw, double seconds, double noise = 0)
    {
        var random = new Random(7);
        double dx = 0, dy = 0;
        for (var i = 0; i < seconds * 250; i++)
        {
            _t += 0.004;
            var jitter = noise * ((random.NextDouble() * 2) - 1);
            var (x, y) = _gyro.Update((pitch + jitter) * Rad, (yaw - jitter) * Rad, TimeSpan.FromSeconds(_t));
            dx += x;
            dy += y;
        }
        return (dx, dy);
    }

    [Fact]
    public void Sensor_drift_is_learned_while_still_and_tremor_never_moves_the_pointer()
    {
        Feed(3.5, -3.5, 2, noise: 1.5); // controle parado na mesa: desvio de 3,5°/s acima da zona morta, com ruído
        var (dx, dy) = Feed(3.5, -3.5, 2, noise: 1.5);
        Assert.InRange(Math.Abs(dx) + Math.Abs(dy), 0, 0.05);
    }

    [Fact]
    public void Turning_moves_proportionally_in_the_right_direction_and_a_gap_is_not_a_jump()
    {
        Feed(0, 0, 0.5);
        var (right, _) = Feed(0, -40, 0.5); // girar para a direita (yaw negativo) 20°: ~(40-2,5)·0,5·0,25 ≈ 4,7 teclas
        Assert.InRange(right, 4, 5);
        var (_, down) = Feed(-40, 0, 0.5); // frente para baixo (pitch negativo): desce
        Assert.InRange(down, 4, 5);

        _t += 2; // Bluetooth engasgou 2 s
        var (jump, _) = _gyro.Update(0, -40 * Rad, TimeSpan.FromSeconds(_t));
        Assert.InRange(jump, 0, 0.5); // no máximo 50 ms de movimento
    }
}
