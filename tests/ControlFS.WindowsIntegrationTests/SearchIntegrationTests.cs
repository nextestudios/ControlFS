using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>Busca por nome (#46) numa árvore gerada: nunca atravessa links e relata pastas sem permissão.</summary>
[SupportedOSPlatform("windows")]
public sealed class SearchIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-win-tests", Guid.NewGuid().ToString("N"));

    public SearchIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static List<SearchResult> Search(string root, string query) =>
        new LocalFileSystemProvider().Search(new SearchRequest(root, query, IncludeSubfolders: true, IncludeHidden: false), CancellationToken.None).ToList();

    [Fact]
    public async Task Search_never_descends_into_a_junction()
    {
        var outside = Directory.CreateDirectory(Path.Join(_root, "outside")).FullName;
        File.WriteAllText(Path.Join(outside, "needle.txt"), "fora da busca");
        var tree = Directory.CreateDirectory(Path.Join(_root, "tree")).FullName;
        var real = Directory.CreateDirectory(Path.Join(tree, "real", "deep")).FullName;
        File.WriteAllText(Path.Join(real, "needle.txt"), "dentro");
        // Junções não exigem privilégio de administrador.
        var link = Path.Join(tree, "link-needle");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, outside }, UseShellExecute = false, CreateNoWindow = true })!;
        await mklink.WaitForExitAsync();
        Assert.Equal(0, mklink.ExitCode);

        var results = Search(tree, "needle");

        Assert.DoesNotContain(results, r => r.InaccessibleFolder is not null);
        var paths = results.Select(r => r.Match!.FullPath).Order(StringComparer.OrdinalIgnoreCase).ToList();
        // O próprio link pode ser um resultado (o nome casa), mas nada do alvo dele é enumerado.
        Assert.Equal([link, Path.Join(real, "needle.txt")], paths);
        Assert.True(results.Single(r => r.Match!.FullPath == link).Match!.IsReparsePoint);
    }

    [Fact]
    public void Folder_without_permission_is_reported_not_silently_skipped()
    {
        var tree = Directory.CreateDirectory(Path.Join(_root, "tree")).FullName;
        File.WriteAllText(Path.Join(tree, "needle-visible.txt"), "x");
        var locked = Directory.CreateDirectory(Path.Join(tree, "locked"));
        File.WriteAllText(Path.Join(locked.FullName, "needle-locked.txt"), "x");
        var user = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = locked.GetAccessControl();
        security.AddAccessRule(deny);
        locked.SetAccessControl(security);
        try
        {
            var results = Search(tree, "needle");

            Assert.Equal(locked.FullName, Assert.Single(results, r => r.InaccessibleFolder is not null).InaccessibleFolder);
            Assert.Equal("needle-visible.txt", Assert.Single(results, r => r.Match is not null).Match!.Name);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            locked.SetAccessControl(security);
        }
    }
}
