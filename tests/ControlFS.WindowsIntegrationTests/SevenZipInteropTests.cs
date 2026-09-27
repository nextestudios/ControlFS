using System.Diagnostics;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// O 7z criado pelo ControlFS (#67) abre no 7-Zip de verdade: o runner do GitHub (windows-latest) tem o 7-Zip instalado.
/// Na CI a ausência dele é falha; fora dela, o teste é pulado.
/// </summary>
public sealed class SevenZipInteropTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-win-tests", Guid.NewGuid().ToString("N"));

    public SevenZipInteropTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string SevenZipExe()
    {
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var exe = Path.Join(dir, "7-Zip", "7z.exe");
            if (File.Exists(exe)) return exe;
        }
        if (Environment.GetEnvironmentVariable("CI") == "true") Assert.Fail("7-Zip não encontrado no runner da CI.");
        Assert.Skip("7-Zip não instalado.");
        return string.Empty;
    }

    private static (int Exit, string Output) Run(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [Theory]
    [InlineData(CompressionStrength.Fast)]
    [InlineData(CompressionStrength.Normal)]
    [InlineData(CompressionStrength.Maximum)]
    public async Task Created_7z_is_tested_and_extracted_by_7zip_byte_for_byte(CompressionStrength strength)
    {
        var exe = SevenZipExe();
        var source = Directory.CreateDirectory(Path.Join(_root, "origem", "Relatórios")).FullName;
        var random = new byte[300_000];
        new Random(67).NextBytes(random);
        File.WriteAllBytes(Path.Join(source, "aleatório.bin"), random);
        File.WriteAllText(Path.Join(source, "texto.txt"), string.Concat(Enumerable.Repeat("ControlFS cria 7z\r\n", 20_000)));
        File.WriteAllBytes(Path.Join(source, "vazio.txt"), []);
        Directory.CreateDirectory(Path.Join(source, "sub", "vazia"));
        File.WriteAllText(Path.Join(source, "sub", "ação.txt"), "acentos");
        var archive = Path.Join(_root, "pacote.7z");

        var created = await new ArchiveService().CompressAsync(new CompressionRequest
        {
            SourcePaths = [source],
            DestinationPath = archive,
            Format = CompressionFormat.SevenZip,
            Strength = strength,
        }, null, CancellationToken.None);
        Assert.Equal(OperationState.Completed, created.FinalState);

        var test = Run(exe, "t", archive);
        Assert.True(test.Exit == 0, test.Output);
        Assert.Contains("Everything is Ok", test.Output, StringComparison.Ordinal);

        var output = Path.Join(_root, "saida");
        var extract = Run(exe, "x", archive, "-o" + output, "-y");
        Assert.True(extract.Exit == 0, extract.Output);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Join(output, "Relatórios", Path.GetRelativePath(source, file));
            Assert.True(File.Exists(copy), copy);
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(copy));
        }
        Assert.True(Directory.Exists(Path.Join(output, "Relatórios", "sub", "vazia")));
    }
}
