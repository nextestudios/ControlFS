using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Shell;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Terminal opcional (#76): só abre depois do aviso, na pasta atual, e o caminho nunca vira argumento.</summary>
public class TerminalJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeTerminal : ITerminalLauncher
    {
        public List<string> Opened { get; } = [];
        public int Keyboards { get; private set; }
        public void OpenTerminal(string folder) => Opened.Add(folder);
        public void OpenSystemKeyboard() => Keyboards++;
    }

    [Fact]
    public void Open_terminal_here_warns_first_starting_on_cancel_and_opens_at_the_current_folder() => UiContext.Run(async () =>
    {
        var folder = _tmp.MakeDir("projeto");
        File.WriteAllText(Path.Join(folder, "a.txt"), "a");
        var terminal = new FakeTerminal();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService()) { Terminal = terminal };
        app.Start();
        var d = new Driver(app);
        app.OpenPhysical(folder);
        await d.FocusItem("a.txt");

        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Abrir terminal aqui…");
        var notice = await d.WaitDialog("Abrir terminal nesta pasta?");
        Assert.Equal("Cancelar", notice.Options[notice.FocusIndex].Label);
        Assert.Empty(terminal.Opened); // nada abre sem escolha explícita
        d.ChooseOption(notice, "Abrir terminal e o teclado");
        Assert.Equal([folder], terminal.Opened);
        Assert.Equal(1, terminal.Keyboards);
        Assert.Null(app.TopModal);
    });

    [Fact]
    public void The_folder_only_goes_as_working_directory_never_inside_the_arguments()
    {
        const string hostile = @"C:\x; calc & ""y"" $(z)";
        var wt = TerminalLauncher.Plan(hostile, @"C:\Users\u\AppData\Local\Microsoft\WindowsApps\wt.exe", @"C:\Windows\System32");
        Assert.Equal(["-d", "."], wt.Arguments);
        Assert.Equal(hostile, wt.WorkingDirectory);
        var ps = TerminalLauncher.Plan(hostile, null, @"C:\Windows\System32");
        Assert.Equal(["-NoLogo"], ps.Arguments);
        Assert.Equal(Path.Join(@"C:\Windows\System32", "WindowsPowerShell", "v1.0", "powershell.exe"), ps.FileName);
        Assert.Equal(hostile, ps.WorkingDirectory);
    }
}
