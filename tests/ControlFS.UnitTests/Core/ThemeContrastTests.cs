using ControlFS.Core.Appearance;

namespace ControlFS.UnitTests.Core;

/// <summary>#37: os dois temas com todas as cores de destaque atingem os contrastes-alvo (WCAG 2.x).</summary>
public class ThemeContrastTests
{
    public static TheoryData<bool, AccentColor> Palettes()
    {
        var data = new TheoryData<bool, AccentColor>();
        foreach (var dark in new[] { true, false })
            foreach (var accent in ThemePalettes.All) data.Add(dark, accent);
        return data;
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Every_theme_and_accent_meets_the_contrast_targets(bool dark, AccentColor accent)
    {
        var p = ThemePalettes.Build(dark, accent);
        void Text(uint fg, uint bg, string what) => Assert.True(ThemeContrast.Ratio(fg, bg) >= 4.5, $"{what}: {ThemeContrast.Ratio(fg, bg):0.00}");
        void Ui(uint fg, uint bg, string what) => Assert.True(ThemeContrast.Ratio(fg, bg) >= 3, $"{what}: {ThemeContrast.Ratio(fg, bg):0.00}");

        foreach (var (surface, name) in new[] { (p.Background, "fundo"), (p.Surface, "faixas"), (p.SurfaceRaised, "cartões"), (p.ModalSolid, "modal") })
        {
            Text(p.Text, surface, "texto/" + name);
            Text(p.TextMuted, surface, "texto secundário/" + name);
            Text(p.Danger, surface, "perigo/" + name);
            Ui(p.Accent, surface, "anel de foco/" + name);
            Ui(p.Selected, surface, "marcado/" + name);
            Ui(p.Success, surface, "sucesso/" + name);
        }
        Text(p.Text, p.AccentSoft, "texto no item focado");
        Text(p.TextMuted, p.AccentSoft, "detalhe no item focado");
        Ui(p.Accent, p.AccentSoft, "anel sobre o item focado");
        Text(p.FocusText, p.Accent, "opção focada preenchida");
        Text(p.FocusText, p.DangerFill, "opção perigosa focada");
        Text(p.TextMuted, p.DisabledFill, "opção indisponível focada");
        Text(p.Text, ThemeContrast.Over(p.ModalInset, p.ModalSolid), "informação no painel");

        // Teclado virtual (auditoria de UX, P2-12): as teclas se destacam do painel e o texto delas continua legível.
        var key = ThemeContrast.Over(p.KeyFill, p.ModalSolid);
        var function = ThemeContrast.Over(p.KeyFunctionFill, p.ModalSolid);
        Assert.True(ThemeContrast.Ratio(key, p.ModalSolid) >= 1.5, $"tecla/painel: {ThemeContrast.Ratio(key, p.ModalSolid):0.00}");
        Assert.True(ThemeContrast.Ratio(function, p.ModalSolid) >= 1.15, $"tecla de função/painel: {ThemeContrast.Ratio(function, p.ModalSolid):0.00}");
        Text(p.Text, key, "letra na tecla");
        Text(p.Text, function, "texto na tecla de função");
        Text(p.Accent, function, "Concluir e página atual (destaque na tecla de função)");
        Assert.Equal(dark, p.IsDark);
    }

    [Fact]
    public void Automatic_theme_follows_windows_and_fixed_themes_ignore_it()
    {
        Assert.True(ThemePalettes.IsDark(ThemeMode.System, systemIsDark: true));
        Assert.False(ThemePalettes.IsDark(ThemeMode.System, systemIsDark: false));
        Assert.True(ThemePalettes.IsDark(ThemeMode.Dark, systemIsDark: false));
        Assert.False(ThemePalettes.IsDark(ThemeMode.Light, systemIsDark: true));
    }
}
