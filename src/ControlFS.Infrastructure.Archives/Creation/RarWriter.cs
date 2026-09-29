using System.Diagnostics;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Infrastructure.Archives.Creation;

/// <summary>
/// Cria RAR5 chamando o <c>Rar.exe</c> do WinRAR que o usuário instalou (docs/decisions/0011): a compressão RAR é
/// proprietária e o ControlFS não a reimplementa nem traz o programa. Sem shell (<see cref="ProcessStartInfo.ArgumentList"/>),
/// opções fixas, nomes numa lista <c>@arquivo</c> (sem limite de linha de comando e sem interpretar nomes como opções),
/// saída num temporário ao lado do destino; cancelar ou travar mata a árvore de processos.
/// </summary>
internal static class RarWriter
{
    /// <summary>Sem crescer o arquivo nem imprimir nada por tanto tempo, o processo é considerado travado.</summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromMinutes(10);
    private const int OutputTailLimit = 4000;
    private static readonly byte[] Rar5Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    public static OperationResult Create(RarTool tool, IReadOnlyList<(string FullPath, string EntryName, bool IsDirectory, long Size)> plan, List<ItemResult> results,
        string workingDirectory, string temp, string destination, CompressionStrength strength, TimeSpan stallTimeout, CancellationToken ct)
    {
        var names = ListedNames(plan);
        if (names.Count == 0) return new OperationResult(OperationState.Failed, results, OperationErrorKind.Unknown, "Nada para compactar.");
        if (names.Any(n => n.AsSpan().IndexOfAnyInRange('\0', '\u001f') >= 0))
            return new OperationResult(OperationState.Failed, results, OperationErrorKind.InvalidName, "Um nome contém caracteres de controle e não pode ir para o RAR.");

        var listFolder = Path.Join(Path.GetTempPath(), "controlfs-rar-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(listFolder);
            var listFile = Path.Join(listFolder, "itens.lst");
            // UTF-16 com BOM: aceita qualquer nome, e -scul diz ao Rar.exe que a lista está nesse formato.
            File.WriteAllLines(listFile, names, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            var run = Run(tool, workingDirectory, Arguments(temp, listFile, strength), temp, stallTimeout, ct);
            switch (run.Status)
            {
                case RarRunStatus.Cancelled:
                    TryDelete(temp);
                    return new OperationResult(OperationState.Cancelled, results, OperationErrorKind.Cancelled, "Compactação cancelada; nenhum arquivo foi criado.");
                case RarRunStatus.Stalled:
                    TryDelete(temp);
                    return new OperationResult(OperationState.Failed, results, OperationErrorKind.Unknown, "O WinRAR parou de responder e foi encerrado; nada foi criado.");
                case RarRunStatus.CouldNotStart:
                    TryDelete(temp);
                    return new OperationResult(OperationState.Failed, results, OperationErrorKind.Unknown, "Não foi possível iniciar o WinRAR. Confira se ele continua instalado.");
            }

            if (run.ExitCode is not (0 or 1) || !HasRar5Signature(temp))
            {
                TryDelete(temp);
                var (kind, message) = run.ExitCode is 0 or 1
                    ? (OperationErrorKind.Unknown, "O WinRAR terminou, mas não gerou um arquivo RAR válido; nada foi criado.")
                    : Describe(run.ExitCode, run.Output);
                return new OperationResult(OperationState.Failed, results, kind, message);
            }

            File.Move(temp, destination, overwrite: false);
            foreach (var item in plan.Where(p => !p.IsDirectory)) results.Add(new ItemResult(item.EntryName, ItemOutcome.Succeeded));
            var warning = run.ExitCode == 1;
            return new OperationResult(warning ? OperationState.CompletedWithWarnings : OperationState.Completed, results,
                Message: warning ? "O WinRAR avisou de problemas (por exemplo, um arquivo em uso): confira se tudo entrou no compactado." : null,
                Destination: destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            var (kind, message) = Security.ErrorMapper.Map(ex);
            if (File.Exists(destination) && kind == OperationErrorKind.Unknown) (kind, message) = (OperationErrorKind.AlreadyExists, $"\"{Path.GetFileName(destination)}\" foi criado por outro programa enquanto compactávamos.");
            return new OperationResult(OperationState.Failed, results, kind, message);
        }
        finally
        {
            try { Directory.Delete(listFolder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Nomes (relativos à pasta de trabalho) que vão para a lista: cada arquivo do plano e só as pastas realmente vazias.
    /// Pasta com conteúdo nunca é listada, para o Rar.exe não descer nela sozinho e seguir um link que o plano ignorou.
    /// </summary>
    public static List<string> ListedNames(IReadOnlyList<(string FullPath, string EntryName, bool IsDirectory, long Size)> plan)
    {
        var names = new List<string>();
        foreach (var item in plan)
        {
            if (item.IsDirectory && DirectoryHasAnything(item.FullPath)) continue;
            var name = item.EntryName.TrimEnd('/').Replace('/', '\\');
            // Nome que começa com '-' ou '@' seria lido como opção ou como outra lista.
            names.Add(name.StartsWith('-') || name.StartsWith('@') ? @".\" + name : name);
        }
        return names;
    }

    private static bool DirectoryHasAnything(string path)
    {
        try { return Directory.EnumerateFileSystemEntries(path).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// a = adicionar; -ma5 = RAR5; -mN = nível (1 rápida, 3 normal, 5 máxima); -r- = sem recursão própria (a lista já tem tudo);
    /// -y = responder sim; -idq = só erros; -cfg- = ignorar Rar.ini e a variável RAR do usuário; -p- = nunca pedir senha;
    /// -scul = lista em UTF-16. Sem senha, sem registro de recuperação, sem volumes, sem sólido.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string archive, string listFile, CompressionStrength strength) =>
    [
        "a", "-ma5", strength switch { CompressionStrength.Fast => "-m1", CompressionStrength.Maximum => "-m5", _ => "-m3" },
        "-r-", "-y", "-idq", "-cfg-", "-p-", "-scul", archive, "@" + listFile,
    ];

    public static (OperationErrorKind Kind, string Message) Describe(int exitCode, string output)
    {
        var detail = LastLine(output);
        var suffix = detail.Length > 0 ? $" Detalhe do WinRAR: {detail}" : string.Empty;
        return exitCode switch
        {
            2 => (OperationErrorKind.Unknown, "O WinRAR encontrou um erro fatal; nada foi criado." + suffix),
            3 => (OperationErrorKind.Corrupt, "O WinRAR acusou erro de checksum; nada foi criado." + suffix),
            4 => (OperationErrorKind.AccessDenied, "O WinRAR não pôde gravar em um arquivo bloqueado; nada foi criado." + suffix),
            5 => (OperationErrorKind.InsufficientSpace, "O WinRAR não conseguiu gravar (disco cheio ou sem permissão); nada foi criado." + suffix),
            6 => (OperationErrorKind.AccessDenied, "O WinRAR não conseguiu abrir um arquivo de origem (em uso ou sem permissão); nada foi criado." + suffix),
            7 => (OperationErrorKind.Unknown, "O WinRAR recusou os parâmetros; esta versão pode não ser compatível." + suffix),
            8 => (OperationErrorKind.InsufficientSpace, "O WinRAR ficou sem memória; nada foi criado." + suffix),
            9 => (OperationErrorKind.AccessDenied, "O WinRAR não conseguiu criar o arquivo na pasta de destino; nada foi criado." + suffix),
            10 => (OperationErrorKind.Unknown, "O WinRAR não encontrou os itens a compactar; nada foi criado." + suffix),
            255 => (OperationErrorKind.Cancelled, "O WinRAR foi interrompido; nada foi criado."),
            _ => (OperationErrorKind.Unknown, $"O WinRAR terminou com o código {exitCode}; nada foi criado." + suffix),
        };
    }

    private static string LastLine(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        var clean = new string(line.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > 200 ? clean[..200] : clean;
    }

    internal enum RarRunStatus { Finished, Cancelled, Stalled, CouldNotStart }

    internal readonly record struct RarRunResult(RarRunStatus Status, int ExitCode, string Output);

    internal static RarRunResult Run(RarTool tool, string workingDirectory, IReadOnlyList<string> arguments, string watchedFile, TimeSpan stallTimeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(tool.Path)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var lead in tool.LeadingArguments ?? []) info.ArgumentList.Add(lead);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        var tail = new StringBuilder();
        long activity = 0;
        void Collect(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (tail)
            {
                tail.AppendLine(e.Data);
                if (tail.Length > OutputTailLimit) tail.Remove(0, tail.Length - OutputTailLimit);
            }
            Interlocked.Increment(ref activity);
        }

        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        try
        {
            if (!process.Start()) return new RarRunResult(RarRunStatus.CouldNotStart, -1, string.Empty);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new RarRunResult(RarRunStatus.CouldNotStart, -1, string.Empty);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try { process.StandardInput.Close(); } catch (IOException) { }

        var idle = Stopwatch.StartNew();
        long lastSize = -1, lastActivity = -1;
        while (!process.WaitForExit(200))
        {
            if (ct.IsCancellationRequested) { Kill(process); return new RarRunResult(RarRunStatus.Cancelled, -1, string.Empty); }
            var size = SafeLength(watchedFile);
            var seen = Interlocked.Read(ref activity);
            if (size != lastSize || seen != lastActivity) { lastSize = size; lastActivity = seen; idle.Restart(); }
            else if (idle.Elapsed > stallTimeout) { Kill(process); return new RarRunResult(RarRunStatus.Stalled, -1, string.Empty); }
        }
        process.WaitForExit(); // termina de ler a saída
        if (ct.IsCancellationRequested) return new RarRunResult(RarRunStatus.Cancelled, -1, string.Empty);
        lock (tail) return new RarRunResult(RarRunStatus.Finished, process.ExitCode, tail.ToString());
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        try { process.WaitForExit(5000); } catch (InvalidOperationException) { }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch (IOException) { return -1; }
    }

    private static bool HasRar5Signature(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[Rar5Signature.Length];
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && head.AsSpan().SequenceEqual(Rar5Signature);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { if (File.Exists(path)) File.Delete(path); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }
}
