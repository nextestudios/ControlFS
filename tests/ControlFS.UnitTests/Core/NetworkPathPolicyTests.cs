using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.UnitTests.Core;

/// <summary>
/// Destinos de "Locais de rede" vêm de .lnk e do registro (não confiáveis, #27): só compartilhamentos UNC entram na lista,
/// e um local de rede nunca pede o ícone ao Shell (isso contataria o servidor).
/// </summary>
public class NetworkPathPolicyTests
{
    [Theory]
    [InlineData(@"\\nas\filmes", @"\\nas\filmes")]
    [InlineData(@"\\NAS\Filmes\Séries\", @"\\NAS\Filmes\Séries")]
    [InlineData("//nas/filmes", @"\\nas\filmes")]
    [InlineData(@"\\192.168.0.10\media", @"\\192.168.0.10\media")]
    [InlineData(@"\\nas", null)]
    [InlineData(@"\\?\C:\Windows", null)]
    [InlineData(@"\\.\pipe\x", null)]
    [InlineData(@"\\?\UNC\nas\filmes", null)]
    [InlineData(@"\\nas\filmes\..\c$", null)]
    [InlineData(@"\\nas\fil*mes", null)]
    [InlineData(@"\\nas\filmes:stream", null)]
    [InlineData(@"\\nas\\filmes", null)]
    [InlineData(@"C:\Users", null)]
    [InlineData("https://nas/filmes", null)]
    [InlineData("filmes", null)]
    [InlineData("", null)]
    public void Only_smb_shares_in_unc_form_are_accepted(string raw, string? expected) =>
        Assert.Equal(expected, NetworkPathPolicy.NormalizeShare(raw));

    [Fact]
    public void Network_drives_use_only_the_network_symbol_and_never_ask_the_shell_for_an_icon()
    {
        Assert.Null(IconRequest.For(new FileEntry(@"drive:Z:\", "filmes (Z:)", EntryKind.Drive, FullPath: @"Z:\", Drive: DriveKind.Network)));
        Assert.Null(IconRequest.For(new FileEntry(@"net:\\nas\filmes", "NAS", EntryKind.Drive, FullPath: @"\\nas\filmes", Drive: DriveKind.Network)));
        Assert.Equal("filmes", NetworkPathPolicy.ShareName(@"\\nas\filmes"));
    }
}
