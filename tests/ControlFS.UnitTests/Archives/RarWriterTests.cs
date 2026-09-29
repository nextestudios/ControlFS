using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Archives.Creation;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Archives;

/// <summary>
/// Criação de RAR pelo Rar.exe do usuário (docs/decisions/0011), testada com um executável falso (tests/ControlFS.FakeRar)
/// que só imita a linha de comando: argumentos, lista, códigos de saída, cancelamento e limpeza. O RAR de verdade é
/// coberto pelo teste de integração do Windows (quando o WinRAR está instalado).
/// </summary>
public class RarWriterTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static RarTool Fake(string mode)
    {
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var config = baseDir.Parent!.Name;
        var dll = Path.GetFullPath(Path.Join(baseDir.FullName, "..", "..", "..", "..", "ControlFS.FakeRar", "bin", config, baseDir.Name, "ControlFS.FakeRar.dll"));
        Assert.True(File.Exists(dll), "Rar.exe falso não foi compilado: " + dll);
        return new RarTool(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet", [dll, "--mode", mode]);
    }

    private ArchiveService Service(string mode) => new(rarLocator: () => Fake(mode));

    private string MakeSources()
    {
        var origem = _tmp.MakeDir("origem");
        Directory.CreateDirectory(Path.Join(origem, "proj", "sub"));
        Directory.CreateDirectory(Path.Join(origem, "proj", "vazia"));
        File.WriteAllText(Path.Join(origem, "proj", "sub", "a.txt"), "conteúdo");
        File.WriteAllText(Path.Join(origem, "solto.txt"), "solto");
        File.WriteAllText(Path.Join(origem, "-x.txt"), "parece opção");
        return origem;
    }

    private static CompressionRequest Request(string origem, string name = "pacote.rar", CompressionStrength strength = CompressionStrength.Normal) => new()
    {
        SourcePaths = [Path.Join(origem, "proj"), Path.Join(origem, "solto.txt"), Path.Join(origem, "-x.txt")],
        DestinationPath = Path.Join(origem, name),
        Format = CompressionFormat.Rar,
        Strength = strength,
    };

    private static string Report(string archive) => Encoding.UTF8.GetString(File.ReadAllBytes(archive)[8..]);

    [Fact]
    public async Task Builds_a_fixed_command_with_a_list_file_and_finalizes_atomically()
    {
        var origem = MakeSources();
        var before = _tmp.Snapshot();
        var request = Request(origem, strength: CompressionStrength.Maximum);

        var result = await Service("ok").CompressAsync(request, null, CancellationToken.None);

        Assert.True(result.FinalState == OperationState.Completed, $"{result.FinalState} {result.Error} {result.Message}");
        Assert.Equal(request.DestinationPath, result.Destination);
        var report = Report(request.DestinationPath);
        Assert.Contains("CWD=" + origem, report);
        Assert.Contains("ARG=-ma5", report);
        Assert.Contains("ARG=-m5", report);
        foreach (var fixedSwitch in new[] { "-r-", "-y", "-idq", "-cfg-", "-scul" }) Assert.Contains("ARG=" + fixedSwitch, report);
        // Nenhuma opção de senha: "-p-" já criptografou com a senha "-" no WinRAR 7.23.
        Assert.DoesNotContain(report.Split('\n'), l => l.StartsWith("ARG=-p", StringComparison.Ordinal) || l.StartsWith("ARG=-hp", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Split('\n'), l => l.StartsWith("ARG=" + request.DestinationPath, StringComparison.Ordinal)); // grava no temporário, não no nome final
        Assert.Contains("LISTEXISTS=True", report);
        // Só arquivos e pastas vazias vão para a lista; pasta com conteúdo não (o Rar.exe não desce sozinho); nome com '-' vira caminho.
        var listed = report.Split('\n').Where(l => l.StartsWith("LIST=", StringComparison.Ordinal)).Select(l => l[5..].TrimEnd('\r')).Order().ToArray();
        Assert.Equal(new[] { @".\-x.txt", @"proj\sub\a.txt", @"proj\vazia", "solto.txt" }.Order().ToArray(), listed);
        // Origem intacta e nenhuma sobra.
        Assert.Equal(before.Append("origem/pacote.rar").Order(StringComparer.Ordinal), _tmp.Snapshot());
        Assert.Equal("conteúdo", File.ReadAllText(Path.Join(origem, "proj", "sub", "a.txt")));
    }

    [Fact]
    public async Task Never_overwrites_an_existing_archive()
    {
        var origem = MakeSources();
        var existing = Path.Join(origem, "pacote.rar");
        File.WriteAllText(existing, "não mexa");

        var result = await Service("ok").CompressAsync(Request(origem), null, CancellationToken.None);

        Assert.Equal(OperationState.Failed, result.FinalState);
        Assert.Equal(OperationErrorKind.AlreadyExists, result.Error);
        Assert.Equal("não mexa", File.ReadAllText(existing));
        Assert.DoesNotContain(Directory.EnumerateFiles(origem), f => Path.GetFileName(f).StartsWith(".controlfs-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Warning_exit_code_completes_with_warnings_and_keeps_the_archive()
    {
        var origem = MakeSources();
        var result = await Service("warn").CompressAsync(Request(origem), null, CancellationToken.None);

        Assert.Equal(OperationState.CompletedWithWarnings, result.FinalState);
        Assert.Contains("WinRAR avisou", result.Message);
        Assert.True(File.Exists(Path.Join(origem, "pacote.rar")));
    }

    [Theory]
    [InlineData("fail", "não conseguiu gravar")]
    [InlineData("empty", "não gerou um arquivo RAR válido")]
    public async Task Failure_leaves_nothing_under_the_final_name_and_explains_in_portuguese(string mode, string expected)
    {
        var origem = MakeSources();
        var before = _tmp.Snapshot();

        var result = await Service(mode).CompressAsync(Request(origem), null, CancellationToken.None);

        Assert.Equal(OperationState.Failed, result.FinalState);
        Assert.Contains(expected, result.Message);
        Assert.DoesNotContain('\u0007', result.Message!);
        Assert.Equal(before, _tmp.Snapshot());
    }

    [Fact]
    public async Task Cancelling_kills_the_process_and_removes_the_partial_file()
    {
        var origem = MakeSources();
        var before = _tmp.Snapshot();
        using var cts = new CancellationTokenSource();
        var task = Service("hang").CompressAsync(Request(origem), null, cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!Directory.EnumerateFiles(origem, ".controlfs-new-*.part").Any() && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.True(Directory.EnumerateFiles(origem, ".controlfs-new-*.part").Any(), "o processo falso não começou a gravar");

        await cts.CancelAsync();
        var result = await task;

        Assert.Equal(OperationState.Cancelled, result.FinalState);
        Assert.Equal(before, _tmp.Snapshot());
    }

    [Fact]
    public async Task A_stalled_process_is_killed_and_reported()
    {
        var origem = MakeSources();
        var before = _tmp.Snapshot();

        var result = await ArchiveCreator.CreateAsync(Request(origem), null, CancellationToken.None, null, Fake("hang"), TimeSpan.FromSeconds(2));

        Assert.Equal(OperationState.Failed, result.FinalState);
        Assert.Contains("parou de responder", result.Message);
        Assert.Equal(before, _tmp.Snapshot());
    }

    [Fact]
    public async Task Rar_needs_the_installed_winrar_and_one_source_folder()
    {
        var origem = MakeSources();
        var none = new ArchiveService(rarLocator: () => null);
        Assert.False(none.GetCreationAvailability(CompressionFormat.Rar).IsAvailable);
        Assert.Contains("WinRAR", none.GetCreationAvailability(CompressionFormat.Rar).Reason);
        Assert.True(none.GetCreationAvailability(CompressionFormat.SevenZip).IsAvailable);
        Assert.True(Service("ok").GetCreationAvailability(CompressionFormat.Rar).IsAvailable);

        var missing = await none.CompressAsync(Request(origem), null, CancellationToken.None);
        Assert.Equal(OperationState.Failed, missing.FinalState);

        var other = _tmp.MakeDir("outra");
        File.WriteAllText(Path.Join(other, "b.txt"), "b");
        var twoFolders = await Service("ok").CompressAsync(new CompressionRequest
        {
            SourcePaths = [Path.Join(origem, "solto.txt"), Path.Join(other, "b.txt")],
            DestinationPath = Path.Join(origem, "dois.rar"),
            Format = CompressionFormat.Rar,
        }, null, CancellationToken.None);
        Assert.Equal(OperationErrorKind.PathRejected, twoFolders.Error);
        Assert.False(File.Exists(Path.Join(origem, "dois.rar")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\WinRAR\Rar.exe", true)]
    [InlineData(@"C:\Program Files\WinRAR\rar.EXE", true)]
    [InlineData(@"C:\Program Files\WinRAR\WinRAR.exe", false)]
    [InlineData(@"C:\Temp\Rar.exe.bat", false)]
    [InlineData(@"\\servidor\share\Rar.exe", false)]
    [InlineData("Rar.exe", false)]
    public void Only_an_absolute_local_Rar_exe_is_acceptable(string path, bool expected)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Caminhos de unidade só fazem sentido no Windows.");
        Assert.Equal(expected, RarLocator.IsAcceptableName(path));
    }
}
