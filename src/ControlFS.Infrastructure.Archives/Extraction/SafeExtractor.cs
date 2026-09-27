using System.Buffers;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Engines;
using ControlFS.Infrastructure.Archives.Security;
using ControlFS.Infrastructure.Windows.FileSystem;

namespace ControlFS.Infrastructure.Archives.Extraction;

/// <summary>
/// Extração com contenção de caminhos, bloqueio de links, detecção de colisões, limites
/// sobre bytes efetivamente escritos, staging privado no mesmo volume, verificação CRC,
/// conflitos resolvidos pelo usuário (sem sobrescrita silenciosa) e cancelamento cooperativo.
/// Nunca apaga o compactado original nem executa conteúdo extraído.
/// </summary>
public sealed class SafeExtractor(IArchiveEngine engine, ITemporaryJournal? journal = null)
{
    public const string StagingPrefix = TemporaryJournal.StagingPrefix;
    public const string ManifestName = TemporaryJournal.ManifestName;
    private readonly ITemporaryJournal _journal = journal ?? NoTemporaryJournal.Instance;
    private const int BufferSize = 81920;

    public async Task<OperationResult> ExtractAsync(ArchiveFormat format, ExtractionRequest request, IExtractionInteraction interaction,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        request.Limits.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Limits.MaxDuration);
        try
        {
            return await Task.Run(() => RunAsync(format, request, interaction, progress, timeout.Token, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OperationResult(OperationState.Failed, [], OperationErrorKind.LimitExceeded, "Tempo máximo de processamento atingido.");
        }
        catch (OperationCanceledException)
        {
            return new OperationResult(OperationState.Cancelled, [], OperationErrorKind.Cancelled, "Operação cancelada antes de gravar qualquer arquivo.");
        }
    }

    /// <summary>
    /// Teste de integridade: o mesmo caminho de leitura da extração (limites, tamanho, CRC), com os dados descartados.
    /// Nada é gravado em disco, nem staging. Não é verificação de vírus.
    /// </summary>
    public async Task<OperationResult> TestAsync(ArchiveFormat format, ArchiveTestRequest request, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        request.Limits.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Limits.MaxDuration);
        try
        {
            return await Task.Run(() => RunTest(format, request, progress, timeout.Token, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OperationResult(OperationState.Failed, [], OperationErrorKind.LimitExceeded, "Tempo máximo de processamento atingido.");
        }
        catch (OperationCanceledException)
        {
            return new OperationResult(OperationState.Cancelled, [], OperationErrorKind.Cancelled, "Teste cancelado.");
        }
    }

    private OperationResult RunTest(ArchiveFormat format, ArchiveTestRequest request, IProgress<OperationProgress>? progress, CancellationToken ct, CancellationToken userToken)
    {
        IArchiveReadSession session;
        try
        {
            session = engine.Open(request.ArchivePath, format, request.Password, request.Limits, ct);
        }
        catch (ArchiveAccessException ex)
        {
            return Fail(ex.Kind, ex.Message);
        }

        using (session)
        {
            var files = session.Info.Entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Index);
            if (files.Values.Any(e => e.IsEncrypted) && string.IsNullOrEmpty(request.Password))
                return Fail(OperationErrorKind.PasswordRequired, "O arquivo está protegido por senha.");
            var names = files.Values.ToDictionary(e => e.Index, e => ArchivePathPolicy.Sanitize(e.RawKey, request.Limits.MaxDepth, request.Limits.MaxRelativePathLength) is { IsAccepted: true } ok
                ? ok.RelativePath : Printable(e.RawKey));
            var declared = files.Values.Sum(e => e.Size ?? 0);
            var results = new List<ItemResult>();
            var state = new RunState();
            var fatal = OperationErrorKind.None;
            string? fatalMessage = null;
            var cancelled = false;
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                using var entries = session.ReadFiles(files.Keys.ToHashSet(), ct).GetEnumerator();
                while (fatal == OperationErrorKind.None)
                {
                    if (ct.IsCancellationRequested) { cancelled = true; break; }
                    bool hasNext;
                    try
                    {
                        hasNext = entries.MoveNext();
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        (fatal, fatalMessage) = ErrorMapper.Map(ex, files.Values.Any(e => e.IsEncrypted));
                        if (fatal is OperationErrorKind.Unknown) fatal = OperationErrorKind.Corrupt;
                        break;
                    }
                    if (!hasNext) break;
                    var (entry, open) = entries.Current;
                    if (!names.TryGetValue(entry.Index, out var name)) continue;
                    progress?.Report(new OperationProgress(name, state.FilesDone, files.Count, state.TotalBytes, declared));
                    try
                    {
                        bool hasCrc;
                        using (var source = open()) hasCrc = CopyVerified(source, Stream.Null, entry, request.Limits, state, buffer, ct);
                        results.Add(new ItemResult(name, ItemOutcome.Succeeded, Message: hasCrc ? "CRC confere." : "Lida por completo; o formato não guarda checksum.") { NoChecksum = !hasCrc });
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    catch (Exception ex)
                    {
                        var (kind, message) = ErrorMapper.Map(ex, entry.IsEncrypted);
                        results.Add(new ItemResult(name, ItemOutcome.Failed, kind, message));
                        if (IsFatal(kind))
                        {
                            fatal = kind;
                            fatalMessage = message;
                        }
                    }
                    state.FilesDone++;
                    if (cancelled) break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            progress?.Report(new OperationProgress(null, state.FilesDone, files.Count, state.TotalBytes, declared));
            var processed = results.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var name in names.Values)
                if (!processed.Contains(name)) results.Add(new ItemResult(name, ItemOutcome.NotProcessed));

            if (cancelled || userToken.IsCancellationRequested)
                return new OperationResult(OperationState.Cancelled, results, OperationErrorKind.Cancelled, "Teste cancelado; as entradas restantes não foram verificadas.");
            if (fatal != OperationErrorKind.None)
                return new OperationResult(OperationState.Failed, results, fatal, fatalMessage);
            var failures = results.Count(r => r.Outcome != ItemOutcome.Succeeded);
            return failures == 0
                ? new OperationResult(OperationState.Completed, results, Message: "Nenhum problema encontrado.")
                : new OperationResult(OperationState.CompletedWithWarnings, results, Message: $"{failures} entrada(s) com problema.");
        }
    }

    private async Task<OperationResult> RunAsync(ArchiveFormat format, ExtractionRequest request, IExtractionInteraction interaction,
        IProgress<OperationProgress>? progress, CancellationToken ct, CancellationToken userToken)
    {
        var limits = request.Limits;
        var destinationParent = Path.GetFullPath(request.DestinationDirectory);
        var destinationInfo = new DirectoryInfo(destinationParent);
        if (!destinationInfo.Exists)
            return Fail(OperationErrorKind.DestinationUnavailable, "A pasta de destino não existe ou não está acessível.");

        IArchiveReadSession session;
        try
        {
            session = engine.Open(request.ArchivePath, format, request.Password, limits, ct);
        }
        catch (ArchiveAccessException ex)
        {
            return Fail(ex.Kind, ex.Message);
        }

        using (session)
        {
            var plan = BuildPlan(session.Info, request, limits, out var planResults);
            if (plan.Count == 0 && planResults.Count == 0)
                return new OperationResult(OperationState.Completed, [], Message: "Nenhuma entrada selecionada.");

            var needsPassword = plan.Any(p => p.Entry is { IsEncrypted: true, IsDirectory: false });
            if (needsPassword && string.IsNullOrEmpty(request.Password))
                return Fail(OperationErrorKind.PasswordRequired, "O arquivo está protegido por senha.");

            // Espaço disponível (quando mensurável) antes de começar.
            var declared = plan.Where(p => !p.Entry.IsDirectory).Sum(p => p.Entry.Size ?? 0);
            if (TryGetFreeSpace(destinationParent) is long free && declared > free)
                return Fail(OperationErrorKind.InsufficientSpace, $"Espaço insuficiente: {Format(declared)} necessários, {Format(free)} livres.");

            string root;
            var createdRoot = false;
            try
            {
                if (request.Mode == DestinationMode.CreateDedicatedFolder)
                {
                    root = CreateDedicatedFolder(destinationParent, request.DedicatedFolderName ?? ArchiveFormats.StemOf(request.ArchivePath));
                    createdRoot = true;
                }
                else
                {
                    root = destinationParent;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var (kind, message) = ErrorMapper.Map(ex);
                return Fail(kind, message);
            }

            var guard = new DestinationGuard(root);
            var (staging, stagingRegistration) = CreateStaging(root);
            var zone = MarkOfTheWeb.Read(request.ArchivePath);
            var results = new List<ItemResult>(planResults);
            var state = new RunState();
            var fatal = OperationErrorKind.None;
            string? fatalMessage = null;
            var cancelled = false;
            var fileTotal = plan.Count(p => !p.Entry.IsDirectory);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                // Diretórios primeiro (baratos); depois os arquivos na ordem do compactado, entregues pela sessão
                // por acesso aleatório ou em sequência (formatos sólidos/stream como TAR.GZ).
                foreach (var item in plan.Where(p => p.Entry.IsDirectory))
                {
                    try { guard.EnsureDirectories(item.Components, item.Components.Count); }
                    catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
                    {
                        var (kind, message) = ErrorMapper.Map(ex);
                        results.Add(new ItemResult(item.RelativePath, ItemOutcome.Blocked, kind, message));
                    }
                }
                var files = plan.Where(p => !p.Entry.IsDirectory).ToDictionary(p => p.Entry.Index);
                using var entries = session.ReadFiles(files.Keys.ToHashSet(), ct).GetEnumerator();
                while (!cancelled && fatal == OperationErrorKind.None)
                {
                    if (ct.IsCancellationRequested) { cancelled = true; break; }
                    bool hasNext;
                    try
                    {
                        hasNext = entries.MoveNext();
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Falha ao avançar no fluxo: nada depois deste ponto pode ser lido com segurança.
                        (fatal, fatalMessage) = ErrorMapper.Map(ex, plan.Any(p => p.Entry.IsEncrypted));
                        if (fatal is OperationErrorKind.Unknown) fatal = OperationErrorKind.Corrupt;
                        break;
                    }
                    if (!hasNext) break;
                    var (entry, open) = entries.Current;
                    if (!files.TryGetValue(entry.Index, out var item)) continue;
                    try
                    {
                        progress?.Report(new OperationProgress(item.RelativePath, state.FilesDone, fileTotal, state.TotalBytes, declared));
                        var outcome = await ExtractFileAsync(open, item, guard, staging, zone, limits, interaction, state, buffer, ct).ConfigureAwait(false);
                        results.Add(outcome);
                        state.FilesDone++;
                        if (state.CancelledByUser) cancelled = true;
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    catch (Exception ex)
                    {
                        var (kind, message) = ErrorMapper.Map(ex, item.Entry.IsEncrypted);
                        results.Add(new ItemResult(item.RelativePath, kind is OperationErrorKind.DestinationTraversesLink or OperationErrorKind.PathRejected ? ItemOutcome.Blocked : ItemOutcome.Failed, kind, message));
                        if (IsFatal(kind))
                        {
                            fatal = kind;
                            fatalMessage = message;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                TryDeleteStaging(staging, stagingRegistration);
            }

            progress?.Report(new OperationProgress(null, state.FilesDone, fileTotal, state.TotalBytes, declared));

            // Itens que não chegaram a ser processados são reportados como tal (resultado parcial verdadeiro).
            var processed = results.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var item in plan)
                if (!item.Entry.IsDirectory && !processed.Contains(item.RelativePath))
                    results.Add(new ItemResult(item.RelativePath, ItemOutcome.NotProcessed));

            var anyWritten = results.Any(r => r.Outcome is ItemOutcome.Succeeded or ItemOutcome.Renamed or ItemOutcome.Replaced);
            if (createdRoot && !anyWritten) TryDeleteEmptyTree(root);
            var destination = createdRoot && !anyWritten && !Directory.Exists(root) ? null : root;

            if (cancelled || userToken.IsCancellationRequested)
                return new OperationResult(OperationState.Cancelled, results, OperationErrorKind.Cancelled,
                    "Operação cancelada. Arquivos já concluídos foram mantidos; temporários foram removidos.", destination);
            if (fatal != OperationErrorKind.None)
                return new OperationResult(OperationState.Failed, results, fatal, fatalMessage, destination);
            var warnings = results.Any(r => r.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked or ItemOutcome.NotProcessed);
            return new OperationResult(warnings ? OperationState.CompletedWithWarnings : OperationState.Completed, results, Destination: destination);
        }
    }

    private static async Task<ItemResult> ExtractFileAsync(Func<Stream> openEntry, PlannedEntry item, DestinationGuard guard, string staging, string? zone,
        ExtractionLimits limits, IExtractionInteraction interaction, RunState state, byte[] buffer, CancellationToken ct)
    {
        var parentCount = item.Components.Count - 1;
        var parent = guard.EnsureDirectories(item.Components, parentCount);
        var name = item.Components[^1];

        if (item.Entry.Size is long declaredSize && TryGetFreeSpace(parent) is long free && declaredSize > free)
            throw new FileOperationException(OperationErrorKind.InsufficientSpace, "Espaço insuficiente no destino.");

        var staged = Path.Join(staging, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using (var source = openEntry())
            using (var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                CopyVerified(source, target, item.Entry, limits, state, buffer, ct);
                target.Flush(flushToDisk: true);
            }

            if (item.Entry.Modified is DateTimeOffset modified && modified.Year is > 1980 and < 2200)
                File.SetLastWriteTimeUtc(staged, modified.UtcDateTime);

            // Colocação final com a cadeia de pastas presa e verificada por handle (ver DestinationGuard).
            return await PlaceAsync(staged, name, parentCount, guard, item, zone, interaction, state, ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    /// <summary>
    /// Lê a entrada inteira para <paramref name="sink"/>, aplicando os limites sobre os bytes efetivamente lidos, e confere
    /// o tamanho declarado e o CRC. Usado pela extração (sink = arquivo em staging) e pelo teste de integridade (sink nulo).
    /// </summary>
    /// <returns>Se havia um CRC para conferir (TAR/GZ e AES AE-2 não têm).</returns>
    private static bool CopyVerified(Stream source, Stream sink, ArchiveEntry entry, ExtractionLimits limits, RunState state, byte[] buffer, CancellationToken ct)
    {
        var crc = new Crc32();
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            written += read;
            state.TotalBytes += read;
            if (written > limits.MaxEntryBytes)
                throw new FileOperationException(OperationErrorKind.LimitExceeded, "Entrada excede o tamanho máximo configurado.");
            if (state.TotalBytes > limits.MaxTotalBytes)
                throw new FileOperationException(OperationErrorKind.LimitExceeded, "Extração excede o total máximo configurado.");
            if (entry.Size is long size && written > size)
                throw new FileOperationException(OperationErrorKind.Corrupt, "A entrada produziu mais dados que o declarado.");
            if (written > limits.RatioCheckThresholdBytes && entry.CompressedSize is > 0 and var compressed && written / (double)compressed > limits.MaxCompressionRatio)
                throw new FileOperationException(OperationErrorKind.LimitExceeded, "Relação de expansão suspeita (possível bomba de descompressão).");
            crc.Append(buffer.AsSpan(0, read));
            sink.Write(buffer, 0, read);
        }

        if (entry.Size is long expected && written != expected)
            throw new FileOperationException(entry.IsEncrypted ? OperationErrorKind.WrongPasswordOrCorrupt : OperationErrorKind.Corrupt,
                "Tamanho extraído difere do declarado (arquivo truncado ou corrompido).");
        // Entradas AES (AE-2) declaram CRC 0; nesse caso a verificação CRC não se aplica.
        var hasCrc = entry.Crc32 is uint expectedCrc && (expectedCrc != 0 || written == 0);
        if (hasCrc && crc.Value != entry.Crc32)
            throw new FileOperationException(entry.IsEncrypted ? OperationErrorKind.WrongPasswordOrCorrupt : OperationErrorKind.Corrupt,
                entry.IsEncrypted ? "Falha de integridade: senha incorreta ou dados corrompidos." : "Falha de integridade (CRC não confere).");
        return hasCrc;
    }

    private static async Task<ItemResult> PlaceAsync(string staged, string name, int parentCount, DestinationGuard guard, PlannedEntry item, string? zone,
        IExtractionInteraction interaction, RunState state, CancellationToken ct)
    {
        string final;
        using (var parent = guard.Pin(item.Components, parentCount, create: false))
        {
            final = Path.Join(parent.FullPath, name);
            guard.AssertContained(final);
            if (!PathExists(final) && parent.MoveHere(staged, name, replace: false))
            {
                MarkOfTheWeb.Apply(final, zone);
                return new ItemResult(item.RelativePath, ItemOutcome.Succeeded, FinalPath: final);
            }
        }

        // A pergunta ao usuário acontece sem a cadeia presa; ela é presa e verificada de novo antes de agir.
        var existing = new FileInfo(final);
        var existingDir = new DirectoryInfo(final);
        var conflict = new ConflictInfo(
            final,
            existing.Exists ? existing.Length : null,
            existing.Exists ? existing.LastWriteTime : existingDir.Exists ? existingDir.LastWriteTime : null,
            existingDir.Exists,
            item.RelativePath,
            item.Entry.Size,
            item.Entry.Modified);

        var decision = state.ApplyToAll ?? await interaction.ResolveConflictAsync(conflict, ct).ConfigureAwait(false);
        if (decision.ApplyToRemaining && decision.Choice != ConflictChoice.Cancel) state.ApplyToAll = decision;

        switch (decision.Choice)
        {
            case ConflictChoice.Skip:
                return new ItemResult(item.RelativePath, ItemOutcome.Skipped, Message: "Existente preservado.", FinalPath: final);
            case ConflictChoice.KeepBoth:
            {
                using var parent = guard.Pin(item.Components, parentCount, create: false);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var alternative = UniqueNames.Next(name, n => PathExists(Path.Join(parent.FullPath, n)));
                    if (parent.MoveHere(staged, alternative, replace: false))
                    {
                        var placed = Path.Join(parent.FullPath, alternative);
                        MarkOfTheWeb.Apply(placed, zone);
                        return new ItemResult(item.RelativePath, ItemOutcome.Renamed, Message: $"Salvo como \"{alternative}\".", FinalPath: placed);
                    }
                }
                return new ItemResult(item.RelativePath, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Não foi possível obter um nome livre.");
            }
            case ConflictChoice.Replace:
            {
                using var parent = guard.Pin(item.Components, parentCount, create: false);
                if (Directory.Exists(final))
                    return new ItemResult(item.RelativePath, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Existe uma pasta com esse nome; pastas não são substituídas por arquivos.");
                if (DestinationGuard.IsLink(new FileInfo(final)))
                    return new ItemResult(item.RelativePath, ItemOutcome.Blocked, OperationErrorKind.DestinationTraversesLink, "O item existente é um link; não será substituído.");
                parent.MoveHere(staged, name, replace: true);
                MarkOfTheWeb.Apply(final, zone);
                return new ItemResult(item.RelativePath, ItemOutcome.Replaced, FinalPath: final);
            }
            default:
                state.CancelledByUser = true;
                return new ItemResult(item.RelativePath, ItemOutcome.Skipped, OperationErrorKind.Cancelled, "Cancelado no conflito; existente preservado.", final);
        }
    }

    /// <summary>
    /// Monta o plano: sanitiza caminhos, bloqueia links/especiais, aplica seleção e prefixo,
    /// e recusa colisões (caixa/normalização, arquivo x pasta) em vez de fundi-las.
    /// </summary>
    internal static List<PlannedEntry> BuildPlan(ArchiveInfo info, ExtractionRequest request, ExtractionLimits limits, out List<ItemResult> rejected)
    {
        rejected = [];
        var plan = new List<PlannedEntry>();
        var baseComponents = request.BaseInnerPath.Length == 0 ? [] : request.BaseInnerPath.Split('/');
        var selected = request.SelectedPaths?.Select(p => p.Trim('/')).Where(p => p.Length > 0).ToArray();
        var fileKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var dirKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in info.Entries)
        {
            var sanitized = ArchivePathPolicy.Sanitize(entry.RawKey, limits.MaxDepth, limits.MaxRelativePathLength);
            var display = sanitized.IsAccepted ? sanitized.RelativePath : entry.RawKey;
            if (!sanitized.IsAccepted)
            {
                if (IsSelected(display, selected, baseComponents, rawOnly: true))
                    rejected.Add(new ItemResult(Printable(entry.RawKey), ItemOutcome.Blocked, OperationErrorKind.PathRejected, sanitized.Message));
                continue;
            }
            if (!IsSelected(sanitized.RelativePath, selected, baseComponents, rawOnly: false)) continue;
            if (entry.IsLinkOrSpecial)
            {
                rejected.Add(new ItemResult(display, ItemOutcome.Blocked, OperationErrorKind.LinkOrSpecialBlocked, "Links e tipos especiais não são extraídos."));
                continue;
            }

            var components = sanitized.Components.Skip(baseComponents.Length).ToList();
            if (components.Count == 0) continue;
            var relative = string.Join('/', components);

            // Colisões: ancestrais não podem ser arquivos; um arquivo não pode coincidir com pasta.
            string? collision = null;
            for (var i = 1; i < components.Count && collision is null; i++)
            {
                var ancestorKey = WindowsNameRules.CollisionKey(string.Join('/', components.Take(i)));
                if (fileKeys.TryGetValue(ancestorKey, out var other)) collision = other;
            }
            var key = WindowsNameRules.CollisionKey(relative);
            if (collision is null)
            {
                if (entry.IsDirectory)
                {
                    if (fileKeys.TryGetValue(key, out var other)) collision = other;
                }
                else if (fileKeys.TryGetValue(key, out var otherFile)) collision = otherFile;
                else if (dirKeys.TryGetValue(key, out var otherDir)) collision = otherDir;
            }
            if (collision is not null)
            {
                rejected.Add(new ItemResult(relative, ItemOutcome.Blocked, OperationErrorKind.NameCollision, $"Colide com \"{collision}\" no Windows (maiúsculas/minúsculas ou normalização)."));
                continue;
            }

            for (var i = 1; i < components.Count; i++)
                dirKeys.TryAdd(WindowsNameRules.CollisionKey(string.Join('/', components.Take(i))), string.Join('/', components.Take(i)));
            if (entry.IsDirectory) dirKeys.TryAdd(key, relative);
            else fileKeys[key] = relative;
            plan.Add(new PlannedEntry(entry, components, relative));
        }
        return plan;
    }

    private static bool IsSelected(string path, string[]? selected, string[] baseComponents, bool rawOnly)
    {
        if (rawOnly) return selected is null && baseComponents.Length == 0; // nomes rejeitados só aparecem na extração completa
        var basePrefix = baseComponents.Length == 0 ? string.Empty : string.Join('/', baseComponents) + "/";
        if (basePrefix.Length > 0 && !path.StartsWith(basePrefix, StringComparison.Ordinal)) return false;
        if (selected is null) return true;
        foreach (var s in selected)
            if (path == s || path.StartsWith(s + "/", StringComparison.Ordinal)) return true;
        return false;
    }

    private string CreateDedicatedFolder(string parent, string requestedName)
    {
        var name = WindowsNameRules.ValidateComponent(requestedName).IsValid ? requestedName : "Extraído";
        // Cria em nome temporário e renomeia: Directory.Move falha se o alvo já existir, o que
        // evita reutilizar silenciosamente uma pasta criada entre a verificação e a criação.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = UniqueNames.Next(name, n => PathExists(Path.Join(parent, n)), isDirectory: true);
            var temp = Path.Join(parent, ".controlfs-new-" + Guid.NewGuid().ToString("N"));
            var registration = _journal.Register(temp, TemporaryKind.EmptyFolder);
            try
            {
                Directory.CreateDirectory(temp);
                var final = Path.Join(parent, candidate);
                Directory.Move(temp, final);
                return final;
            }
            catch (IOException)
            {
                TryDeleteIfEmpty(temp);
            }
            finally
            {
                if (!Directory.Exists(temp)) registration.Dispose();
            }
        }
        throw new IOException("Não foi possível criar a pasta de destino.");
    }

    private (string Path, IDisposable Registration) CreateStaging(string root)
    {
        var staging = Path.Join(root, StagingPrefix + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        // Registrada antes de existir: se o app cair, a próxima inicialização remove esta pasta (e só ela).
        var registration = _journal.Register(staging, TemporaryKind.StagingFolder, token);
        var info = Directory.CreateDirectory(staging);
        if (OperatingSystem.IsWindows()) info.Attributes |= FileAttributes.Hidden;
        // Manifesto da própria operação, com o token do registro: base para limpeza confiável (nunca por padrão de nomes).
        File.WriteAllText(Path.Join(staging, ManifestName), TemporaryJournal.ManifestContent(token));
        return (staging, registration);
    }

    private static void TryDeleteStaging(string staging, IDisposable registration)
    {
        try
        {
            if (Directory.Exists(staging) && File.Exists(Path.Join(staging, ManifestName)))
                Directory.Delete(staging, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (!Directory.Exists(staging)) registration.Dispose(); // se ficou para trás, o registro também fica
    }

    /// <summary>Remove apenas diretórios vazios criados por nós (de baixo para cima). Nunca remove arquivos.</summary>
    private static void TryDeleteEmptyTree(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            if (!info.Exists || DestinationGuard.IsLink(info)) return;
            foreach (var sub in info.EnumerateDirectories())
                if (!DestinationGuard.IsLink(sub)) TryDeleteEmptyTree(sub.FullName);
            TryDeleteIfEmpty(dir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteIfEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static long? TryGetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsFatal(OperationErrorKind kind) => kind is OperationErrorKind.PasswordRequired or OperationErrorKind.WrongPassword
        or OperationErrorKind.InsufficientSpace or OperationErrorKind.DestinationUnavailable or OperationErrorKind.AccessDenied or OperationErrorKind.LimitExceeded;

    private static OperationResult Fail(OperationErrorKind kind, string message) => new(OperationState.Failed, [], kind, message);

    private static string Printable(string raw) => new(raw.Select(c => char.IsControl(c) ? '�' : c).ToArray());

    private static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };

    internal sealed record PlannedEntry(ArchiveEntry Entry, IReadOnlyList<string> Components, string RelativePath);

    private sealed class RunState
    {
        public long TotalBytes;
        public int FilesDone;
        public ConflictDecision? ApplyToAll;
        public bool CancelledByUser;
    }
}
