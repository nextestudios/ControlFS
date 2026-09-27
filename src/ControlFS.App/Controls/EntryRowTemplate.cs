using ControlFS.App.Resources;
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
        "<Grid Padding=\"12,8\" ColumnSpacing=\"12\">" +
        "<Grid.ColumnDefinitions><ColumnDefinition Width=\"36\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>" +
        "<TextBlock x:Name=\"Icon\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"22\" VerticalAlignment=\"Center\"/>" +
        "<StackPanel Grid.Column=\"1\" VerticalAlignment=\"Center\">" +
        "<TextBlock x:Name=\"Title\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"20\" TextTrimming=\"CharacterEllipsis\"/>" +
        "<TextBlock x:Name=\"Detail\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FontSize=\"14\" TextTrimming=\"CharacterEllipsis\"/>" +
        "</StackPanel>" +
        "<TextBlock x:Name=\"Mark\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Grid.Column=\"2\" FontSize=\"18\" VerticalAlignment=\"Center\"/>" +
        "</Grid></DataTemplate>";

    public static DataTemplate Create() => (DataTemplate)XamlReader.Load(Xaml);

    public static void Fill(SelectorItem container, FileEntry entry, bool selected, bool cut = false)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root) return;
        var icon = (TextBlock)root.FindName("Icon");
        var title = (TextBlock)root.FindName("Title");
        var detail = (TextBlock)root.FindName("Detail");
        var mark = (TextBlock)root.FindName("Mark");

        icon.Text = entry.IsBlocked ? "⚠" : entry.Kind switch
        {
            EntryKind.Drive => "🖴",
            EntryKind.KnownFolder or EntryKind.Directory or EntryKind.ArchiveDirectory => "📁",
            _ when IsArchiveName(entry.Name) => "📦",
            _ => "📄",
        };
        title.Text = entry.Name;
        title.Foreground = entry.IsBlocked ? Theme.Danger : entry.IsHidden ? Theme.TextMuted : Theme.Text;
        detail.Text = entry.IsBlocked ? "Bloqueado: " + entry.BlockedReason : Describe(entry);
        detail.Foreground = entry.IsBlocked ? Theme.Danger : Theme.TextMuted;
        mark.Text = selected ? "✔ marcado" : cut ? "✂ recortado" : entry.IsEncrypted ? "🔒" : string.Empty;
        root.Opacity = cut ? 0.5 : 1.0; // recortado: esmaecido (e indicado em texto, não só visualmente)
        mark.Foreground = selected ? Theme.Selected : Theme.TextMuted;
        var state = (selected ? ", marcado" : string.Empty) + (cut ? ", recortado" : string.Empty);
        AutomationProperties.SetName(container, entry.IsBlocked ? $"{entry.Name}, bloqueado: {entry.BlockedReason}" : $"{entry.Name}{state}");
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
