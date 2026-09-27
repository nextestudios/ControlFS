namespace ControlFS.Core.Layout;

/// <summary>Faixa de layout escolhida pelo tamanho efetivo da janela (não pelo tipo de aparelho).</summary>
public enum LayoutTier
{
    /// <summary>Portáteis 1280×720/800, janelas pequenas e TVs 4K a 300%: espaçamento enxuto, nada essencial cortado.</summary>
    Compact,

    /// <summary>Desktop e TV 1080p a 100% (ou 4K a 200%): os tokens de referência.</summary>
    Regular,

    /// <summary>Telas grandes com escala baixa (4K a 100–125%): texto e espaços crescem para ler a ~3 m.</summary>
    Large,
}

/// <summary>
/// Medidas derivadas da faixa. Os tamanhos de fonte da tela são multiplicados por <see cref="FontScale"/>; espaços,
/// alturas fixas e larguras máximas por <see cref="SpaceScale"/>. O fator de texto do Windows NÃO entra no
/// <see cref="FontScale"/>: o WinUI já o aplica sozinho sobre cada texto; aqui ele só decide quanto espaço sobra.
/// </summary>
public sealed record LayoutProfile(LayoutTier Tier, double FontScale, double SpaceScale, double RasterizationScale)
{
    /// <summary>Altura das teclas do teclado na tela (pixels efetivos).</summary>
    public double KeyHeight => Tier == LayoutTier.Compact ? 44 : Snap(56 * SpaceScale);

    /// <summary>Altura do logotipo no cabeçalho da tela inicial.</summary>
    public double LogoHeight => Tier == LayoutTier.Compact ? 32 : Snap(44 * SpaceScale);

    /// <summary>Arredonda para pixels físicos inteiros (linhas e bordas nítidas a 125%, 150%, 175%…).</summary>
    public double Snap(double value) => Math.Round(value * RasterizationScale) / RasterizationScale;

    /// <summary>Tokens de referência (1920×1080 a 100%).</summary>
    public static LayoutProfile Default { get; } = new(LayoutTier.Regular, 1, 1, 1);
}

/// <summary>
/// Escolhe a faixa de layout. Função pura para ser testada: recebe o tamanho EFETIVO da área do app (pixels
/// independentes de dispositivo, ou seja, já dividido pela escala do Windows), a escala de rasterização (DPI/96) e o
/// fator de texto do Windows (Configurações → Acessibilidade → Tamanho do texto).
/// </summary>
public static class LayoutBreakpoints
{
    /// <summary>Abaixo desta altura "em texto" (efetiva ÷ fator de texto) o layout fica compacto (720p, 800p, 768p).</summary>
    public const double CompactBelowHeight = 900;

    /// <summary>Abaixo desta largura "em texto" também fica compacto (janela estreita, 1280 de largura).</summary>
    public const double CompactBelowWidth = 1360;

    /// <summary>A partir desta altura o conteúdo cresce proporcionalmente (4K a 100–125%, telas grandes).</summary>
    public const double LargeFromHeight = 1300;

    /// <summary>Altura de referência dos tokens: 1080 efetivos.</summary>
    public const double ReferenceHeight = 1080;

    /// <summary>Crescimento máximo (4K a 100% fica com as proporções de 1080p).</summary>
    public const double MaxScale = 2;

    public static LayoutProfile Select(double effectiveWidth, double effectiveHeight, double rasterizationScale = 1, double textScaleFactor = 1)
    {
        if (!(rasterizationScale > 0) || double.IsInfinity(rasterizationScale)) rasterizationScale = 1;
        if (!(textScaleFactor >= 1) || double.IsInfinity(textScaleFactor)) textScaleFactor = 1;
        if (!(effectiveWidth > 0) || !(effectiveHeight > 0)) return LayoutProfile.Default with { RasterizationScale = rasterizationScale };

        // Texto maior do Windows ocupa mais espaço: o que importa é quantas "linhas de texto" cabem.
        var width = effectiveWidth / textScaleFactor;
        var height = effectiveHeight / textScaleFactor;
        if (height < CompactBelowHeight || width < CompactBelowWidth)
            return new LayoutProfile(LayoutTier.Compact, 1, 0.75, rasterizationScale);
        if (height < LargeFromHeight)
            return new LayoutProfile(LayoutTier.Regular, 1, 1, rasterizationScale);
        // Cresce pela menor proporção (altura ou largura 16:9) para nunca faltar espaço em telas mais estreitas.
        var scale = Math.Min(MaxScale, Math.Min(height / ReferenceHeight, width / (ReferenceHeight * 16 / 9)));
        scale = Math.Max(1, Math.Round(scale * 4) / 4); // passos de 25% (menos relayouts ao redimensionar)
        return new LayoutProfile(scale > 1 ? LayoutTier.Large : LayoutTier.Regular, scale, scale, rasterizationScale);
    }
}
