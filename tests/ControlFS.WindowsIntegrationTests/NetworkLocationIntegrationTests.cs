using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// Locais de rede que o Windows já conhece (#27) com recursos reais do runner: uma unidade mapeada com <c>net use</c> para o
/// compartilhamento administrativo da própria máquina (<c>\\localhost\C$</c>, o mais perto de um NAS que o CI permite) e
/// atalhos de "Locais de rede" numa pasta temporária. NAS real, servidor desligado e credenciais ficam na verificação manual.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NetworkLocationIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-network-tests", Guid.NewGuid().ToString("N"));
    private string? _letter;

    public NetworkLocationIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (_letter is not null) Net($"use {_letter} /delete /y");
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task A_mapped_drive_appears_with_the_network_symbol_and_its_share_and_can_be_browsed()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "ZYXWVUTSRQ".FirstOrDefault(c => !used.Contains(c));
        if (free == default) Assert.Skip("Nenhuma letra livre.");
        var letter = free + ":";
        if (Net($@"use {letter} \\localhost\C$ /persistent:no") != 0) Assert.Skip(@"Este runner não permite mapear \\localhost\C$ (serviço Servidor ou compartilhamento administrativo indisponível).");
        _letter = letter;

        var provider = new LocalFileSystemProvider(networkShortcutsFolder: _root);
        var drive = Assert.Single(provider.GetPlaces(), p => p.Id.Equals($@"drive:{letter}\", StringComparison.OrdinalIgnoreCase));
        Assert.Equal((EntryKind.Drive, DriveKind.Network), (drive.Kind, drive.Drive));
        Assert.Equal($"C$ ({letter})", drive.Name);
        Assert.Contains(@"\\localhost\C$", drive.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desconectada", drive.Detail, StringComparison.Ordinal);
        Assert.Null(drive.Volume); // o volume de rede não é consultado ao montar o início
        Assert.True(provider.IsNetworkPath($@"{letter}\Windows"));
        Assert.False(provider.IsNetworkPath(Path.GetTempPath()));

        var listing = await provider.ListAsync(drive.FullPath!, includeHidden: false, TestContext.Current.CancellationToken);
        Assert.Contains(listing.Entries, e => e.Name.Equals("Windows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Network_shortcuts_to_unc_shares_are_listed_and_other_targets_are_ignored()
    {
        var nas = Directory.CreateDirectory(Path.Join(_root, "NAS da sala")).FullName;
        CreateShortcut(Path.Join(nas, "target.lnk"), @"\\localhost\C$\Windows");
        CreateShortcut(Path.Join(_root, "Pasta local.lnk"), Path.GetTempPath());

        var places = new LocalFileSystemProvider(networkShortcutsFolder: _root).GetPlaces();

        var share = Assert.Single(places, p => p.Id.StartsWith("net:", StringComparison.Ordinal));
        Assert.Equal("NAS da sala", share.Name);
        Assert.Equal(@"\\localhost\C$\Windows", share.FullPath, ignoreCase: true);
        Assert.Equal((EntryKind.Drive, DriveKind.Network), (share.Kind, share.Drive));
    }

    private static void CreateShortcut(string path, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        var shell = Activator.CreateInstance(type)!;
        var link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path], CultureInfo.InvariantCulture)!;
        link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, [target], CultureInfo.InvariantCulture);
        link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null, CultureInfo.InvariantCulture);
    }

    private static int Net(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("net.exe", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
