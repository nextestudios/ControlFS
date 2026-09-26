using System.Diagnostics;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Windows.Shell;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>Abrir com os programas do Windows, de verdade. "Abrir com…" é modal e fica na verificação manual (docs/TESTING.md).</summary>
public sealed class ShellIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-shell-tests", Guid.NewGuid().ToString("N"));
    private readonly WindowsShellService _shell = new();

    public ShellIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Open_starts_the_default_program_for_a_text_file()
    {
        var file = Path.Join(_root, "nota-controlfs.txt");
        File.WriteAllText(file, "teste");
        var before = Process.GetProcesses().Select(p => p.Id).ToHashSet();

        _shell.Open(file);

        Process? started = null;
        for (var i = 0; i < 40 && started is null; i++)
        {
            await Task.Delay(250);
            started = Process.GetProcesses().FirstOrDefault(p => !before.Contains(p.Id) && p.ProcessName.Contains("notepad", StringComparison.OrdinalIgnoreCase));
        }
        Assert.NotNull(started);
        try { started!.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    [Fact]
    public void Reveal_in_explorer_succeeds_for_an_existing_file()
    {
        var file = Path.Join(_root, "mostrar.txt");
        File.WriteAllText(file, "x");
        _shell.RevealInExplorer(file);
    }

    [Fact]
    public void Missing_items_fail_with_a_clear_error()
    {
        var missing = Path.Join(_root, "nao-existe.txt");
        Assert.Throws<ShellException>(() => _shell.Open(missing));
        Assert.Throws<ShellException>(() => _shell.RevealInExplorer(missing));
    }
}
