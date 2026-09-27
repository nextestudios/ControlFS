using ControlFS.Application;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Operations;

public class LeftoverCleanupTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string JournalDir => _tmp.Sub("dados-do-app", "operations");

    /// <summary>Registro de um processo que já não existe (o ControlFS que caiu).</summary>
    private TemporaryJournal Crashed() => new(JournalDir, processId: int.MaxValue, processStartTicks: 1);

    private static string Staging(string folder, ITemporaryJournal journal, string token, string? manifestToken = null)
    {
        var path = Path.Join(folder, TemporaryJournal.StagingPrefix + Guid.NewGuid().ToString("N"));
        journal.Register(path, TemporaryKind.StagingFolder, token); // nunca descartado: a operação "caiu"
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Join(path, TemporaryJournal.ManifestName), TemporaryJournal.ManifestContent(manifestToken ?? token));
        File.WriteAllText(Path.Join(path, "entrada.part"), "parcial");
        return path;
    }

    [Fact]
    public void Next_launch_removes_the_owned_leftovers_of_a_crashed_operation_and_nothing_else() => UiContext.Run(async () =>
    {
        var work = _tmp.MakeDir("Downloads");
        var crashed = Crashed();
        var ownedStaging = Staging(work, crashed, "token-da-extracao");
        var ownedPartial = Path.Join(work, ".controlfs-copy-" + Guid.NewGuid().ToString("N") + ".part");
        crashed.Register(ownedPartial, TemporaryKind.PartialFile);
        File.WriteAllText(ownedPartial, "cópia pela metade");

        // Parece nosso só pelo nome, mas não está no registro: nunca é tocado.
        var lookalike = _tmp.MakeDir("Downloads", TemporaryJournal.StagingPrefix + "do-usuario");
        File.WriteAllText(Path.Join(lookalike, TemporaryJournal.ManifestName), TemporaryJournal.ManifestContent("qualquer"));
        // Registrado, mas o manifesto não confere com o token: não é comprovadamente nosso.
        var wrongToken = Staging(work, crashed, "token-registrado", manifestToken: "outro-token");
        // Operação ativa de outra instância do ControlFS que continua rodando (este processo): fica.
        var live = new TemporaryJournal(JournalDir);
        var active = Staging(work, live, "token-ativo");
        File.WriteAllText(Path.Join(work, "foto.jpg"), "do usuário");

        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), temporaries: live);
        app.Start();
        await app.WhenIdleAsync();

        Assert.False(Directory.Exists(ownedStaging));
        Assert.False(File.Exists(ownedPartial));
        Assert.True(Directory.Exists(lookalike));
        Assert.True(Directory.Exists(wrongToken));
        Assert.True(Directory.Exists(active));
        Assert.Equal("do usuário", File.ReadAllText(Path.Join(work, "foto.jpg")));
        Assert.StartsWith("Limpeza: 2 temporários", app.StatusMessage, StringComparison.Ordinal);
        // Só o registro da operação ativa continua; os demais foram resolvidos.
        Assert.Single(Directory.EnumerateFiles(JournalDir));
    });

    [Fact]
    public async Task Finished_operations_leave_no_journal_entries_behind()
    {
        var journal = new TemporaryJournal(JournalDir);
        var zip = Create(_tmp.Sub("dados.zip"), Text("a.txt", "A"), Text("pasta/b.txt", "B"));
        var archives = new ArchiveService(journal);
        await archives.ExtractAsync(new ExtractionRequest { ArchivePath = zip, DestinationDirectory = _tmp.Path }, new NoConflicts(), null, CancellationToken.None);
        await archives.CompressAsync(new CompressionRequest { SourcePaths = [_tmp.Sub("dados")], DestinationPath = _tmp.Sub("de-novo.zip") }, null, CancellationToken.None);
        var copied = await new FileOperationService(journal).RunAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [_tmp.Sub("dados")],
            DestinationFolder = _tmp.MakeDir("copia"),
        }, new NoConflicts(), null, CancellationToken.None);

        Assert.Equal(OperationState.Completed, copied.FinalState);
        Assert.True(File.Exists(_tmp.Sub("de-novo.zip")));
        Assert.Empty(Directory.EnumerateFiles(JournalDir));
    }

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Conflito inesperado: " + conflict.ExistingPath);
    }
}
