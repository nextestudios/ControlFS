using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Windows.Shell;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Core;

/// <summary>Atalhos .url/.lnk: conteúdo não confiável (#168). Reconhecer jogos da Steam sem nunca tocar em caminho remoto.</summary>
public class ShortcutTests
{
    public const string SteamUrl = "[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,0\r\n[InternetShortcut]\r\nIDList=\r\nIconIndex=0\r\n" +
        "URL=steam://rungameid/892970\r\nIconFile=C:\\Program Files (x86)\\Steam\\steam\\games\\1bd1b2b4bb8e2c3f5a7d6e9f0a1b2c3d4e5f6a7b.ico\r\n";

    public const string WebUrl = "[InternetShortcut]\nURL=https://example.com/\n";

    [Fact]
    public void Steam_and_web_shortcuts_are_told_apart_only_by_the_url_scheme()
    {
        var steam = InternetShortcut.Parse(Encoding.UTF8.GetBytes(SteamUrl))!;
        Assert.True(steam.IsSteamGame);
        Assert.Equal("892970", steam.SteamAppId);
        Assert.Equal(@"C:\Program Files (x86)\Steam\steam\games\1bd1b2b4bb8e2c3f5a7d6e9f0a1b2c3d4e5f6a7b.ico", steam.IconFile);
        Assert.Equal(0, steam.IconIndex);

        var web = InternetShortcut.Parse(Encoding.UTF8.GetBytes(WebUrl))!;
        Assert.False(web.IsSteamGame);
        Assert.Null(web.IconFile);
        // "steam" em outro lugar da URL não conta: só o esquema.
        Assert.False(InternetShortcut.Parse(Encoding.UTF8.GetBytes("[InternetShortcut]\nURL=https://store.steampowered.com/app/892970\n"))!.IsSteamGame);
    }

    [Fact]
    public void Parsing_is_tolerant_but_keeps_the_first_value_and_ignores_other_sections()
    {
        var text = "; comentário\n[Outra]\nURL=steam://rungameid/1\n[InternetShortcut]\nlixo sem igual\nURL = \"https://a.example/\"\n" +
            "URL=steam://rungameid/2\nIconFile=C:\\x\\a.ico\nIconIndex=abc\n";
        var parsed = InternetShortcut.Parse(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray())!; // UTF-16 com BOM
        Assert.Equal("https://a.example/", parsed.Url);
        Assert.Equal(@"C:\x\a.ico", parsed.IconFile);
        Assert.Equal(0, parsed.IconIndex); // índice inválido vira 0

        Assert.Null(InternetShortcut.Parse(Encoding.UTF8.GetBytes("URL=steam://rungameid/1\n"))); // sem a seção
        Assert.Equal(-102, InternetShortcut.Parse(Encoding.UTF8.GetBytes("[InternetShortcut]\nIconIndex=-102\n"))!.IconIndex);
    }

    [Fact]
    public void Oversized_shortcut_files_are_not_read()
    {
        using var tmp = new TempDir();
        var big = tmp.Sub("grande.url");
        File.WriteAllText(big, SteamUrl + new string(';', InternetShortcut.MaxFileBytes));
        Assert.Null(ShortcutFiles.ReadInternetShortcut(big));
        Assert.Null(InternetShortcut.Parse(new byte[InternetShortcut.MaxFileBytes + 1]));

        var small = tmp.Sub("Valheim.url");
        File.WriteAllText(small, SteamUrl);
        Assert.True(ShortcutFiles.ReadInternetShortcut(small)!.IsSteamGame);
    }

    [Theory]
    [InlineData(@"\\servidor\compartilhado\jogo.ico")] // UNC: o Windows mandaria as credenciais (NTLM)
    [InlineData(@"//servidor/compartilhado/jogo.ico")]
    [InlineData(@"\\?\C:\jogos\jogo.ico")] // prefixos de dispositivo
    [InlineData(@"\\.\C:\jogos\jogo.ico")]
    [InlineData(@"\??\C:\jogos\jogo.ico")]
    [InlineData(@"\\?\UNC\servidor\x\jogo.ico")]
    [InlineData("file://servidor/x/jogo.ico")] // URLs
    [InlineData("file:///C:/jogos/jogo.ico")]
    [InlineData("https://exemplo.com/jogo.ico")]
    [InlineData("jogo.ico")] // relativos
    [InlineData(@"..\jogo.ico")]
    [InlineData(@"\jogos\jogo.ico")]
    [InlineData(@"C:jogo.ico")]
    [InlineData(@"C:\jogos\..\jogo.ico")]
    [InlineData(@"C:\jogos\jogo.ico:fluxo")] // fluxo alternativo
    [InlineData(@"C:\jogos\NUL.ico")] // dispositivo
    [InlineData(@"%LOGONSERVER%\x\jogo.ico")] // variável por expandir (pode virar UNC)
    [InlineData(@"C:\jogos\jogo.txt")] // não é arquivo de ícone
    [InlineData("")]
    public void Remote_relative_and_device_icon_paths_are_refused(string path) => Assert.Null(IconLocationPolicy.NormalizeIconFile(path));

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam\games\abc.ico", @"C:\Program Files (x86)\Steam\steam\games\abc.ico")]
    [InlineData("d:/steam/steam/games/abc.ico", @"d:\steam\steam\games\abc.ico")]
    [InlineData(@"C:\Windows\System32\imageres.dll", @"C:\Windows\System32\imageres.dll")]
    public void Local_absolute_icon_paths_are_accepted(string path, string expected) => Assert.Equal(expected, IconLocationPolicy.NormalizeIconFile(path));

    [Fact]
    public void Remote_icon_paths_are_refused_before_any_disk_access() =>
        Assert.Null(ShortcutFiles.ResolveLocal(@"\\192.0.2.1\compartilhado\jogo.ico", iconFile: true)); // TEST-NET: nunca responde

    [Fact]
    public void Shortcut_icons_are_cached_per_path_and_invalidated_when_the_shortcut_changes()
    {
        var when = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var steam = new FileEntry("v", "Valheim.url", EntryKind.File, Size: 200, Modified: when, FullPath: @"C:\Users\ana\Desktop\Valheim.url",
            Shortcut: new InternetShortcut("steam://rungameid/892970", @"C:\s\a.ico", 0));
        var request = IconRequest.For(steam)!;
        Assert.Equal(IconSourceKind.Shortcut, request.Kind);
        Assert.Equal(steam.FullPath, request.Value);
        Assert.Equal(request.Key, IconRequest.For(steam with { Id = "outra lista" })!.Key);
        Assert.NotEqual(request.Key, IconRequest.For(steam with { Modified = when.AddSeconds(1) })!.Key);
        Assert.NotEqual(request.Key, IconRequest.For(steam with { Size = 201 })!.Key);
        Assert.NotEqual(request.Key, IconRequest.For(steam with { FullPath = @"C:\Users\ana\Desktop\Dead Space.url", Name = "Dead Space.url" })!.Key);

        // Site comum continua com o ícone do tipo (.url); .lnk usa o próprio ícone.
        var web = steam with { Shortcut = new InternetShortcut("https://example.com/", null, 0) };
        Assert.Equal((IconSourceKind.Extension, "ext:.url"), (IconRequest.For(web)!.Kind, IconRequest.For(web)!.Key));
        Assert.Equal(IconSourceKind.Shortcut, IconRequest.For(new FileEntry("l", "Bloco.lnk", EntryKind.File, FullPath: @"C:\x\Bloco.lnk"))!.Kind);
        // Dentro de um compactado não há arquivo para ler.
        Assert.Equal(IconSourceKind.Extension, IconRequest.For(new FileEntry("a", "Bloco.lnk", EntryKind.ArchiveFile))!.Kind);
    }
}
