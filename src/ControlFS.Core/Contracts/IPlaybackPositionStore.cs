namespace ControlFS.Core.Contracts;

/// <summary>
/// Onde um vídeo parou (#170). <see cref="Key"/> é um resumo (SHA-256) do caminho, do tamanho e da data de modificação:
/// o arquivo não aparece em texto e outro arquivo com o mesmo nome (ou o mesmo arquivo editado) não herda a posição.
/// </summary>
public sealed record PlaybackPosition(string Key, TimeSpan Position, DateTimeOffset Updated);

/// <summary>Posições salvas localmente (dados do app); nada sai do computador.</summary>
public interface IPlaybackPositionStore
{
    IReadOnlyList<PlaybackPosition> Load();

    void Save(IReadOnlyList<PlaybackPosition> positions);
}
