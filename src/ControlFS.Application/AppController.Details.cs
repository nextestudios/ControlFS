using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Icons;
using ControlFS.Core.Models;
using ControlFS.Core.Preview;

namespace ControlFS.Application;

/// <summary>Que tipo de item o painel de detalhes descreve (decide o ícone grande e as linhas).</summary>
public enum DetailsKind
{
    Folder,
    File,
    Image,
    Archive,
    ArchiveEntry,
    Drive,
    Place,
}

/// <summary>Símbolo de cada linha do painel (a view escolhe o glifo; nunca emoji).</summary>
public enum DetailsIcon
{
    Location,
    Items,
    Size,
    Date,
    Format,
    Dimensions,
    FileSystem,
    Free,
    Compression,
    Lock,
    Marked,
    Info,
    Warning,
}

/// <summary>Linha do painel: símbolo, rótulo opcional (acima do valor) e valor.</summary>
public sealed record DetailsLine(DetailsIcon Icon, string? Label, string Value);

/// <summary>
/// Painel de detalhes da lista (redesenho, fase C) para o item focado. Tudo real: contagem e soma da pasta (a mesma do
/// início), dimensões e miniatura da imagem (pelo decodificador da visualização, com os mesmos limites), formato e
/// arquivos do compactado (só quando a lista de entradas é barata de ler), uso da unidade.
/// </summary>
public sealed record ItemDetails(FileEntry Entry, DetailsKind Kind, string Title, string Subtitle, IReadOnlyList<DetailsLine> Lines,
    VolumeInfo? Volume = null, PreviewImage? Thumbnail = null, string? Note = null);

public sealed partial class AppController
{
    private readonly LruCache<string, ImageFacts> _imageFacts = new(24);
    private readonly LruCache<string, ArchiveFacts> _archiveFacts = new(64);
    private string? _detailsKey;
    /// <summary>Trabalho do item focado em andamento (descartado pela própria tarefa ao terminar).</summary>
    private CancellationTokenSource? DetailsRun { get; set; }

    /// <summary>Formato e resolução lidos do cabeçalho e a miniatura (ou o motivo de não haver).</summary>
    private sealed record ImageFacts(ImageHeaderInfo? Header, PreviewImage? Thumbnail, string? Problem);

    /// <summary>Formato detectado pelo conteúdo e, quando a lista é barata (ZIP, 7z), quantos arquivos e se há senha.</summary>
    private sealed record ArchiveFacts(ArchiveFormat Format, int? Files, bool Encrypted);

    /// <summary>
    /// Publicado pela view: o painel está à mostra (lista com espaço para ele). Só então o item focado é medido, lido ou
    /// decodificado; com o painel escondido (grade, janela estreita) nada roda.
    /// </summary>
    public bool DetailsPanelVisible { get; private set; }

    public void SetDetailsPanelVisible(bool visible)
    {
        if (DetailsPanelVisible == visible) return;
        DetailsPanelVisible = visible;
        RaiseChanged();
    }

    /// <summary>Espera antes de medir/ler o item focado: percorrer a lista rápido não dispara trabalho em cada item.</summary>
    internal TimeSpan DetailsDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Tempo máximo para ler a lista de entradas de um compactado só para contar os arquivos.</summary>
    internal TimeSpan ArchiveCountBudget { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Lado maior da miniatura decodificada (pixels).</summary>
    public const int ThumbnailSide = 720;

    /// <summary>Item descrito pelo painel: o focado na lista (ou no início); nada enquanto a pasta carrega.</summary>
    private FileEntry? DetailsTarget => Screen == Screen.Home
        ? PlacesFocus >= 0 && PlacesFocus < Places.Count ? Places[PlacesFocus] : null
        : ActivePane is { IsLoading: false } pane ? pane.List.Focused : null;

    /// <summary>Detalhes do item focado (null: nada focado ou pasta carregando).</summary>
    public ItemDetails? Details => DetailsTarget is { } entry ? BuildDetails(entry) : null;

    private ItemDetails BuildDetails(FileEntry entry)
    {
        var lines = new List<DetailsLine>();
        var inRecycleBin = Screen != Screen.Home && ActivePane.Location is RecycleBinLocation;
        ItemDetails Done(DetailsKind kind, string subtitle, VolumeInfo? volume = null, PreviewImage? thumbnail = null, string? note = null)
        {
            AddMarkedSummary(lines);
            return new ItemDetails(entry, kind, entry.Name, subtitle, lines, volume, thumbnail, note);
        }

        if (entry.IsBlocked)
        {
            if (entry.FullPath is { } blockedPath) lines.Add(new DetailsLine(DetailsIcon.Location, null, blockedPath));
            lines.Add(new DetailsLine(DetailsIcon.Warning, "Bloqueado", entry.BlockedReason!));
            return Done(entry.IsContainer ? DetailsKind.Folder : DetailsKind.File, entry.IsContainer ? "Pasta indisponível" : "Item bloqueado");
        }

        if (entry.Kind == EntryKind.Drive)
        {
            if (entry.FullPath is { } root) lines.Add(new DetailsLine(DetailsIcon.Location, null, root));
            if (entry.Volume is { TotalBytes: > 0 } volume)
            {
                if (volume.FileSystem is { Length: > 0 } fs) lines.Add(new DetailsLine(DetailsIcon.FileSystem, "Sistema de arquivos", fs));
                lines.Add(new DetailsLine(DetailsIcon.Size, "Capacidade", EntryText.Size(volume.TotalBytes)));
                lines.Add(new DetailsLine(DetailsIcon.Free, "Livre", string.Create(EntryText.Culture, $"{EntryText.Size(volume.FreeBytes)} ({1 - volume.UsedFraction:P0})")));
                lines.Add(new DetailsLine(DetailsIcon.Items, "Usado", string.Create(EntryText.Culture, $"{EntryText.Size(volume.UsedBytes)} ({volume.UsedFraction:P0})")));
                return Done(DetailsKind.Drive, EntryText.TypeName(entry), volume);
            }
            lines.Add(new DetailsLine(DetailsIcon.Info, null, entry.Detail ?? "Unidade sem informações de espaço"));
            return Done(DetailsKind.Drive, EntryText.TypeName(entry));
        }

        // Recentes e Lixeira (locais do ControlFS, sem caminho próprio).
        if (entry.Kind == EntryKind.KnownFolder && entry.FullPath is null)
        {
            if (entry.Detail is { Length: > 0 } detail) lines.Add(new DetailsLine(DetailsIcon.Info, null, detail));
            return Done(DetailsKind.Place, entry.Id == RecycleBinLocation.PlaceId ? "Lixeira do Windows" : "Local do ControlFS");
        }

        if (inRecycleBin)
        {
            if (entry.FoundIn is { } original) lines.Add(new DetailsLine(DetailsIcon.Location, "Local original", original));
            if (entry.Size is long deletedSize && !entry.IsContainer) lines.Add(new DetailsLine(DetailsIcon.Size, null, EntryText.Size(deletedSize)));
            if (entry.Modified is { } deleted) lines.Add(new DetailsLine(DetailsIcon.Date, "Excluído em", EntryText.FriendlyDate(deleted)));
            return Done(entry.IsContainer ? DetailsKind.Folder : DetailsKind.File, (entry.IsContainer ? "Pasta" : TypeNameOf(entry)) + " na Lixeira");
        }

        if (entry.Kind is EntryKind.ArchiveDirectory or EntryKind.ArchiveFile && ActivePane is { Location: ArchiveLocation archive, Archive: { } tree })
        {
            var inner = ArchiveTree.PathFromId(entry.Id);
            lines.Add(new DetailsLine(DetailsIcon.Location, "No compactado", Path.GetFileName(archive.ArchivePath) + " › " + inner));
            if (entry.Kind == EntryKind.ArchiveDirectory)
            {
                var (files, folders, bytes) = CountArchiveFolder(tree, inner);
                lines.Add(new DetailsLine(DetailsIcon.Items, null, EntryText.Items(files + folders)));
                lines.Add(new DetailsLine(DetailsIcon.Size, null, EntryText.Size(bytes)));
            }
            else
            {
                if (entry.Size is long unpacked) lines.Add(new DetailsLine(DetailsIcon.Size, null, EntryText.Size(unpacked)));
                if (entry.Detail is { Length: > 0 } compression) lines.Add(new DetailsLine(DetailsIcon.Compression, "Compressão", compression));
                if (entry.IsEncrypted) lines.Add(new DetailsLine(DetailsIcon.Lock, null, "Protegido por senha"));
            }
            AddModified(lines, entry);
            return Done(entry.Kind == EntryKind.ArchiveDirectory ? DetailsKind.Folder : DetailsKind.ArchiveEntry,
                entry.Kind == EntryKind.ArchiveDirectory ? "Pasta no compactado" : TypeNameOf(entry) + " no compactado");
        }

        if (entry.IsContainer)
        {
            if (entry.FullPath is { } folder)
            {
                lines.Add(new DetailsLine(DetailsIcon.Location, null, folder));
                var stats = FolderStatsFor(folder);
                lines.Add(new DetailsLine(DetailsIcon.Items, null, stats.State switch
                {
                    FolderStatsState.Ready => EntryText.Items(stats.Items),
                    FolderStatsState.Partial => EntryText.Items(stats.Items) + "+",
                    FolderStatsState.Unavailable => "Conteúdo indisponível",
                    _ => "Calculando…",
                }));
                if (stats.State is FolderStatsState.Ready or FolderStatsState.Partial or FolderStatsState.Calculating)
                    lines.Add(new DetailsLine(DetailsIcon.Size, null, EntryText.FolderSize(stats)));
            }
            AddModified(lines, entry);
            return Done(DetailsKind.Folder, TypeNameOf(entry));
        }

        // Arquivos.
        if (entry.FullPath is { } path) lines.Add(new DetailsLine(DetailsIcon.Location, null, path));
        var key = FactsKey(entry);
        if (IsPreviewableImage(entry))
        {
            var facts = key is not null && _imageFacts.TryGet(key, out var cached) ? cached : null;
            if (facts?.Header is { } header)
            {
                lines.Add(new DetailsLine(DetailsIcon.Format, "Formato", header.Format.ToString().ToUpperInvariant()));
                lines.Add(new DetailsLine(DetailsIcon.Dimensions, "Dimensões", string.Create(EntryText.Culture, $"{header.Width:N0} × {header.Height:N0} px")));
            }
            else if (facts is null) lines.Add(new DetailsLine(DetailsIcon.Dimensions, "Dimensões", "Calculando…"));
            AddSizeAndModified(lines, entry);
            return Done(DetailsKind.Image, TypeNameOf(entry), thumbnail: facts?.Thumbnail, note: facts?.Problem);
        }
        if (ArchiveFormats.HasExtractableExtension(entry.Name))
        {
            var facts = key is not null && _archiveFacts.TryGet(key, out var cached) ? cached : null;
            if (facts is null) lines.Add(new DetailsLine(DetailsIcon.Format, "Formato", "Calculando…"));
            else if (facts.Format == ArchiveFormat.Unknown) lines.Add(new DetailsLine(DetailsIcon.Warning, "Formato", "não reconhecido pelo conteúdo"));
            else
            {
                lines.Add(new DetailsLine(DetailsIcon.Format, "Formato", ArchiveFormats.DisplayName(facts.Format)));
                if (facts.Files is int files) lines.Add(new DetailsLine(DetailsIcon.Items, null, files == 1 ? "1 arquivo" : string.Create(EntryText.Culture, $"{files:N0} arquivos")));
                if (facts.Encrypted) lines.Add(new DetailsLine(DetailsIcon.Lock, null, "Contém itens protegidos por senha"));
            }
            AddSizeAndModified(lines, entry);
            return Done(DetailsKind.Archive, TypeNameOf(entry));
        }
        AddSizeAndModified(lines, entry);
        var attributes = new List<string>();
        if (entry.IsReadOnly) attributes.Add("somente leitura");
        if (entry.IsHidden) attributes.Add("oculto");
        if (entry.IsSystem) attributes.Add("sistema");
        if (entry.IsReparsePoint) attributes.Add("link");
        if (attributes.Count > 0) lines.Add(new DetailsLine(DetailsIcon.Info, "Atributos", string.Join(", ", attributes)));
        return Done(DetailsKind.File, TypeNameOf(entry));
    }

    private static void AddSizeAndModified(List<DetailsLine> lines, FileEntry entry)
    {
        if (entry.Size is long size) lines.Add(new DetailsLine(DetailsIcon.Size, null, EntryText.Size(size)));
        AddModified(lines, entry);
    }

    private static void AddModified(List<DetailsLine> lines, FileEntry entry)
    {
        if (entry.Modified is { } modified) lines.Add(new DetailsLine(DetailsIcon.Date, "Modificado em", EntryText.FriendlyDate(modified)));
    }

    /// <summary>Itens marcados na pasta: quantos e quanto somam os arquivos (as pastas não são somadas aqui).</summary>
    private void AddMarkedSummary(List<DetailsLine> lines)
    {
        if (Screen == Screen.Home || ActivePane.List.SelectionCount == 0) return;
        var marked = ActivePane.List.SelectedEntries;
        var bytes = marked.Where(e => !e.IsContainer).Sum(e => e.Size ?? 0);
        var text = marked.Count == 1 ? "1 item marcado" : string.Create(EntryText.Culture, $"{marked.Count:N0} itens marcados");
        if (bytes > 0) text += " · " + EntryText.Size(bytes) + (marked.Any(e => e.IsContainer) ? " em arquivos" : string.Empty);
        lines.Add(new DetailsLine(DetailsIcon.Marked, null, text));
    }

    /// <summary>Arquivos, pastas e bytes declarados sob uma pasta do compactado (a árvore já está na memória).</summary>
    private static (long Files, long Folders, long Bytes) CountArchiveFolder(ArchiveTree tree, string inner)
    {
        long files = 0, folders = 0, bytes = 0;
        var pending = new Stack<string>();
        pending.Push(inner);
        while (pending.Count > 0)
        {
            foreach (var child in tree.Children(pending.Pop()))
            {
                if (child.Kind == EntryKind.ArchiveDirectory)
                {
                    folders++;
                    pending.Push(ArchiveTree.PathFromId(child.Id));
                }
                else
                {
                    files++;
                    bytes += child.Size ?? 0;
                }
            }
        }
        return (files, folders, bytes);
    }

    /// <summary>Chave dos dados lidos de um arquivo: caminho + data + tamanho (arquivo trocado relê).</summary>
    private static string? FactsKey(FileEntry entry) =>
        entry.FullPath is { } path ? $"{path}|{entry.Modified?.UtcTicks}|{entry.Size}" : null;

    /// <summary>
    /// Chamado a cada mudança de estado: com o painel à mostra, começa (depois de uma pausa curta) o trabalho que o item
    /// focado precisa e ainda não tem; mudar o foco cancela o trabalho do item anterior.
    /// </summary>
    private void UpdateDetailsWork()
    {
        var target = DetailsPanelVisible && !IsGrid ? DetailsTarget : null;
        var key = target is null ? null : DetailsWorkKey(target);
        if (key == _detailsKey) return;
        _detailsKey = key;
        DetailsRun?.Cancel();
        if (target is null || key is null) return;
        var cts = DetailsRun = new CancellationTokenSource();
        Track(LoadDetailsAsync(target, key, cts));
    }

    /// <summary>O trabalho que falta para o item (null: nada a fazer, já está guardado).</summary>
    private string? DetailsWorkKey(FileEntry entry)
    {
        if (entry.IsBlocked || entry.FullPath is not { } path || (Screen != Screen.Home && ActivePane.Location is RecycleBinLocation)) return null;
        if (entry.Kind is EntryKind.Directory or EntryKind.KnownFolder)
        {
            if (Screen == Screen.Home && IsHomeStatFolder(path)) return null; // a soma do início já cuida
            return _folderStats.TryGetValue(path, out var cached) && Clock() - cached.At <= FolderStatsLifetime ? null : "folder:" + path;
        }
        if (entry.Kind != EntryKind.File || FactsKey(entry) is not { } facts) return null;
        if (IsPreviewableImage(entry)) return _imageFacts.TryGet(facts, out _) ? null : "image:" + facts;
        if (ArchiveFormats.HasExtractableExtension(entry.Name)) return _archiveFacts.TryGet(facts, out _) ? null : "archive:" + facts;
        return null;
    }

    private async Task LoadDetailsAsync(FileEntry entry, string key, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            if (DetailsDelay > TimeSpan.Zero) await Task.Delay(DetailsDelay, token);
            var path = entry.FullPath!;
            if (key.StartsWith("folder:", StringComparison.Ordinal))
            {
                if (await MeasureStatsAsync(path, token) is not { } stats) return;
                _folderStats[path] = (stats, Clock());
                FolderStatsVersion++;
            }
            else if (key.StartsWith("image:", StringComparison.Ordinal))
                _imageFacts.Set(FactsKey(entry)!, await ReadImageFactsAsync(path, token));
            else
                _archiveFacts.Set(FactsKey(entry)!, await ReadArchiveFactsAsync(path, token));
        }
        catch (OperationCanceledException)
        {
            return; // outro item focado: nada guardado, recomeça se ele voltar
        }
        finally
        {
            if (ReferenceEquals(DetailsRun, cts))
            {
                DetailsRun = null;
                _detailsKey = null;
            }
            cts.Dispose();
        }
        RaiseChanged();
    }

    private async Task<ImageFacts> ReadImageFactsAsync(string path, CancellationToken token)
    {
        var limits = PreviewLimits;
        ImageHeaderInfo? header = null;
        try
        {
            // Os mesmos limites da visualização (#57): cabeçalho e resolução conferidos antes de decodificar.
            header = await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
                return ImagePreviewPolicy.Inspect(stream, limits);
            }, token);
            if (_imageDecoder is null) return new ImageFacts(header, null, null);
            var thumbnail = await _imageDecoder.DecodeAsync(path, Math.Min(ThumbnailSide, limits.MaxDecodedSide), token);
            return new ImageFacts(header, thumbnail, null);
        }
        catch (PreviewException ex)
        {
            return new ImageFacts(header, null, "Sem miniatura: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImageFacts(header, null, "Não foi possível ler a imagem: " + ex.Message);
        }
    }

    private async Task<ArchiveFacts> ReadArchiveFactsAsync(string path, CancellationToken token)
    {
        ArchiveFormat format;
        try
        {
            format = await Task.Run(() => _archives.Detect(path), token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ArchiveFacts(ArchiveFormat.Unknown, null, false);
        }
        // Só ZIP e 7z guardam a lista de entradas num índice que se lê sem descompactar; os outros ficam sem contagem.
        if (format is not (ArchiveFormat.Zip or ArchiveFormat.SevenZip)) return new ArchiveFacts(format, null, false);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(ArchiveCountBudget);
        try
        {
            var info = await _archives.InspectAsync(path, null, Limits, budget.Token);
            return new ArchiveFacts(format, info.Entries.Count(e => !e.IsDirectory), info.HasEncryptedEntries);
        }
        catch (ArchiveAccessException ex) when (ex.Kind is OperationErrorKind.PasswordRequired or OperationErrorKind.WrongPassword)
        {
            return new ArchiveFacts(format, null, true); // lista protegida: só com a senha
        }
        catch (ArchiveAccessException)
        {
            return new ArchiveFacts(format, null, false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new ArchiveFacts(format, null, false); // demorou demais: sem contagem
        }
    }
}
