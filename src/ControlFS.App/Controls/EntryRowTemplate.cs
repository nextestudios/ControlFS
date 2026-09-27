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
/// ContainerContentChanging — padrão recomendado para listas virtualizadas grandes.
/// </summary>
public static class EntryRowTemplate
{
    private const string Xaml =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
        "<Border x:Name=\"Ring\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" BorderThickness=\"3\" CornerRadius=\"6\">" +
        "<Grid Padding=\"12,8\" ColumnSpacing=\"12\">" +
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"36\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
        "<Grid Width=\"32\" Height=\"32\" VerticalAlignment=\"Center\">" +
        "<TextBlock x:Name=\"Icon\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontFamily=\"Segoe Fluent Icons, Segoe MDL2 Assets\" FontSize=\"26\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
        "<Image x:Name=\"IconImage\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Width=\"32\" Height=\"32\" Stretch=\"Uniform\" Visibility=\"Collapsed\"/>" +
        "</Grid>" +
        "<StackPanel Grid.Column=\"1\" VerticalAlignment=\"Center\">" +
        "<TextBlock x:Name=\"Title\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"20\" TextTrimming=\"CharacterEllipsis\"/>" +
        "<TextBlock x:Name=\"Detail\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"14\" TextTrimming=\"CharacterEllipsis\"/>" +
        "</StackPanel>" +
        "<TextBlock x:Name=\"Mark\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Grid.Column=\"2\" FontSize=\"18\" VerticalAlignment=\"Center\"/>" +
        "</Grid></Border></DataTemplate>";

    public static DataTemplate Create() => (DataTemplate)XamlReader.Load(Xaml);

    public static void Fill(SelectorItem container, FileEntry entry, bool focused, bool selected, bool cut, IconLoader icons, IReadOnlySet<string>? specialFolders)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root) return;
        SetFocused(container, focused);
        var icon = (TextBlock)root.FindName("Icon");
        var title = (TextBlock)root.FindName("Title");
        var detail = (TextBlock)root.FindName("Detail");
        var mark = (TextBlock)root.FindName("Mark");

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
        detail.Text = entry.IsBlocked ? "Bloqueado: " + entry.BlockedReason : Describe(entry);
        detail.Foreground = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        mark.Text = selected ? "✔ marcado" : cut ? "✂ recortado" : entry.IsEncrypted ? "com senha" : string.Empty;
        // Recortado: conteúdo esmaecido (e indicado em texto, não só visualmente); o anel de foco continua nítido.
        if (root is Border { Child: UIElement content }) content.Opacity = cut ? 0.5 : 1.0;
        mark.Foreground = selected ? Theme.Selected : Theme.TextMuted;
        var state = (selected ? ", marcado" : string.Empty) + (cut ? ", recortado" : string.Empty);
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
    }

    /// <summary>Liga/desliga o anel de foco de uma linha já preenchida (sem refazer o conteúdo).</summary>
    public static void SetFocused(SelectorItem container, bool focused)
    {
        if (container.ContentTemplateRoot is Border ring) Theme.ApplyFocus(ring, focused);
    }

    private static bool IsArchiveName(string name) =>
        new[] { ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static string Describe(FileEntry entry)
    {
        if (entry.Detail is { Length: > 0 } d && entry.Kind is EntryKind.Drive or EntryKind.KnownFolder) return d;
        var parts = new List<string>();
        if (entry.Size is long size && !entry.IsContainer) parts.Add(Format(size));
        if (entry.Modified is { } m) parts.Add(m.LocalDateTime.ToString("g"));
        if (entry.IsSystem) parts.Add("sistema");
        if (entry.IsHidden) parts.Add("oculto");
        if (entry.IsReparsePoint) parts.Add("link");
        return string.Join(" · ", parts);
    }

    private static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}
