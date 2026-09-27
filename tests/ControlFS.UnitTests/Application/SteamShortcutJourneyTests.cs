using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Core;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Atalhos de jogos da Steam na Área de trabalho (#168): título, tipo, detalhes, Narrador e abrir pelo Windows.</summary>
public class SteamShortcutJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeShell : IShellService
    {
        public List<(string Verb, string Path)> Calls { get; } = [];
        public void Open(string path) => Calls.Add(("open", path));
        public void OpenWith(string path) => Calls.Add(("openwith", path));
        public void RevealInExplorer(string path) => Calls.Add(("reveal", path));
    }

    [Fact]
    public void Steam_shortcut_shows_the_game_title_keeps_the_real_file_and_opens_the_file_itself_after_confirmation() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("Valheim.url"), ShortcutTests.SteamUrl);
        File.WriteAllText(_tmp.Sub("Site.url"), ShortcutTests.WebUrl);
        var shell = new FakeShell();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), shell: shell);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);

        await d.FocusItem("Valheim.url");
        var game = app.ActivePane.List.Focused!;
        Assert.Equal("Valheim", EntryText.DisplayName(game));
        Assert.Equal("Jogo da Steam", app.TypeNameOf(game));
        Assert.StartsWith("Valheim, jogo da steam", app.DescribeFocus().Item, StringComparison.Ordinal);
        var details = app.Details!;
        Assert.Equal("Valheim.url", details.Title); // detalhes: nome e tipo reais do arquivo
        Assert.Contains(details.Lines, l => l.Value == "steam://rungameid/892970");
        Assert.Contains(details.Lines, l => l.Value == "Atalho da Internet (.url)");

        // Abrir pede confirmação (começa em Cancelar) e entrega o próprio arquivo ao Windows.
        d.Press(InputAction.Confirm);
        var dialog = await d.WaitDialog("Abrir este jogo da Steam?");
        Assert.Equal("Cancelar", dialog.Options[dialog.FocusIndex].Label);
        Assert.Contains(dialog.Lines, l => l is ("Abre", "steam://rungameid/892970"));
        d.ChooseOption(dialog, "Jogar");
        Assert.Equal([("open", _tmp.Sub("Valheim.url"))], shell.Calls);

        // Site comum continua um .url comum.
        await d.FocusItem("Site.url");
        var site = app.ActivePane.List.Focused!;
        Assert.Equal("Site.url", EntryText.DisplayName(site));
        Assert.Equal("Arquivo URL", app.TypeNameOf(site));
        d.Press(InputAction.Confirm);
        d.ChooseOption(await d.WaitDialog("Executar este arquivo?"), "Cancelar");
    });
}
