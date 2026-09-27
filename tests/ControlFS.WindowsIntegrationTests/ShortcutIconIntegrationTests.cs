using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Shell;
using Microsoft.Win32;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// Ícones de atalhos (#168) com arquivos reais: .url de jogo da Steam com IconFile/IconIndex e .lnk criado pelo
/// WScript.Shell. Nenhum jogo é aberto: o único teste de abrir só roda quando a Steam NÃO está instalada.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ShortcutIconIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-shortcut-tests", Guid.NewGuid().ToString("N"));
    private readonly ShellIconProvider _icons = new();

    public ShortcutIconIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _icons.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>.ico de 32×32 px, 32 bits, de uma cor só (BGRA).</summary>
    private string WriteIcon(string name, byte b, byte g, byte r)
    {
        const int side = 32;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var pixels = side * side * 4;
            var mask = side * 4; // 1 bit por pixel, linhas de 4 bytes
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
            w.Write((byte)side); w.Write((byte)side); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32); w.Write(40 + pixels + mask); w.Write(22);
            w.Write(40); w.Write(side); w.Write(side * 2); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(pixels + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (var i = 0; i < side * side; i++) { w.Write(b); w.Write(g); w.Write(r); w.Write((byte)255); }
            w.Write(new byte[mask]);
        }
        var path = Path.Join(_root, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private string WriteUrl(string name, string url, string? iconFile, int index = 0)
    {
        var path = Path.Join(_root, name);
        File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\n" + (iconFile is null ? string.Empty : $"IconFile={iconFile}\r\nIconIndex={index}\r\n"));
        return path;
    }

    private string WriteLnk(string name, string target, string? iconLocation)
    {
        var path = Path.Join(_root, name);
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        var shell = Activator.CreateInstance(type)!;
        var link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path], CultureInfo.InvariantCulture)!;
        var linkType = link.GetType();
        linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, [target], CultureInfo.InvariantCulture);
        if (iconLocation is not null) linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, [iconLocation], CultureInfo.InvariantCulture);
        linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null, CultureInfo.InvariantCulture);
        return path;
    }

    private static FileEntry Entry(string path)
    {
        var info = new FileInfo(path);
        return new FileEntry(path, info.Name, EntryKind.File, info.Length, info.LastWriteTime, path,
            Shortcut: path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ? ShortcutFiles.ReadInternetShortcut(path) : null);
    }

    private Task<IconImage?> IconOf(string path) => _icons.GetIconAsync(IconRequest.For(Entry(path))!, 32, TestContext.Current.CancellationToken);

    /// <summary>Cor do pixel central (BGRA pré-multiplicado).</summary>
    private static (byte B, byte G, byte R) Center(IconImage icon)
    {
        var i = ((icon.Height / 2 * icon.Width) + (icon.Width / 2)) * 4;
        var span = icon.Pixels.Span;
        return (span[i], span[i + 1], span[i + 2]);
    }

    [Fact]
    public async Task Steam_shortcuts_show_the_icon_they_declare_and_two_games_render_distinctly()
    {
        var red = WriteIcon("vermelho.ico", 0, 0, 255);
        var blue = WriteIcon("azul.ico", 255, 0, 0);
        var valheim = WriteUrl("Valheim.url", "steam://rungameid/892970", red);
        var deadSpace = WriteUrl("Dead Space.url", "steam://rungameid/1693980", blue);

        Assert.Equal((0, 0, 255), Center((await IconOf(valheim))!));
        Assert.Equal((255, 0, 0), Center((await IconOf(deadSpace))!));

        // Listagem real reconhece o jogo pelo conteúdo.
        var listing = await new LocalFileSystemProvider().ListAsync(_root, includeHidden: false, TestContext.Current.CancellationToken);
        Assert.True(listing.Entries.Single(e => e.Name == "Valheim.url").IsSteamGame);

        // Índice num .dll do sistema: ícones diferentes por índice (o mesmo caminho que a Steam/.lnk usam).
        var imageres = Path.Join(Environment.SystemDirectory, "imageres.dll");
        var a = await IconOf(WriteUrl("A.url", "steam://rungameid/1", imageres, 2));
        var b = await IconOf(WriteUrl("B.url", "steam://rungameid/2", imageres, 3));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.False(a.Pixels.Span.SequenceEqual(b.Pixels.Span));
    }

    [Fact]
    public async Task Missing_remote_and_linked_icon_paths_fall_back_without_touching_them()
    {
        var watch = Stopwatch.StartNew();
        Assert.Null(await IconOf(WriteUrl("Sumido.url", "steam://rungameid/3", Path.Join(_root, "nao-existe.ico"))));
        Assert.Null(await IconOf(WriteUrl("Remoto.url", "steam://rungameid/4", @"\\192.0.2.1\x\jogo.ico"))); // TEST-NET: nunca responde
        Assert.Null(await IconOf(WriteUrl("Relativo.url", "steam://rungameid/5", "jogo.ico")));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "caminho remoto não pode ser consultado");

        // Pasta que é junção no meio do caminho: recusada (um link pode levar a qualquer lugar).
        var real = Directory.CreateDirectory(Path.Join(_root, "real")).FullName;
        File.Copy(WriteIcon("x.ico", 0, 255, 0), Path.Join(real, "x.ico"));
        var link = Path.Join(_root, "juncao");
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, real }, UseShellExecute = false, CreateNoWindow = true })!)
        {
            await mklink.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, mklink.ExitCode);
        }
        Assert.NotNull(ShortcutFiles.ResolveLocal(Path.Join(real, "x.ico"), iconFile: true));
        Assert.Null(ShortcutFiles.ResolveLocal(Path.Join(link, "x.ico"), iconFile: true));
        Assert.Null(await IconOf(WriteUrl("Juncao.url", "steam://rungameid/6", Path.Join(link, "x.ico"))));
    }

    [Fact]
    public async Task Lnk_shortcuts_show_their_own_icon_and_never_a_remote_one()
    {
        var target = Path.Join(_root, "nota.txt");
        File.WriteAllText(target, "x");
        var green = WriteIcon("verde.ico", 0, 255, 0);

        Assert.Equal((0, 255, 0), Center((await IconOf(WriteLnk("Com icone.lnk", target, green + ",0")))!));
        Assert.NotNull(await IconOf(WriteLnk("Programa.lnk", Path.Join(Environment.SystemDirectory, "cmd.exe"), null))); // ícone do destino
        Assert.NotNull(await IconOf(WriteLnk("Documento.lnk", target, null))); // ícone do tipo do destino
        Assert.Null(await IconOf(WriteLnk("Remoto.lnk", target, @"\\192.0.2.1\x\jogo.ico,0")));
    }

    [Fact]
    public void Opening_a_steam_shortcut_without_steam_gives_a_readable_error()
    {
        using (var steam = Registry.ClassesRoot.OpenSubKey("steam"))
            if (steam?.GetValue("URL Protocol") is not null) Assert.Skip("Steam instalada neste Windows: não abrimos jogos em testes.");
        var shortcut = WriteUrl("Valheim.url", "steam://rungameid/892970", null);
        var error = Assert.Throws<ShellException>(() => new WindowsShellService().Open(shortcut));
        Assert.Contains("Steam não está instalada", error.Message, StringComparison.Ordinal);
    }
}
