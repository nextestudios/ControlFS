using ControlFS.Core.Actions;
using ControlFS.Core.Audio;
using Xunit;

namespace ControlFS.UnitTests.Core;

public sealed class ControllerSoundsTests
{
    private sealed class Recorder : ISoundPlayer
    {
        public List<(SoundCue Cue, int Volume)> Played { get; } = [];
        public void Play(SoundCue cue, int volume) => Played.Add((cue, volume));
    }

    [Fact]
    public void Volume_zero_is_silent()
    {
        var player = new Recorder();
        var sounds = new ControllerSounds(player, () => 0, () => TimeSpan.Zero);
        sounds.OnAction(InputAction.Confirm);
        Assert.Empty(player.Played);
    }

    [Fact]
    public void Sounds_are_on_by_default_keep_a_saved_choice_and_read_the_old_0_13_field()
    {
        var fresh = new ControlFS.Core.Contracts.AppSettings();
        Assert.Equal(ControlFS.Core.Contracts.AppSettings.DefaultControllerSoundVolume, fresh.EffectiveControllerSoundVolume); // #286: ligados de fábrica
        Assert.Equal(0, (fresh with { ControllerSoundLevel = 0 }).EffectiveControllerSoundVolume); // desligados escolhidos: seguem desligados
        Assert.Equal(25, (fresh with { ControllerSoundLevel = 25 }).EffectiveControllerSoundVolume);
        Assert.Equal(75, (fresh with { ControllerSoundVolume = 75 }).EffectiveControllerSoundVolume); // escolha da 0.13 vale
        Assert.Equal(50, (fresh with { ControllerSoundVolume = 0 }).EffectiveControllerSoundVolume); // 0 da 0.13 era o padrão de então
        Assert.Equal(0, (fresh with { ControllerSoundVolume = 75, ControllerSoundLevel = 0 }).EffectiveControllerSoundVolume); // a escolha nova vence
    }

    [Fact]
    public void Each_kind_of_action_has_its_own_cue_and_the_analog_scroll_is_silent()
    {
        Assert.Equal(SoundCue.Move, ControllerSounds.CueFor(InputAction.NavigateDown));
        Assert.Equal(SoundCue.Move, ControllerSounds.CueFor(InputAction.PageUp));
        Assert.Equal(SoundCue.Confirm, ControllerSounds.CueFor(InputAction.Confirm));
        Assert.Equal(SoundCue.Back, ControllerSounds.CueFor(InputAction.Back));
        Assert.Equal(SoundCue.Select, ControllerSounds.CueFor(InputAction.ToggleSelection));
        Assert.All([InputAction.ScrollUp, InputAction.ScrollDown, InputAction.ScrollLeft, InputAction.ScrollRight], a => Assert.Null(ControllerSounds.CueFor(a)));
    }

    [Fact]
    public void Holding_the_dpad_does_not_machine_gun_the_same_cue_but_other_cues_still_play()
    {
        var player = new Recorder();
        var now = TimeSpan.Zero;
        var sounds = new ControllerSounds(player, () => 60, () => now);
        for (var i = 0; i < 40; i++) // 40 repetições a cada 10 ms = 400 ms segurando
        {
            sounds.OnAction(InputAction.NavigateDown);
            now += TimeSpan.FromMilliseconds(10);
        }
        Assert.InRange(player.Played.Count, 4, 6); // ~1 a cada 80 ms
        Assert.All(player.Played, p => Assert.Equal((SoundCue.Move, 60), p));

        sounds.OnAction(InputAction.Confirm); // outro som passa na hora
        Assert.Equal(SoundCue.Confirm, player.Played[^1].Cue);
    }

    [Fact]
    public void A_failing_audio_system_never_breaks_the_action()
    {
        var sounds = new ControllerSounds(new Throwing(), () => 50, () => TimeSpan.Zero);
        sounds.OnAction(InputAction.Confirm); // não lança
    }

    private sealed class Throwing : ISoundPlayer
    {
        public void Play(SoundCue cue, int volume) => throw new InvalidOperationException("sem dispositivo");
    }

    [Fact]
    public void Synthesized_cues_are_short_valid_wav_files_quieter_at_lower_volume_and_different_from_each_other()
    {
        var bytes = Enum.GetValues<SoundCue>().ToDictionary(c => c, c => SoundBank.Render(c, 50));
        foreach (var (cue, wav) in bytes)
        {
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
            var data = BitConverter.ToInt32(wav, 40);
            Assert.Equal(wav.Length - 44, data);
            Assert.InRange(data / 2.0 / SoundBank.SampleRate, 0.02, 0.35); // curtos: 20 a 350 ms
            Assert.True(Peak(wav) > 500, $"{cue}: sem sinal");
        }
        Assert.True(Peak(SoundBank.Render(SoundCue.Confirm, 100)) > Peak(SoundBank.Render(SoundCue.Confirm, 25)) * 3);
        Assert.InRange(Peak(SoundBank.Render(SoundCue.Confirm, 100)), 1, (int)(short.MaxValue * 0.8)); // discreto mesmo no máximo
        Assert.Equal(4, bytes.Values.Select(Convert.ToBase64String).Distinct().Count());
    }

    [Fact]
    public void Every_cue_starts_and_ends_in_silence_stays_soft_and_the_volume_setting_scales_it()
    {
        foreach (var cue in Enum.GetValues<SoundCue>())
        {
            var wav = SoundBank.Render(cue, 100);
            var peak = Peak(wav);
            Assert.True(Math.Abs((int)BitConverter.ToInt16(wav, 44)) < peak * 0.05, $"{cue}: começa fora do silêncio");
            Assert.True(Math.Abs((int)BitConverter.ToInt16(wav, wav.Length - 2)) < peak * 0.05, $"{cue}: termina fora do silêncio");
            Assert.True(Rms(wav) < 0.25, $"{cue}: alto demais");
        }
        Assert.InRange(Rms(SoundBank.Render(SoundCue.Move, 100)) / Rms(SoundBank.Render(SoundCue.Move, 50)), 1.95, 2.05); // médio = o nível do Console Mode; máximo = o dobro
        Assert.True(Rms(SoundBank.Render(SoundCue.Move, 100)) > Rms(SoundBank.Render(SoundCue.Move, 25)) * 3);
    }

    private static double Rms(byte[] wav)
    {
        double sum = 0;
        var n = 0;
        for (var i = 44; i + 1 < wav.Length; i += 2, n++) { var s = BitConverter.ToInt16(wav, i) / 32768.0; sum += s * s; }
        return Math.Sqrt(sum / n);
    }

    private static int Peak(byte[] wav)
    {
        var peak = 0;
        for (var i = 44; i + 1 < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        return peak;
    }
}
