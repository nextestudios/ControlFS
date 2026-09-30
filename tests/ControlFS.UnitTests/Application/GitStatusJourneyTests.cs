using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Git;
using ControlFS.UnitTests.Support;
using LibGit2Sharp;

namespace ControlFS.UnitTests.Application;

/// <summary>Status do Git (#75) num repositório de teste real: desligado por padrão, marcas por item e dentro de subpastas.</summary>
public class GitStatusJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose()
    {
        // A libgit2 deixa objetos somente leitura: libera para o TempDir conseguir apagar.
        foreach (var file in Directory.EnumerateFiles(_tmp.Path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        _tmp.Dispose();
    }

    [Fact]
    public void Badges_show_modified_untracked_and_changed_folders_only_after_turning_it_on() => UiContext.Run(async () =>
    {
        var root = _tmp.MakeDir("repo");
        Repository.Init(root);
        File.WriteAllText(Path.Join(root, "a.txt"), "a");
        File.WriteAllText(Path.Join(root, "igual.txt"), "=");
        Directory.CreateDirectory(Path.Join(root, "src"));
        File.WriteAllText(Path.Join(root, "src", "b.txt"), "b");
        using (var repo = new Repository(root))
        {
            Commands.Stage(repo, "*");
            var who = new Signature("ControlFS", "tests@example.invalid", DateTimeOffset.Now);
            repo.Commit("inicial", who, who);
        }
        File.WriteAllText(Path.Join(root, "a.txt"), "a mudado");
        File.WriteAllText(Path.Join(root, "novo.txt"), "n");
        File.WriteAllText(Path.Join(root, "src", "b.txt"), "b mudado");
        File.WriteAllText(Path.Join(root, "src", "c.txt"), "c");

        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService()) { Git = new GitStatusReader() };
        app.Start();
        var d = new Driver(app);
        app.OpenPhysical(root);
        await d.Idle();
        Assert.Null(app.GitSummary); // desligado por padrão: nada é lido

        app.ToggleGitStatus(); // sem entrada nas Configurações (removida): o leitor segue coberto pelo motor
        await UiContext.WaitUntil(() => app.GitSummary is not null, "status do Git");
        Assert.StartsWith("GIT · ramo ", app.GitSummary, StringComparison.Ordinal);
        string? State(string name) => app.GitState(app.Browser.List.Items.Single(i => i.Name == name));
        Assert.Equal("Git: modificado", State("a.txt"));
        Assert.Equal("Git: novo (não rastreado)", State("novo.txt"));
        Assert.Equal("Git: com mudanças", State("src"));
        Assert.Null(State("igual.txt"));

        while (app.TopModal is not null) d.Press(InputAction.Back);
        await d.FocusItem("src");
        d.Press(InputAction.Confirm);
        await UiContext.WaitUntil(() => app.Browser.Location is PhysicalLocation { FullPath: var p } && p.EndsWith("src", StringComparison.Ordinal) && app.GitSummary is not null, "subpasta");
        Assert.Equal("Git: modificado", State("b.txt"));
        Assert.Equal("Git: novo (não rastreado)", State("c.txt"));
    });
}
