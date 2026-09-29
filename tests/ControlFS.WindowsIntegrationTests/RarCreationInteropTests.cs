using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// RAR criado pelo Rar.exe do WinRAR (docs/decisions/0011) e aberto pelo leitor do próprio ControlFS. Só roda onde o
/// WinRAR está instalado (o ControlFS nunca o traz); em outro lugar é pulado. A variável CONTROLFS_REQUIRE_WINRAR=1 transforma
/// a ausência em falha, para o runner que instala o WinRAR de propósito.
/// </summary>
public sealed class RarCreationInteropTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-win-tests", Guid.NewGuid().ToString("N"));

    public RarCreationInteropTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("conflito inesperado");
    }

    [Theory]
    [InlineData(CompressionStrength.Fast)]
    [InlineData(CompressionStrength.Maximum)]
    public async Task Rar_created_with_the_installed_winrar_opens_and_extracts_with_the_controlfs_reader(CompressionStrength strength)
    {
        var service = new ArchiveService();
        var availability = service.GetCreationAvailability(CompressionFormat.Rar);
        if (!availability.IsAvailable)
        {
            if (Environment.GetEnvironmentVariable("CONTROLFS_REQUIRE_WINRAR") == "1") Assert.Fail("WinRAR não encontrado no runner: " + availability.Reason);
            Assert.Skip("WinRAR não instalado.");
        }

        var source = Directory.CreateDirectory(Path.Join(_root, "origem", "Relatórios")).FullName;
        var random = new byte[300_000];
        new Random(258).NextBytes(random);
        File.WriteAllBytes(Path.Join(source, "aleatório.bin"), random);
        File.WriteAllText(Path.Join(source, "texto.txt"), string.Concat(Enumerable.Repeat("ControlFS cria RAR\r\n", 20_000)));
        File.WriteAllBytes(Path.Join(source, "vazio.txt"), []);
        Directory.CreateDirectory(Path.Join(source, "sub", "vazia"));
        File.WriteAllText(Path.Join(source, "sub", "ação.txt"), "acentos");
        File.WriteAllText(Path.Join(_root, "origem", "-solto.txt"), "nome com hífen");
        var archive = Path.Join(_root, "origem", "pacote.rar");

        var created = await service.CompressAsync(new CompressionRequest
        {
            SourcePaths = [source, Path.Join(_root, "origem", "-solto.txt")],
            DestinationPath = archive,
            Format = CompressionFormat.Rar,
            Strength = strength,
        }, null, CancellationToken.None);
        Assert.True(created.FinalState == OperationState.Completed, $"{created.FinalState} {created.Error} {created.Message}");
        Assert.Equal(ArchiveFormat.Rar, service.Detect(archive));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(archive)!), f => Path.GetFileName(f).StartsWith(".controlfs-", StringComparison.Ordinal));

        var info = await service.InspectAsync(archive, null, ExtractionLimits.Default, CancellationToken.None);
        Assert.NotEmpty(info.Entries);

        var extracted = await service.ExtractAsync(new ExtractionRequest { ArchivePath = archive, DestinationDirectory = Path.Join(_root, "saida") }, new NoConflicts(), null, CancellationToken.None);
        Assert.True(extracted.FinalState == OperationState.Completed, $"{extracted.FinalState} {extracted.Error} {extracted.Message}");
        var root = extracted.Destination!;
        Assert.Equal(random, File.ReadAllBytes(Path.Join(root, "Relatórios", "aleatório.bin")));
        Assert.Equal(File.ReadAllBytes(Path.Join(source, "texto.txt")), File.ReadAllBytes(Path.Join(root, "Relatórios", "texto.txt")));
        Assert.Empty(File.ReadAllBytes(Path.Join(root, "Relatórios", "vazio.txt")));
        Assert.Equal("acentos", File.ReadAllText(Path.Join(root, "Relatórios", "sub", "ação.txt")));
        Assert.True(Directory.Exists(Path.Join(root, "Relatórios", "sub", "vazia")));
        Assert.Equal("nome com hífen", File.ReadAllText(Path.Join(root, "-solto.txt")));
    }
}
