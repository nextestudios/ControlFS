using ControlFS.Core.Policies;

namespace ControlFS.UnitTests.Core;

public class BackgroundModePolicyTests
{
    private readonly BackgroundModePolicy _policy = new();

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Background_lowers_at_once_trims_once_after_the_delay_and_activation_restores()
    {
        Assert.Equal(BackgroundEffects.None, _policy.Update(windowActive: true, enabled: true, mediaPlaying: false, S(0)));
        Assert.Equal(BackgroundEffects.Lower, _policy.Update(false, true, false, S(10)));
        Assert.Equal(S(10) + BackgroundModePolicy.TrimDelay, _policy.TrimDue);
        Assert.Equal(BackgroundEffects.None, _policy.Update(false, true, false, S(12)));
        Assert.Equal(BackgroundEffects.Trim, _policy.Update(false, true, false, S(15)));
        Assert.Null(_policy.TrimDue);
        Assert.Equal(BackgroundEffects.None, _policy.Update(false, true, false, S(60))); // uma vez por ida ao segundo plano
        Assert.Equal(BackgroundEffects.Restore, _policy.Update(true, true, false, S(61)));

        // Alt+Tab rápido: volta antes do prazo, nada é devolvido; a próxima ida recomeça a contagem.
        Assert.Equal(BackgroundEffects.Lower, _policy.Update(false, true, false, S(70)));
        Assert.Equal(BackgroundEffects.Restore, _policy.Update(true, true, false, S(72)));
        Assert.Equal(BackgroundEffects.Lower, _policy.Update(false, true, false, S(80)));
        Assert.Equal(BackgroundEffects.None, _policy.Update(false, true, false, S(84)));
        Assert.Equal(BackgroundEffects.Trim, _policy.Update(false, true, false, S(85)));
    }

    [Fact]
    public void Disabled_setting_or_playing_media_keeps_or_restores_normal_priority()
    {
        Assert.Equal(BackgroundEffects.None, _policy.Update(false, enabled: false, mediaPlaying: false, S(0)));
        Assert.Equal(BackgroundEffects.None, _policy.Update(false, enabled: true, mediaPlaying: true, S(1))); // música tocando
        Assert.Equal(BackgroundEffects.Lower, _policy.Update(false, true, mediaPlaying: false, S(2))); // a faixa acabou
        Assert.Equal(BackgroundEffects.Restore, _policy.Update(false, enabled: false, false, S(3))); // desligou a opção em segundo plano
        Assert.False(_policy.IsLowered);
    }
}
