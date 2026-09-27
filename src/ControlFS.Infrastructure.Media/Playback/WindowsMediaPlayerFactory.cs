using System.Globalization;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using CoreState = ControlFS.Core.Contracts.MediaPlaybackState;
using WinState = Windows.Media.Playback.MediaPlaybackState;

namespace ControlFS.Infrastructure.Media.Playback;

/// <summary>
/// Reprodução com o <see cref="MediaPlayer"/> do Windows (Media Foundation), só com os codecs instalados. O arquivo é
/// entregue como fluxo (nunca como URL: nada de rede, nada de caminhos UNC resolvidos pelo reprodutor) e fica aberto
/// compartilhado enquanto tocar. Os controles de mídia do sistema ficam desligados: o controle do ControlFS manda.
/// </summary>
public sealed class WindowsMediaPlayerFactory : IMediaPlayerFactory
{
    public IMediaSession Open(string path, MediaKind kind, string? subtitlePath = null) => new Session(path, kind, subtitlePath);

    private sealed class Session : IMediaSession
    {
        private readonly MediaPlayer _player;
        private readonly FileStream _file;
        private readonly IRandomAccessStream _stream;
        private readonly MediaSource _source;
        private readonly MediaPlaybackItem _item;
        private readonly List<IDisposable> _extras = [];
        private volatile string? _error;
        private volatile bool _ended;
        private volatile bool _opened;
        private int _subtitle = -1;
        private bool _disposed;

        public Session(string path, MediaKind kind, string? subtitlePath)
        {
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            try
            {
                _stream = _file.AsRandomAccessStream();
                _source = MediaSource.CreateFromStream(_stream, MediaPreviewPolicy.ContentType(path));
                if (subtitlePath is not null) AddSubtitle(subtitlePath);
                _item = new MediaPlaybackItem(_source);
                _item.TimedMetadataTracksChanged += (_, _) => ApplySubtitle();
                _player = new MediaPlayer
                {
                    AutoPlay = true,
                    AudioCategory = kind == MediaKind.Video ? MediaPlayerAudioCategory.Movie : MediaPlayerAudioCategory.Media,
                    IsLoopingEnabled = false,
                };
                _player.CommandManager.IsEnabled = false;
                _player.MediaOpened += (_, _) => _opened = true;
                _player.MediaEnded += (_, _) => _ended = true;
                _player.MediaFailed += (_, e) => _error = Describe(e.Error, e.ExtendedErrorCode);
                _player.Source = _item;
            }
            catch
            {
                DisposeCore();
                throw;
            }
        }

        /// <summary>Legenda externa (.srt/.vtt ao lado do vídeo), lida como fluxo local.</summary>
        private void AddSubtitle(string subtitlePath)
        {
            try
            {
                var file = new FileStream(subtitlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024);
                _extras.Add(file);
                var stream = file.AsRandomAccessStream();
                _extras.Add(stream);
                _source.ExternalTimedTextSources.Add(TimedTextSource.CreateFromStream(stream));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Sem a legenda externa: o vídeo toca do mesmo jeito.
            }
        }

        public object? VideoSurface => _player;

        public MediaStatus Status
        {
            get
            {
                if (_disposed) return new(CoreState.Paused, TimeSpan.Zero, TimeSpan.Zero, 0, true);
                var session = _player.PlaybackSession;
                var state = _error is not null ? CoreState.Failed
                    : _ended ? CoreState.Ended
                    : session.PlaybackState switch
                    {
                        WinState.Playing => CoreState.Playing,
                        WinState.Paused => CoreState.Paused,
                        WinState.Buffering => CoreState.Buffering,
                        _ => _opened ? CoreState.Paused : CoreState.Opening,
                    };
                return new(state, session.Position, session.NaturalDuration, _player.Volume, _player.IsMuted, _error,
                    AudioTracks(), _item.AudioTracks.SelectedIndex, Subtitles(), _subtitle,
                    session.NaturalVideoWidth > 0 && session.NaturalVideoHeight > 0);
            }
        }

        private List<MediaTrack> AudioTracks()
        {
            var tracks = new List<MediaTrack>();
            for (var i = 0; i < _item.AudioTracks.Count; i++)
                tracks.Add(new MediaTrack(i, Label(_item.AudioTracks[i].Label, _item.AudioTracks[i].Language, "Faixa", i)));
            return tracks;
        }

        /// <summary>Legendas e closed captions, na ordem do arquivo (as externas vêm por último).</summary>
        private List<MediaTrack> Subtitles()
        {
            var tracks = new List<MediaTrack>();
            for (var i = 0; i < _item.TimedMetadataTracks.Count; i++)
            {
                var track = _item.TimedMetadataTracks[i];
                if (track.TimedMetadataKind is TimedMetadataKind.Subtitle or TimedMetadataKind.Caption)
                    tracks.Add(new MediaTrack(i, Label(track.Label, track.Language, "Legenda", tracks.Count)));
            }
            return tracks;
        }

        private static string Label(string? label, string? language, string fallback, int index)
        {
            if (!string.IsNullOrWhiteSpace(label)) return label;
            if (!string.IsNullOrWhiteSpace(language))
            {
                try { return CultureInfo.GetCultureInfo(language).NativeName; }
                catch (CultureNotFoundException) { return language; }
            }
            return string.Create(CultureInfo.CurrentCulture, $"{fallback} {index + 1}");
        }

        public void Play()
        {
            if (_disposed) return;
            _ended = false;
            _player.Play();
        }

        public void Pause()
        {
            if (!_disposed) _player.Pause();
        }

        public void Seek(TimeSpan position)
        {
            if (_disposed || !_player.PlaybackSession.CanSeek) return;
            _ended = false;
            _player.PlaybackSession.Position = position;
        }

        public void SetVolume(double volume)
        {
            if (!_disposed) _player.Volume = Math.Clamp(volume, 0, 1);
        }

        public void SetMuted(bool muted)
        {
            if (!_disposed) _player.IsMuted = muted;
        }

        public void SelectAudioTrack(int index)
        {
            if (!_disposed && index >= 0 && index < _item.AudioTracks.Count) _item.AudioTracks.SelectedIndex = index;
        }

        public void SelectSubtitle(int index)
        {
            if (_disposed) return;
            _subtitle = index;
            ApplySubtitle();
        }

        /// <summary>Só a legenda escolhida é desenhada pelo reprodutor; as demais ficam desligadas.</summary>
        private void ApplySubtitle()
        {
            if (_disposed) return;
            for (var i = 0; i < _item.TimedMetadataTracks.Count; i++)
                _item.TimedMetadataTracks.SetPresentationMode((uint)i, i == _subtitle ? TimedMetadataTrackPresentationMode.PlatformPresented : TimedMetadataTrackPresentationMode.Disabled);
        }

        private static string Describe(MediaPlayerError error, Exception? extended) => error switch
        {
            MediaPlayerError.SourceNotSupported or MediaPlayerError.DecodingError =>
                "Este formato ou codec não é suportado neste Windows (pode faltar uma extensão de mídia da Microsoft Store). Abra no aplicativo padrão, se tiver um.",
            _ => "Não foi possível reproduzir este arquivo" + (extended is null ? "." : $" ({extended.Message.Trim()})."),
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _player.Pause();
                _player.Source = null;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                // O reprodutor já falhou; liberar continua abaixo.
            }
            DisposeCore();
        }

        private void DisposeCore()
        {
            _player?.Dispose();
            _source?.Dispose();
            _stream?.Dispose();
            foreach (var extra in _extras) extra.Dispose();
            _file.Dispose();
        }
    }
}
