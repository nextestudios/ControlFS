using System.Globalization;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;

namespace ControlFS.App.Controls;

/// <summary>
/// Linha de lista: modelo mínimo carregado em tempo de execução (sem bindings), preenchido em
/// ContainerContentChanging — padrão recomendado para listas virtualizadas grandes. Duas densidades: confortável (duas
/// linhas, para TV) e compacta (uma linha com colunas de tipo, tamanho e data). Estados nunca dependem só de cor:
/// foco = anel + fundo; marcado = faixa à esquerda + caixa marcada + texto; recortado = tesoura + texto + esmaecido.
/// </summary>
public static class EntryRowTemplate
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    private const string IconFont = "FontFamily=\"Segoe Fluent Icons, Segoe MDL2 Assets\"";

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Fonte na escala da faixa de layout (portátil, desktop, TV grande).</summary>
    private static string F(double size) => N(Math.Round(size * Theme.Layout.FontScale * Theme.SimulatedTextScale));

    /// <summary>Espaço na escala da faixa de layout.</summary>
    private static string S(double size) => N(Theme.Layout.Snap(size * Theme.Layout.SpaceScale));

    /// <summary>Medida que acompanha o texto (ícones, colunas de largura fixa).</summary>
    private static string W(double size) => N(Theme.Scaled(size));

    private static string IconCell(double size, double glyph) =>
        $"<Grid Width=\"{W(size)}\" Height=\"{W(size)}\" VerticalAlignment=\"Center\">" +
        $"<TextBlock x:Name=\"Icon\" {IconFont} FontSize=\"{F(glyph)}\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
        $"<Image x:Name=\"IconImage\" Width=\"{W(size)}\" Height=\"{W(size)}\" Stretch=\"Uniform\" Visibility=\"Collapsed\"/>" +
        "</Grid>";

    private static string StateCell(int column, double glyph, double text) =>
        $"<StackPanel Grid.Column=\"{column}\" Orientation=\"Horizontal\" Spacing=\"{S(6)}\" VerticalAlignment=\"Center\">" +
        $"<TextBlock x:Name=\"StateGlyph\" {IconFont} FontSize=\"{F(glyph)}\" VerticalAlignment=\"Center\"/>" +
        $"<TextBlock x:Name=\"StateText\" FontSize=\"{F(text)}\" FontWeight=\"SemiBold\" VerticalAlignment=\"Center\"/>" +
        "</StackPanel>";

    /// <summary>Halo (tag "glow", ver <see cref="Theme.ApplyFocus"/>) em volta do anel; o fundo do anel muda com transição curta.</summary>
    private static string Frame(string body, string tag = "row") =>
        $"<DataTemplate {Ns}><Border Tag=\"glow\" BorderThickness=\"{N(Theme.GlowRing.Left)}\" CornerRadius=\"{N(Theme.Radius.TopLeft + Theme.GlowRing.Left)}\">" +
        $"<Border x:Name=\"Ring\" Tag=\"{tag}\" BorderThickness=\"{N(Theme.FocusRing.Left)}\" CornerRadius=\"{N(Theme.Radius.TopLeft)}\">" +
        $"<Border.BackgroundTransition><BrushTransition Duration=\"0:0:0.{Theme.MotionFocus.Milliseconds:000}\"/></Border.BackgroundTransition><Grid>" +
        $"<Border x:Name=\"MarkBar\" Width=\"{W(6)}\" HorizontalAlignment=\"Left\" CornerRadius=\"3\" Margin=\"2,6,0,6\"/>" +
        body + "</Grid></Border></Border></DataTemplate>";

    /// <summary>
    /// Colunas da lista (redesenho, fase C): [caixa de marcação] [ícone] Nome | Tipo | Tamanho | Modificado em | seta. As
    /// mesmas medidas servem às linhas e ao cabeçalho (alinhados). Sem espaço, a coluna de tipo sai primeiro (o tipo
    /// continua no painel de detalhes e em Propriedades); sem marcação possível (início, busca), a caixa sai.
    /// </summary>
    public sealed record ListColumns(bool Compact, bool Mark, bool Type, double MarkWidth, double IconWidth, double TypeWidth, double SizeWidth,
        double DateWidth, double ChevronWidth, double Spacing, Thickness Padding, double RowHeight)
    {
        /// <summary>Distância da borda da linha até o conteúdo (halo + anel), igual no cabeçalho.</summary>
        public static double Inset => Theme.GlowRing.Left + Theme.FocusRing.Left;

        /// <summary>Largura que as colunas fixas ocupam (o nome fica com o resto).</summary>
        public double FixedWidth => (2 * Inset) + Padding.Left + Padding.Right + (Mark ? MarkWidth + Spacing : 0) + IconWidth + Spacing
            + (Type ? TypeWidth + Spacing : 0) + SizeWidth + Spacing + DateWidth + Spacing + ChevronWidth + Spacing;

        public string Key => $"{Compact}|{Mark}|{Type}|{Theme.Layout}|{Theme.SimulatedTextScale}";
    }

    /// <summary>Medidas das colunas para a largura da lista.</summary>
    public static ListColumns Columns(ListDensity density, double listWidth, bool mark)
    {
        var compact = density == ListDensity.Compact;
        var columns = compact
            ? new ListColumns(true, mark, true, Theme.Scaled(32), Theme.Scaled(28), Theme.Scaled(170), Theme.Scaled(110), Theme.Scaled(180), Theme.Scaled(22),
                Theme.Space(12), new Thickness(Theme.Space(14), Theme.Space(2), Theme.Space(14), Theme.Space(2)), Theme.Scaled(44))
            : new ListColumns(false, mark, true, Theme.Scaled(44), Theme.Scaled(60), Theme.Scaled(200), Theme.Scaled(140), Theme.Scaled(220), Theme.Scaled(28),
                Theme.Space(18), new Thickness(Theme.Space(18), Theme.Space(6), Theme.Space(22), Theme.Space(6)), Theme.Scaled(88));
        // O nome precisa de espaço para ser lido: sem ele, o tipo sai da linha.
        return listWidth > 0 && listWidth - columns.FixedWidth < Theme.Scaled(compact ? 260 : 320) ? columns with { Type = false } : columns;
    }

    /// <summary>Grade das colunas (linha e cabeçalho): índices das colunas e as definições.</summary>
    public static (int Mark, int Icon, int Name, int Type, int Size, int Date, int Chevron, string Definitions) ColumnLayout(ListColumns c)
    {
        var defs = new List<string>();
        int Add(double? width)
        {
            defs.Add(width is { } w ? $"<ColumnDefinition Width=\"{N(w)}\"/>" : "<ColumnDefinition Width=\"*\"/>");
            return defs.Count - 1;
        }
        var mark = c.Mark ? Add(c.MarkWidth) : -1;
        var icon = Add(c.IconWidth);
        var name = Add(null);
        var type = c.Type ? Add(c.TypeWidth) : -1;
        var size = Add(c.SizeWidth);
        var date = Add(c.DateWidth);
        var chevron = Add(c.ChevronWidth);
        return (mark, icon, name, type, size, date, chevron, "<Grid.ColumnDefinitions>" + string.Concat(defs) + "</Grid.ColumnDefinitions>");
    }

    /// <summary>
    /// Linha da lista: alta na densidade confortável (TV/controle), baixa na compacta. Foco = anel ciano + fundo azul +
    /// halo e seta em destaque; marcado = caixa marcada + faixa âmbar + "Marcado" (o foco nunca marca). Estados, pasta
    /// do resultado e atributos numa segunda linha discreta (confortável) ou ao lado do nome (compacta).
    /// </summary>
    private static string ListXaml(ListColumns c)
    {
        var (mark, icon, name, type, size, date, chevron, definitions) = ColumnLayout(c);
        var titleSize = c.Compact ? 17 : 22;
        var columnSize = c.Compact ? 15 : 19;
        var iconSize = c.Compact ? 24 : 52;
        string Cell(int column, string xname, string extra = "") =>
            $"<TextBlock x:Name=\"{xname}\" Grid.Column=\"{column}\" FontSize=\"{F(columnSize)}\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\" VerticalAlignment=\"Center\"{extra}/>";
        var states =
            $"<StackPanel x:Name=\"StateLine\" Orientation=\"Horizontal\" Spacing=\"{S(6)}\" VerticalAlignment=\"Center\"{(c.Compact ? " Grid.Column=\"1\" Margin=\"" + S(10) + ",0,0,0\"" : string.Empty)}>" +
            $"<TextBlock x:Name=\"StateGlyph\" {IconFont} FontSize=\"{F(c.Compact ? 13 : 15)}\" VerticalAlignment=\"Center\"/>" +
            $"<TextBlock x:Name=\"StateText\" FontSize=\"{F(c.Compact ? 13 : 15)}\" VerticalAlignment=\"Center\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\"/>" +
            "</StackPanel>";
        var title = $"<TextBlock x:Name=\"Title\" FontSize=\"{F(titleSize)}\"{(c.Compact ? string.Empty : " FontWeight=\"Medium\"")} TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\" VerticalAlignment=\"Center\"/>";
        var nameCell = c.Compact
            ? $"<Grid Grid.Column=\"{name}\" VerticalAlignment=\"Center\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>{title}{states}</Grid>"
            : $"<StackPanel Grid.Column=\"{name}\" VerticalAlignment=\"Center\" Spacing=\"{S(2)}\">{title}{states}</StackPanel>";
        var body =
            $"<Grid x:Name=\"Body\" MinHeight=\"{N(c.RowHeight - (2 * ListColumns.Inset))}\" Padding=\"{N(c.Padding.Left)},{N(c.Padding.Top)},{N(c.Padding.Right)},{N(c.Padding.Bottom)}\" ColumnSpacing=\"{N(c.Spacing)}\">" +
            definitions +
            (mark >= 0 ? $"<TextBlock x:Name=\"Check\" Grid.Column=\"{mark}\" {IconFont} FontSize=\"{F(c.Compact ? 18 : 26)}\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\"/>" : string.Empty) +
            IconCell(iconSize, iconSize * (c.Compact ? 0.8 : 0.7)).Replace("<Grid Width=", $"<Grid Grid.Column=\"{icon}\" HorizontalAlignment=\"Left\" Width=", StringComparison.Ordinal) +
            nameCell +
            (type >= 0 ? Cell(type, "TypeColumn") : string.Empty) +
            Cell(size, "SizeColumn") +
            Cell(date, "DateColumn") +
            $"<TextBlock x:Name=\"Chevron\" Grid.Column=\"{chevron}\" {IconFont} Text=\"&#xE76C;\" FontSize=\"{F(c.Compact ? 16 : 24)}\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\"/>" +
            "</Grid>";
        // Separador discreto entre linhas (fora do anel: o foco nunca é coberto).
        return $"<DataTemplate {Ns}><Grid>" +
            $"<Border Tag=\"glow\" BorderThickness=\"{N(Theme.GlowRing.Left)}\" CornerRadius=\"{N(Theme.Radius.TopLeft + Theme.GlowRing.Left)}\">" +
            $"<Border x:Name=\"Ring\" Tag=\"row\" BorderThickness=\"{N(Theme.FocusRing.Left)}\" CornerRadius=\"{N(Theme.Radius.TopLeft)}\">" +
            $"<Border.BackgroundTransition><BrushTransition Duration=\"0:0:0.{Theme.MotionFocus.Milliseconds:000}\"/></Border.BackgroundTransition><Grid>" +
            $"<Border x:Name=\"MarkBar\" Width=\"{W(4)}\" HorizontalAlignment=\"Left\" CornerRadius=\"2\" Margin=\"2,{S(10)},0,{S(10)}\"/>" +
            body + "</Grid></Border></Border>" +
            $"<Border x:Name=\"Separator\" Height=\"{N(Theme.Hairline.Top)}\" VerticalAlignment=\"Bottom\" Margin=\"{N(ListColumns.Inset + c.Padding.Left)},0,{N(ListColumns.Inset + c.Padding.Right)},0\" IsHitTestVisible=\"False\"/>" +
            "</Grid></DataTemplate>";
    }

    /// <summary>
    /// Medidas do cartão da grade: largura mínima (define as colunas), altura e ícone, na escala da faixa de layout.
    /// Confortável: 3 colunas em 1080p, 2 no portátil, 1 em janela estreita, 4+ numa TV grande; compacta: cartões menores.
    /// </summary>
    public static (double MinWidth, double Height, double Icon) TileSize(ListDensity density)
    {
        var large = Theme.Layout.Tier == Core.Layout.LayoutTier.Large ? 0.9 : 1;
        return density == ListDensity.Compact
            ? (Theme.Scaled(360) * large, Theme.Scaled(92), 44)
            : (Theme.Scaled(480) * large, Theme.Scaled(124), 64);
    }

    /// <summary>Espaço entre cartões (metade de cada lado do cartão).</summary>
    public static double TileGap => Theme.Space(20);

    /// <summary>
    /// Cartão da grade (redesenho, fase B2), no desenho dos cartões do início: [ícone grande] nome / tipo e tamanho
    /// ("Pasta", "Arquivo ZIP · 12 MB") / estados (marcado, recortado, com senha) ou a pasta do resultado da busca, e a
    /// seta nas pastas. Mesmos nomes de elementos da linha: <see cref="Fill"/> preenche os dois.
    /// </summary>
    private static string TileXaml(ListDensity density)
    {
        var compact = density == ListDensity.Compact;
        var (_, _, iconSize) = TileSize(density);
        var half = N(TileGap / 2);
        return Frame(
            $"<Grid x:Name=\"Body\" Padding=\"{S(compact ? 16 : 24)},{S(8)},{S(compact ? 14 : 20)},{S(8)}\" ColumnSpacing=\"{S(compact ? 16 : 24)}\">" +
            $"<Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
            IconCell(iconSize, iconSize * 0.7) +
            $"<StackPanel Grid.Column=\"1\" VerticalAlignment=\"Center\" Spacing=\"{S(3)}\">" +
            $"<TextBlock x:Name=\"Title\" FontSize=\"{F(compact ? 17 : 21)}\" TextTrimming=\"CharacterEllipsis\" TextWrapping=\"NoWrap\" MaxLines=\"1\"/>" +
            $"<TextBlock x:Name=\"TileDetail\" FontSize=\"{F(compact ? 14 : 17)}\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\"/>" +
            $"<StackPanel x:Name=\"TileStates\" Orientation=\"Horizontal\" Spacing=\"{S(6)}\">" +
            $"<TextBlock x:Name=\"StateGlyph\" {IconFont} FontSize=\"{F(compact ? 13 : 15)}\" VerticalAlignment=\"Center\"/>" +
            $"<TextBlock x:Name=\"StateText\" FontSize=\"{F(compact ? 13 : 16)}\" FontWeight=\"SemiBold\" VerticalAlignment=\"Center\" TextTrimming=\"CharacterEllipsis\"/>" +
            "</StackPanel></StackPanel>" +
            $"<TextBlock x:Name=\"Chevron\" Grid.Column=\"2\" {IconFont} Text=\"&#xE76C;\" FontSize=\"{F(compact ? 18 : 22)}\" VerticalAlignment=\"Center\"/>" +
            "</Grid>", "tile").Replace("<Border Tag=\"glow\"", $"<Border Tag=\"glow\" Margin=\"{half}\"", StringComparison.Ordinal);
    }

    /// <summary>Modelo do bloco da grade na densidade pedida (recriar quando a faixa de layout mudar).</summary>
    public static DataTemplate CreateTile(ListDensity density) => (DataTemplate)XamlReader.Load(TileXaml(density));

    /// <summary>Modelo das linhas com as colunas pedidas, nas medidas da faixa de layout atual (recriar quando mudarem).</summary>
    public static DataTemplate Create(ListColumns columns) => (DataTemplate)XamlReader.Load(ListXaml(columns));

    /// <summary>
    /// O que a linha da lista precisa saber além do item: o tipo mostrado (pastas do sistema), o tamanho das pastas com
    /// soma (início), se a lista permite marcar e o relógio das datas amigáveis.
    /// </summary>
    public sealed record RowContext(Func<FileEntry, string> TypeName, Func<FileEntry, string?> FolderSize, bool CanMark, DateTime Now);

    public static void Fill(SelectorItem container, FileEntry entry, bool focused, bool selected, bool cut, IconLoader icons, IReadOnlySet<string>? specialFolders, RowContext? row = null)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root) return;
        var icon = (TextBlock)root.FindName("Icon");
        var title = (TextBlock)root.FindName("Title");

        // Símbolo de reserva (fonte de ícones do Windows, nunca emoji) até o ícone do Shell chegar.
        icon.Text = entry.IsBlocked ? Glyphs.Warning : entry.Kind switch
        {
            EntryKind.Drive => DriveGlyph(entry.Drive),
            EntryKind.KnownFolder when entry.Id == RecycleBinLocation.PlaceId => Glyphs.RecycleBin,
            EntryKind.KnownFolder or EntryKind.Directory or EntryKind.ArchiveDirectory => Glyphs.Folder,
            _ when IsArchiveName(entry.Name) => Glyphs.Archive,
            _ => Glyphs.File,
        };
        icon.Foreground = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        icons.Load((Image)root.FindName("IconImage"), icon, IconRequest.For(entry, specialFolders));
        title.Text = entry.Name;
        title.Foreground = entry.IsBlocked ? Theme.Danger : entry.IsHidden ? Theme.TextMuted : Theme.Text;

        var muted = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        var glyphs = new List<string>();
        var states = new List<string>();
        if (selected) { glyphs.Add(Glyphs.Checked); states.Add("Marcado"); }
        if (cut) { glyphs.Add(Glyphs.Cut); states.Add("Recortado"); }
        if (entry.IsEncrypted) { glyphs.Add(Glyphs.Lock); states.Add("Com senha"); }
        var stateGlyph = (TextBlock)root.FindName("StateGlyph");
        var stateText = (TextBlock)root.FindName("StateText");
        if (root.FindName("SizeColumn") is TextBlock size)
            FillListColumns(root, entry, size, selected, row);
        else if (root.FindName("TileDetail") is TextBlock tileDetail)
        {
            var tileType = row?.TypeName(entry) ?? TypeName(entry);
            var place = entry.Kind is EntryKind.Drive or EntryKind.KnownFolder;
            tileDetail.Text = entry.IsBlocked ? "Bloqueado: " + entry.BlockedReason
                : place ? entry.Detail ?? tileType
                : entry.Size is long bytes && !entry.IsContainer ? $"{tileType} · {Format(bytes)}" : tileType;
            tileDetail.Foreground = muted;
        }
        if (root.FindName("Chevron") is TextBlock chevron) chevron.Visibility = entry.IsContainer && !entry.IsBlocked ? Visibility.Visible : Visibility.Collapsed;

        stateGlyph.Text = string.Join(" ", glyphs);
        var stateParts = new List<string>(states);
        // Sem estado, a linha extra diz onde o resultado da busca está (ou onde estava, na Lixeira); na lista, também os atributos.
        if (entry.FoundIn is { } foundIn && (states.Count == 0 || root.FindName("SizeColumn") is not null)) stateParts.Add("em " + foundIn);
        if (root.FindName("SizeColumn") is not null)
        {
            if (entry is { Kind: EntryKind.ArchiveFile, Detail.Length: > 0 }) stateParts.Add(entry.Detail); // compressão
            stateParts.AddRange(Flags(entry));
        }
        stateText.Text = string.Join(" · ", stateParts);
        stateGlyph.Foreground = stateText.Foreground = selected ? Theme.Selected : Theme.TextMuted;
        stateText.FontWeight = states.Count == 0 ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        if (root.FindName("StateLine") is UIElement line) line.Visibility = stateText.Text.Length > 0 || stateGlyph.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ((Border)root.FindName("MarkBar")).Background = selected ? Theme.Selected : Theme.Transparent;
        if (root.FindName("Separator") is Border separator) separator.Background = Theme.Border;
        // Recortado: conteúdo esmaecido (e indicado em texto e símbolo); o anel de foco e a faixa de marcado continuam nítidos.
        ((UIElement)root.FindName("Body")).Opacity = cut ? 0.55 : 1.0;

        SetFocused(container, focused);
        var state = string.Concat(states.Select(s => ", " + s.ToLowerInvariant()));
        var kind = entry.Kind == EntryKind.Drive ? ", " + TypeName(entry).ToLowerInvariant() : string.Empty;
        AutomationProperties.SetName(container, entry.IsBlocked ? $"{entry.Name}, bloqueado: {entry.BlockedReason}" : $"{entry.Name}{kind}{state}");
    }

    /// <summary>
    /// Colunas da linha: tipo, tamanho e data amigável. Unidades, favoritos, Recentes, Lixeira e entradas bloqueadas não
    /// têm tamanho/data próprios: o texto delas (uso da unidade, caminho, motivo) ocupa as três colunas. Pastas do sistema
    /// no início mostram a soma real (ou "Calculando…").
    /// </summary>
    private static void FillListColumns(FrameworkElement root, FileEntry entry, TextBlock size, bool selected, RowContext? row)
    {
        var date = (TextBlock)root.FindName("DateColumn");
        var type = root.FindName("TypeColumn") as TextBlock;
        var typeName = row?.TypeName(entry) ?? TypeName(entry);
        var folderSize = row?.FolderSize(entry);
        var spanned = entry.IsBlocked || entry.Kind == EntryKind.Drive || (entry.Kind == EntryKind.KnownFolder && folderSize is null);
        var spanText = entry.IsBlocked ? "Bloqueado: " + entry.BlockedReason
            : entry.Kind == EntryKind.Drive ? EntryText.DriveUsage(entry)
            : entry.Detail ?? typeName;
        if (type is not null)
        {
            type.Text = spanned ? spanText : typeName;
            Grid.SetColumnSpan(type, spanned ? 3 : 1);
        }
        size.Text = spanned ? (type is null ? spanText : string.Empty) : folderSize ?? (entry.IsContainer || entry.Size is not long bytes ? string.Empty : Format(bytes));
        Grid.SetColumnSpan(size, spanned && type is null ? 2 : 1);
        date.Text = spanned || entry.Modified is not { } modified ? string.Empty : EntryText.FriendlyDate(modified, row?.Now ?? DateTime.Now);
        var muted = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        if (type is not null) type.Foreground = muted;
        size.Foreground = date.Foreground = muted;
        if (root.FindName("Check") is TextBlock check)
        {
            var markable = row?.CanMark == true && Application.State.FileListState.IsSelectable(entry);
            check.Visibility = markable ? Visibility.Visible : Visibility.Collapsed;
            check.Text = selected ? Glyphs.Checked : Glyphs.Unchecked;
            check.Foreground = selected ? Theme.Selected : Theme.TextMuted;
        }
    }

    /// <summary>Linha voltou para a fila de reciclagem: cancela o ícone pendente.</summary>
    public static void Recycle(SelectorItem container, IconLoader icons)
    {
        if (container.ContentTemplateRoot is FrameworkElement root && root.FindName("IconImage") is Image image) icons.Cancel(image);
    }

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    private static class Glyphs
    {
        public const string Folder = "\uE8B7";
        public const string File = "\uE8A5";
        public const string Drive = "\uEDA2";
        public const string Usb = "\uE88E";
        public const string RecycleBin = "\uE74D";
        public const string Optical = "\uE958";
        public const string NetworkDrive = "\uE8CE";
        public const string Archive = "\uE7B8";
        public const string Warning = "\uE7BA";
        public const string Checked = "\uE73A";
        public const string Unchecked = "\uE739";
        public const string Cut = "\uE8C6";
        public const string Lock = "\uE72E";
    }

    /// <summary>
    /// Liga/desliga o anel de foco de uma linha já preenchida (sem refazer o conteúdo). O item focado mostra o nome
    /// inteiro (até três linhas); os demais ficam em uma linha com reticências.
    /// </summary>
    public static void SetFocused(SelectorItem container, bool focused)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root || root.FindName("Ring") is not Border ring) return;
        if (Equals(ring.Tag, "tile"))
        {
            // Cartão: mesmo foco dos cartões do início (borda ciano, fundo azul, halo, leve aumento) e seta em destaque.
            if (ring.Parent is Border { ScaleTransition: null } glow) glow.ScaleTransition = new Vector3Transition { Duration = Theme.MotionFocus };
            Theme.ApplyCardFocus(ring, focused);
            if (ring.FindName("Chevron") is TextBlock chevron) chevron.Foreground = focused ? Theme.Accent : Theme.TextMuted;
            return;
        }
        Theme.ApplyFocus(ring, focused);
        if (ring.FindName("Chevron") is TextBlock rowChevron) rowChevron.Foreground = focused ? Theme.Accent : Theme.TextMuted;
        if (ring.FindName("Title") is TextBlock title)
        {
            title.TextWrapping = focused ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap;
            title.MaxLines = focused ? 3 : 1;
        }
    }

    /// <summary>Símbolo por tipo de unidade (o pendrive nunca se parece com o disco do sistema, mesmo antes do ícone do Shell).</summary>
    private static string DriveGlyph(DriveKind? kind) => kind switch
    {
        DriveKind.Removable => Glyphs.Usb,
        DriveKind.Optical => Glyphs.Optical,
        DriveKind.Network => Glyphs.NetworkDrive,
        _ => Glyphs.Drive,
    };

    private static bool IsArchiveName(string name) =>
        new[] { ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Flags(FileEntry entry)
    {
        if (entry.IsSystem) yield return "sistema";
        if (entry.IsHidden) yield return "oculto";
        if (entry.IsReparsePoint) yield return "link";
    }

    private static string TypeName(FileEntry entry) => EntryText.TypeName(entry);

    private static string Format(long bytes) => EntryText.Size(bytes);
}
