using ControlFS.App.Resources;
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

    private static string IconCell(int size, int glyph) =>
        $"<Grid Width=\"{size}\" Height=\"{size}\" VerticalAlignment=\"Center\">" +
        $"<TextBlock x:Name=\"Icon\" {IconFont} FontSize=\"{glyph}\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
        $"<Image x:Name=\"IconImage\" Width=\"{size}\" Height=\"{size}\" Stretch=\"Uniform\" Visibility=\"Collapsed\"/>" +
        "</Grid>";

    private static string StateCell(int column, int glyph, int text) =>
        $"<StackPanel Grid.Column=\"{column}\" Orientation=\"Horizontal\" Spacing=\"6\" VerticalAlignment=\"Center\">" +
        $"<TextBlock x:Name=\"StateGlyph\" {IconFont} FontSize=\"{glyph}\" VerticalAlignment=\"Center\"/>" +
        $"<TextBlock x:Name=\"StateText\" FontSize=\"{text}\" FontWeight=\"SemiBold\" VerticalAlignment=\"Center\"/>" +
        "</StackPanel>";

    private static string Frame(string body) =>
        $"<DataTemplate {Ns}><Border x:Name=\"Ring\" BorderThickness=\"3\" CornerRadius=\"6\"><Grid>" +
        "<Border x:Name=\"MarkBar\" Width=\"6\" HorizontalAlignment=\"Left\" CornerRadius=\"3\" Margin=\"2,6,0,6\"/>" +
        body + "</Grid></Border></DataTemplate>";

    private static readonly string ComfortableXaml = Frame(
        "<Grid x:Name=\"Body\" Padding=\"16,8,12,8\" ColumnSpacing=\"12\">" +
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"36\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
        IconCell(32, 26) +
        "<StackPanel Grid.Column=\"1\" VerticalAlignment=\"Center\">" +
        "<TextBlock x:Name=\"Title\" FontSize=\"20\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\"/>" +
        "<TextBlock x:Name=\"Detail\" FontSize=\"14\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\"/>" +
        "</StackPanel>" +
        StateCell(2, 20, 16) +
        "</Grid>");

    private static readonly string CompactXaml = Frame(
        "<Grid x:Name=\"Body\" Padding=\"14,2,12,2\" ColumnSpacing=\"12\">" +
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"26\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"160\"/>" +
        "<ColumnDefinition Width=\"96\"/><ColumnDefinition Width=\"150\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
        IconCell(24, 20) +
        "<TextBlock x:Name=\"Title\" Grid.Column=\"1\" FontSize=\"17\" TextTrimming=\"CharacterEllipsis\" MaxLines=\"1\" VerticalAlignment=\"Center\"/>" +
        "<TextBlock x:Name=\"TypeColumn\" Grid.Column=\"2\" FontSize=\"14\" TextTrimming=\"CharacterEllipsis\" VerticalAlignment=\"Center\"/>" +
        "<TextBlock x:Name=\"SizeColumn\" Grid.Column=\"3\" FontSize=\"14\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\"/>" +
        "<TextBlock x:Name=\"DateColumn\" Grid.Column=\"4\" FontSize=\"14\" VerticalAlignment=\"Center\"/>" +
        StateCell(5, 16, 14) +
        "</Grid>");

    public static DataTemplate Create(ListDensity density) =>
        (DataTemplate)XamlReader.Load(density == ListDensity.Compact ? CompactXaml : ComfortableXaml);

    public static void Fill(SelectorItem container, FileEntry entry, bool focused, bool selected, bool cut, IconLoader icons, IReadOnlySet<string>? specialFolders)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root) return;
        var icon = (TextBlock)root.FindName("Icon");
        var title = (TextBlock)root.FindName("Title");

        // Símbolo de reserva (fonte de ícones do Windows, nunca emoji) até o ícone do Shell chegar.
        icon.Text = entry.IsBlocked ? Glyphs.Warning : entry.Kind switch
        {
            EntryKind.Drive => Glyphs.Drive,
            EntryKind.KnownFolder or EntryKind.Directory or EntryKind.ArchiveDirectory => Glyphs.Folder,
            _ when IsArchiveName(entry.Name) => Glyphs.Archive,
            _ => Glyphs.File,
        };
        icon.Foreground = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        icons.Load((Image)root.FindName("IconImage"), icon, IconRequest.For(entry, specialFolders));
        title.Text = entry.Name;
        title.Foreground = entry.IsBlocked ? Theme.Danger : entry.IsHidden ? Theme.TextMuted : Theme.Text;

        var muted = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        if (root.FindName("Detail") is TextBlock detail)
        {
            detail.Text = entry.IsBlocked ? "Bloqueado: " + entry.BlockedReason : string.Join(" · ", DetailParts(entry));
            detail.Foreground = muted;
        }
        if (root.FindName("TypeColumn") is TextBlock type)
        {
            var place = entry.Kind is EntryKind.Drive or EntryKind.KnownFolder;
            type.Text = entry.IsBlocked ? "Bloqueado: " + entry.BlockedReason
                : place ? entry.Detail ?? TypeName(entry)
                : string.Join(" · ", (entry.FoundIn is { } folder ? new[] { "em " + folder, TypeName(entry) } : [TypeName(entry)]).Concat(Flags(entry)));
            type.Foreground = muted;
            // Unidades e pastas especiais não têm tamanho/data: o espaço livre ocupa as três colunas.
            Grid.SetColumnSpan(type, place || entry.IsBlocked ? 3 : 1);
            var size = (TextBlock)root.FindName("SizeColumn");
            var date = (TextBlock)root.FindName("DateColumn");
            size.Text = place || entry.IsBlocked || entry.IsContainer || entry.Size is not long bytes ? string.Empty : Format(bytes);
            date.Text = place || entry.IsBlocked || entry.Modified is not { } modified ? string.Empty : modified.LocalDateTime.ToString("g");
            size.Foreground = date.Foreground = Theme.TextMuted;
        }

        var glyphs = new List<string>();
        var states = new List<string>();
        if (selected) { glyphs.Add(Glyphs.Checked); states.Add("Marcado"); }
        if (cut) { glyphs.Add(Glyphs.Cut); states.Add("Recortado"); }
        if (entry.IsEncrypted) { glyphs.Add(Glyphs.Lock); states.Add("Com senha"); }
        var stateGlyph = (TextBlock)root.FindName("StateGlyph");
        var stateText = (TextBlock)root.FindName("StateText");
        stateGlyph.Text = string.Join(" ", glyphs);
        stateText.Text = string.Join(" · ", states);
        stateGlyph.Foreground = stateText.Foreground = selected ? Theme.Selected : Theme.TextMuted;
        ((Border)root.FindName("MarkBar")).Background = selected ? Theme.Selected : Theme.Transparent;
        // Recortado: conteúdo esmaecido (e indicado em texto e símbolo); o anel de foco e a faixa de marcado continuam nítidos.
        ((UIElement)root.FindName("Body")).Opacity = cut ? 0.55 : 1.0;

        SetFocused(container, focused);
        var state = string.Concat(states.Select(s => ", " + s.ToLowerInvariant()));
        AutomationProperties.SetName(container, entry.IsBlocked ? $"{entry.Name}, bloqueado: {entry.BlockedReason}" : $"{entry.Name}{state}");
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
        public const string Archive = "\uE7B8";
        public const string Warning = "\uE7BA";
        public const string Checked = "\uE73A";
        public const string Cut = "\uE8C6";
        public const string Lock = "\uE72E";
    }

    /// <summary>
    /// Liga/desliga o anel de foco de uma linha já preenchida (sem refazer o conteúdo). O item focado mostra o nome
    /// inteiro (até três linhas); os demais ficam em uma linha com reticências.
    /// </summary>
    public static void SetFocused(SelectorItem container, bool focused)
    {
        if (container.ContentTemplateRoot is not Border ring) return;
        Theme.ApplyFocus(ring, focused);
        if (ring.FindName("Title") is TextBlock title)
        {
            title.TextWrapping = focused ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap;
            title.MaxLines = focused ? 3 : 1;
        }
    }

    private static bool IsArchiveName(string name) =>
        new[] { ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ordem fixa em toda a lista: tipo · tamanho · data · atributos.</summary>
    private static IEnumerable<string> DetailParts(FileEntry entry)
    {
        if (entry.Detail is { Length: > 0 } d && entry.Kind is EntryKind.Drive or EntryKind.KnownFolder)
        {
            yield return d;
            yield break;
        }
        if (entry.FoundIn is { } folder) yield return "em " + folder;
        yield return TypeName(entry);
        if (entry.Size is long size && !entry.IsContainer) yield return Format(size);
        if (entry.Modified is { } m) yield return m.LocalDateTime.ToString("g");
        foreach (var flag in Flags(entry)) yield return flag;
    }

    private static IEnumerable<string> Flags(FileEntry entry)
    {
        if (entry.IsSystem) yield return "sistema";
        if (entry.IsHidden) yield return "oculto";
        if (entry.IsReparsePoint) yield return "link";
    }

    private static string TypeName(FileEntry entry) => entry.Kind switch
    {
        EntryKind.Drive => "Unidade",
        EntryKind.KnownFolder => "Pasta especial",
        EntryKind.Directory or EntryKind.ArchiveDirectory => "Pasta",
        _ => entry.Extension.Length > 1 ? "Arquivo " + entry.Extension[1..].ToUpperInvariant() : "Arquivo",
    };

    private static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
