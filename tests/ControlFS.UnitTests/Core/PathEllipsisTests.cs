using ControlFS.Core.Text;

namespace ControlFS.UnitTests.Core;

/// <summary>Auditoria de UX (P2-6): o caminho longo perde o meio, nunca a unidade nem a pasta final.</summary>
public class PathEllipsisTests
{
    [Fact]
    public void Long_paths_keep_the_root_and_the_last_folders()
    {
        const string path = @"C:\Users\erick\Documents\Empresa\Financeiro\2026\Relatórios trimestrais";
        Assert.Equal(path, PathEllipsis.Middle(path, 200));
        Assert.Equal(@"C:\…\2026\Relatórios trimestrais", PathEllipsis.Middle(path, 34));
        Assert.Equal(@"C:\…\Relatórios trimestrais", PathEllipsis.Middle(path, 10)); // a pasta final fica inteira
        Assert.Equal(@"\\nas\filmes\…\Relatórios trimestrais", PathEllipsis.Middle(@"\\nas\filmes\Séries\2026\Relatórios trimestrais", 38));
    }
}
