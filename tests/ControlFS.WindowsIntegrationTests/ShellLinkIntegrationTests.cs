using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Windows.Shell;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public class ShellLinkIntegrationTests
{
    /// <summary>Fronteira de segurança: só https simples chega ao navegador; nada que execute programas ou leve credenciais.</summary>
    [Fact]
    public void OpenLink_refuses_everything_but_plain_https()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        var shell = new WindowsShellService();
        string[] refused =
        [
            "http://nextboost.pro/",
            "file:///C:/Windows/System32/cmd.exe",
            "steam://rungameid/1",
            "ms-msdt:/id",
            "ftp://exemplo.com/a",
            "https://usuario:senha@nextboost.pro/",
            "https://exemplo.com/" + new string('a', 250),
        ];
        foreach (var url in refused) Assert.Throws<ShellException>(() => shell.OpenLink(new Uri(url)));
        Assert.Throws<ShellException>(() => shell.OpenLink(new Uri("relativo/caminho", UriKind.Relative)));
    }
}
