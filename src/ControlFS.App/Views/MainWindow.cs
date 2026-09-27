using ControlFS.App.Controls;
using ControlFS.App.Navigation;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Updates;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Shell;
using ControlFS.Infrastructure.Windows.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ControlFS.App.Views;

/// <summary>
/// Janela única: cabeçalho (local + estado), lista virtualizada, rodapé de comandos contextuais
/// e camada modal. Toda interação vira InputAction no AppController.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "InputHost e o serviço de atualização são descartados no evento Closed da janela.")]
public sealed class MainWindow : Window
{
    private readonly AppController _app;
    private readonly InputHost _input;
    private readonly GitHubReleaseUpdateService _updates;
    private readonly ShellIconProvider _iconProvider = new();
    private readonly IconLoader _icons;
    private IReadOnlyList<FileEntry>? _shownPlaces;
    private HashSet<string> _specialFolders = new(StringComparer.OrdinalIgnoreCase);
    private ListDensity _density = ListDensity.Comfortable;
    private readonly ContentControl _root = new() { IsTabStop = true, UseSystemFocusVisuals = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _location = new() { FontSize = Theme.FontTitle, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceXs, Margin = new Thickness(-Theme.SpaceS, Theme.SpaceXs, 0, 0) };
    private readonly TextBlock _badge = new() { FontSize = Theme.FontCaption, Foreground = Theme.Accent };
    private readonly Image _logo = new() { Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _device = new() { FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _operation = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _empty = new() { FontSize = Theme.FontBody, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListView _list = new();
    private readonly StackPanel _hints = new() { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceL };
    private readonly TextBlock _status = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _overlay = new();
    private IReadOnlyList<FileEntry>? _shownItems;
    private HashSet<string> _shownSelection = [];
    private int _shownFocus = -1;
    private object? _shownClipboard;
    private bool _fullScreen;

    public MainWindow()
    {
        Title = "ControlFS";
        var icon = Path.Join(AppContext.BaseDirectory, "controlfs.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        var settingsStore = new JsonSettingsStore(AppPaths.DataDirectory);
        _updates = GitHubReleaseUpdateService.CreateDefault(AppPaths.IsInstalled, Path.Join(AppPaths.DataDirectory, "updates"));
        // Temporários (staging, cópias parciais) registrados para limpeza na próxima inicialização se o app cair no meio.
        var temporaries = new TemporaryJournal(Path.Join(AppPaths.DataDirectory, "operations"));
        _app = new AppController(new LocalFileSystemProvider(), new ArchiveService(temporaries), settingsStore, _updates, new WindowsShellService(),
            new FileOperationService(temporaries), new JsonControllerProfileStore(AppPaths.DataDirectory), temporaries);
        _input = new InputHost(_app, DispatcherQueue);
        _icons = new IconLoader(_iconProvider);
        _icons.Invalidated += () =>
        {
            _shownItems = null; // força recriar as linhas com ícones no novo tamanho
            Render();
        };

        AppLog.Info("MainWindow: serviços criados; montando layout");
        Content = _root;
        _root.Content = BuildLayout();
        AppLog.Info("MainWindow: layout montado");
        _root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F11) { ToggleFullScreen(); e.Handled = true; return; }
            _input.OnKeyDown(e);
        };
        _root.CharacterReceived += (_, e) => _input.OnCharacter(e.Character);
        _root.Loaded += (_, _) =>
        {
            _root.Focus(FocusState.Programmatic);
            _icons.SetScale(_root.XamlRoot.RasterizationScale);
            _root.XamlRoot.Changed += (root, _) => _icons.SetScale(root.RasterizationScale);
        };

        Activated += (_, e) =>
        {
            var active = e.WindowActivationState != WindowActivationState.Deactivated;
            _input.OnWindowActivated(active);
            if (active) _root.Focus(FocusState.Programmatic);
        };
        Closed += (_, _) =>
        {
            _app.PrepareShutdown(); // instala em silêncio uma atualização verificada, se o usuário deixou ligado
            _input.Dispose();
            _updates.Dispose();
            _iconProvider.Dispose();
        };

        _app.Changed += Render;
        _app.ExitRequested += Close;
        _app.CopyText = CopyToClipboard;
        _input.StatusChanged += Render;
        _app.SettingsChanged += settings =>
        {
            if (settings.Density == _density) return;
            _density = settings.Density;
            _list.ItemTemplate = EntryRowTemplate.Create(_density);
            _shownItems = null; // recria as linhas no novo modelo
        };
        _app.Start();
        if (AppPaths.Notice is { } notice) _app.ShowNotice(notice);
        AppLog.Info($"MainWindow: controlador iniciado; entrada: {(_input.BackendReady ? _input.BackendDescription : "SDL indisponível: " + _input.BackendError)}");
    }

    private Grid BuildLayout()
    {
        var layout = new Grid { Background = Theme.Background };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Cabeçalho
        var header = new Grid { Padding = new Thickness(Theme.SpaceL, Theme.SpaceM, Theme.SpaceL, Theme.SpaceS), ColumnSpacing = Theme.SpaceM };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        _logo.Source = Branding.Logo;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_logo, "ControlFS");
        titleStack.Children.Add(_logo);
        titleStack.Children.Add(_badge);
        titleStack.Children.Add(_location);
        titleStack.Children.Add(_crumbs);
        header.Children.Add(titleStack);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_device);
        right.Children.Add(_operation);
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        layout.Children.Add(header);

        // Lista
        _list.ItemTemplate = EntryRowTemplate.Create(_density);
        _list.SelectionMode = ListViewSelectionMode.None; // o foco é desenhado pelo anel da linha (mesmo token dos menus)
        _list.IsItemClickEnabled = true;
        _list.IsTabStop = false;
        _list.AllowFocusOnInteraction = false;
        _list.ItemContainerTransitions = new TransitionCollection(); // sem animações de lista
        _list.Padding = new Thickness(Theme.SpaceM, 0, Theme.SpaceM, 0);
        _list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue)
            {
                EntryRowTemplate.Recycle(args.ItemContainer, _icons);
                return;
            }
            if (args.Item is not FileEntry entry) return;
            EntryRowTemplate.Fill(args.ItemContainer, entry, args.ItemIndex == _shownFocus, _shownSelection.Contains(entry.Id), _app.IsCut(entry), _icons, _specialFolders);
        };
        _list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FileEntry entry && _shownItems is { } items) _app.PointerActivateListItem(IndexOf(items, entry));
        };
        var content = new Grid();
        content.Children.Add(_list);
        content.Children.Add(_empty);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);

        // Rodapé
        var footer = new StackPanel { Padding = new Thickness(Theme.SpaceL, Theme.SpaceS, Theme.SpaceL, Theme.SpaceM), Spacing = Theme.SpaceXs, Background = Theme.Surface };
        footer.Children.Add(_status);
        footer.Children.Add(new ScrollViewer { Content = _hints, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        Grid.SetRowSpan(_overlay, 3);
        layout.Children.Add(_overlay);
        return layout;
    }

    private void Render()
    {
        // Cabeçalho
        var pane = _app.ActivePane;
        switch (_app.Screen)
        {
            case Screen.Home:
                _badge.Text = "INÍCIO · LOCAIS";
                _location.Text = string.Empty;
                break;
            case Screen.FolderPicker:
                _badge.Text = "ESCOLHER PASTA · " + _app.PickerTitle;
                _location.Text = pane.Location?.DisplayPath ?? "…";
                break;
            default:
                _badge.Text = pane.Location switch
                {
                    ArchiveLocation => "COMPACTADO · SOMENTE LEITURA",
                    SearchLocation => "BUSCA",
                    _ => "PASTA NO DISCO",
                };
                _location.Text = pane.Location?.DisplayPath ?? "…";
                break;
        }
        // Logo só na tela inicial (as demais telas usam o espaço para o caminho).
        _logo.Visibility = _app.Screen == Screen.Home && _logo.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        var crumbs = _app.Breadcrumbs;
        _location.Visibility = _app.Screen == Screen.Home || crumbs.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        RenderBreadcrumbs(crumbs, pane.Region == PaneRegion.Breadcrumbs && _app.Screen != Screen.Home ? pane.BreadcrumbFocus : -1);
        var device = _input.ActiveDevice;
        _device.Text = !_input.BackendReady
            ? $"Controles indisponíveis ({_input.BackendError}) · use teclado/mouse"
            : device is not null
                ? $"🎮 {device.Name}" + (_input.Devices.Count > 1 ? $" (+{_input.Devices.Count - 1})" : string.Empty)
                : _input.Devices.Count > 0 ? $"🎮 {_input.Devices.Count} controle(s) — pressione um botão para ativar" : "Nenhum controle — teclado disponível";
        var op = _app.Operations.Current;
        _operation.Text = op is null
            ? _app.UpdateState switch
            {
                UpdateState.Ready => $"⬆ Atualização {_app.ReadyUpdate!.Manifest.Version} pronta (Menu → Atualizações)",
                UpdateState.Downloading => "⬆ Baixando atualização…",
                UpdateState.AvailableManual => $"⬆ Nova versão {_app.AvailableUpdate!.Version} disponível",
                _ => _app.Clipboard is { } clip ? $"📋 {clip.Paths.Count} item(ns) {(clip.IsCut ? "recortado(s)" : "copiado(s)")} — Ações → Colar" : string.Empty,
            }
            : $"{op.Title} — {(op.Progress is { } p ? $"{p.ItemsProcessed}/{p.ItemsTotal?.ToString() ?? "?"}" : "…")} ({(op.State == OperationState.WaitingForUser ? "aguardando você" : "em andamento")})";

        // Lista (a identidade dos itens decide se o ItemsSource muda)
        if (!ReferenceEquals(_app.Places, _shownPlaces))
        {
            // Pastas especiais (Downloads, Documentos…) mostram o ícone próprio também dentro das pastas do disco.
            _shownPlaces = _app.Places;
            _specialFolders = new HashSet<string>(_app.Places.Where(p => p.Kind == EntryKind.KnownFolder && p.FullPath is not null).Select(p => p.FullPath!), StringComparer.OrdinalIgnoreCase);
        }
        IReadOnlyList<FileEntry> items = _app.Screen == Screen.Home ? _app.Places : pane.List.Items;
        var focus = _app.Screen == Screen.Home ? _app.PlacesFocus : pane.List.FocusIndex;
        if (_app.Screen != Screen.Home && pane.Region == PaneRegion.Breadcrumbs) focus = -1; // um só foco visível: o da barra de caminho
        var selection = _app.Screen == Screen.Home ? new HashSet<string>() : pane.List.SelectedIds.ToHashSet();
        if (focus >= items.Count) focus = -1;
        var sourceChanged = !ReferenceEquals(items, _shownItems);
        if (sourceChanged || !selection.SetEquals(_shownSelection) || !ReferenceEquals(_app.Clipboard, _shownClipboard))
        {
            _shownClipboard = _app.Clipboard;
            _shownItems = items;
            _shownSelection = selection;
            _shownFocus = focus;
            _list.ItemsSource = items.ToList();
            // Pasta nova: mede a lista antes de rolar, para o item focado já aparecer no primeiro quadro.
            if (sourceChanged && focus >= 0) _list.UpdateLayout();
            if (focus >= 0) _list.ScrollIntoView(items[focus]);
        }
        else if (focus != _shownFocus)
        {
            if (_shownFocus >= 0 && _list.ContainerFromIndex(_shownFocus) is ListViewItem previous) EntryRowTemplate.SetFocused(previous, false);
            _shownFocus = focus;
            if (focus >= 0)
            {
                _list.ScrollIntoView(items[focus]);
                if (_list.ContainerFromIndex(focus) is ListViewItem current) EntryRowTemplate.SetFocused(current, true);
            }
        }
        var search = _app.Screen == Screen.Home ? null : pane.ActiveSearch;
        _empty.Text = pane.IsLoading && _app.Screen != Screen.Home ? "Carregando…"
            : items.Count > 0 ? string.Empty
            : search is null ? "Pasta vazia"
            : search.IsRunning ? "Buscando…" : "Nenhum resultado";
        if (search is not null && _app.StatusMessage is null)
            _status.Text = search.Summary; // parcial, concluída ou cancelada, e as pastas puladas
        else if (_app.Screen != Screen.Home && pane.InaccessibleCount > 0 && _app.StatusMessage is null)
            _status.Text = $"{pane.InaccessibleCount} item(ns) sem permissão de leitura foram omitidos.";
        else
            _status.Text = _app.StatusMessage ?? string.Empty;

        // Rodapé: somente ações válidas no contexto, com a legenda do dispositivo em uso (glifo do controle ou tecla)
        _hints.Children.Clear();
        foreach (var prompt in _app.Prompts)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            if (prompt is { Button: { } button, Family: { } family })
                chip.Children.Add(ControllerGlyphs.Create(button, family, Theme.FontCaption * 1.6));
            else
                chip.Children.Add(new Border
                {
                    Background = Theme.SurfaceRaised,
                    BorderBrush = Theme.Border,
                    BorderThickness = Theme.Hairline,
                    CornerRadius = Theme.Radius,
                    Padding = new Thickness(Theme.SpaceS, 2, Theme.SpaceS, 2),
                    Child = new TextBlock { Text = prompt.Key, FontSize = Theme.FontCaption, Foreground = Theme.Text, FontWeight = FontWeights.SemiBold },
                });
            chip.Children.Add(new TextBlock { Text = prompt.Label, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, prompt.AccessibilityText);
            _hints.Children.Add(chip);
        }

        // Camada modal
        _overlay.Children.Clear();
        if (ModalView.Build(_app) is { } modal) _overlay.Children.Add(modal);
        RestoreKeyboardFocus();
    }

    /// <summary>
    /// Ao fechar um modal, o elemento que tinha o foco do XAML pode sair da árvore e o teclado ficaria sem destino.
    /// A raiz volta a receber o foco (o foco lógico continua no AppController).
    /// </summary>
    private void RestoreKeyboardFocus()
    {
        if (_root.XamlRoot is not { } xamlRoot) return;
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) is not DependencyObject focused || !IsInsideRoot(focused))
            _root.Focus(FocusState.Programmatic);
    }

    private bool IsInsideRoot(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, _root)) return true;
        return false;
    }

    /// <summary>
    /// Barra de caminho: segmentos focáveis (LB/RB entram e saem, esquerda/direita escolhem). A fronteira do compactado
    /// usa "▸" e o segmento do compactado leva o símbolo de pacote; segmentos do meio de caminhos longos viram "…".
    /// </summary>
    private void RenderBreadcrumbs(IReadOnlyList<Breadcrumb> crumbs, int focus)
    {
        _crumbs.Children.Clear();
        _crumbs.Visibility = crumbs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < crumbs.Count; i++)
        {
            var crumb = crumbs[i];
            if (i > 0)
            {
                var boundary = crumb.Kind == BreadcrumbKind.Archive;
                _crumbs.Children.Add(new TextBlock
                {
                    Text = boundary ? "▸" : "›",
                    FontSize = Theme.FontItem,
                    Foreground = boundary ? Theme.Accent : Theme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceXs };
            if (crumb.Kind == BreadcrumbKind.Archive)
                content.Children.Add(new TextBlock { Text = "\uE7B8", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = Theme.FontBody, Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock
            {
                Text = crumb.Label,
                FontSize = Theme.FontItem,
                FontWeight = crumb.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = crumb.IsCurrent ? Theme.Text : crumb.Kind is BreadcrumbKind.Archive or BreadcrumbKind.ArchiveFolder ? Theme.Accent : Theme.TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 260,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var chip = new Border { Child = content, CornerRadius = Theme.Radius, Padding = new Thickness(Theme.SpaceS, 2, Theme.SpaceS, 2) };
            Theme.ApplyFocus(chip, i == focus);
            var index = i;
            chip.Tapped += (_, _) => _app.PointerActivateBreadcrumb(index);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, crumb.Kind switch
            {
                BreadcrumbKind.Collapsed => $"{crumb.Hidden.Count} pastas recolhidas",
                BreadcrumbKind.Archive => $"{crumb.Label}, compactado",
                _ => crumb.Label,
            } + (crumb.IsCurrent ? ", pasta atual" : string.Empty));
            _crumbs.Children.Add(chip);
        }
    }

    /// <summary>Texto simples para a área de transferência do Windows (relatório do teste de controles).</summary>
    private static bool CopyToClipboard(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush(); // continua disponível depois de fechar o app
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            AppLog.Info($"Área de transferência indisponível: {ex.Message}");
            return false;
        }
    }

    private void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        AppWindow.SetPresenter(_fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
    }

    private static int IndexOf(IReadOnlyList<FileEntry> items, FileEntry entry)
    {
        for (var i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], entry) || items[i].Id == entry.Id) return i;
        return -1;
    }
}
