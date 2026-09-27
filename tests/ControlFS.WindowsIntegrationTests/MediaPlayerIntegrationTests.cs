using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Media.Playback;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// O MediaPlayer do Windows de verdade com um WAV gerado aqui. Ouvir o som não dá para provar no CI (o runner pode não ter
/// saída de áudio: aí o teste é pulado com o motivo); abrir pelo fluxo, ler a duração, pausar, buscar e liberar o arquivo sim.
/// </summary>
public sealed class MediaPlayerIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-media-tests", Guid.NewGuid().ToString("N"));

    public MediaPlayerIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Opens_a_local_wav_as_a_stream_reads_its_duration_seeks_and_releases_the_file_on_dispose()
    {
        var path = Path.Join(_root, "tom.wav");
        File.WriteAllBytes(path, Wav(seconds: 2));
        var session = new WindowsMediaPlayerFactory().Open(path, MediaKind.Audio);
        try
        {
            for (var i = 0; i < 100 && session.Status.State == MediaPlaybackState.Opening; i++)
                await Task.Delay(100, TestContext.Current.CancellationToken);
            var status = session.Status;
            if (status.State == MediaPlaybackState.Failed) Assert.Skip("Sem saída de áudio neste runner: " + status.Error);
            Assert.NotEqual(MediaPlaybackState.Opening, status.State);
            Assert.InRange(status.Duration.TotalSeconds, 1.9, 2.1);
            Assert.False(status.HasVideo);

            session.Pause();
            session.Seek(TimeSpan.FromSeconds(1));
            for (var i = 0; i < 50 && session.Status.Position < TimeSpan.FromSeconds(0.9); i++)
                await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.InRange(session.Status.Position.TotalSeconds, 0.9, 2.1);
        }
        finally
        {
            session.Dispose();
        }
        File.Delete(path); // liberado na hora
        Assert.False(File.Exists(path));
    }

    /// <summary>WAV PCM 16 bits mono 8 kHz com um tom de 440 Hz.</summary>
    private static byte[] Wav(int seconds)
    {
        const int rate = 8000;
        var samples = rate * seconds;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
        writer.Flush();
        return memory.ToArray();
    }
}
