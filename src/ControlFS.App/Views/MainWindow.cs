using ControlFS.App.Controls;
using ControlFS.App.Navigation;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Layout;
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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ControlFS.App.Views;

/// <summary>
/// Janela única: cabeçalho (local + estado), lista virtualizada, rodapé de comandos contextuais
/// e camada modal. Toda interação vira InputAction no AppController.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "InputHost, o serviço de atualização, os ícones e o monitor de unidades são descartados no evento Closed da janela.")]
public sealed class MainWindow : Window
{
    private readonly AppController _app;
    private readonly InputHost _input;
    private readonly GitHubReleaseUpdateService? _updates;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private bool _layoutPinned;
    private readonly ShellIconProvider _iconProvider = new();
    private readonly DriveWatcher _drives = new();
    private readonly IconLoader _icons;
    private readonly IconLoader _tileIcons;
    private IReadOnlyList<FileEntry>? _shownPlaces;
    private HashSet<string> _specialFolders = new(StringComparer.OrdinalIgnoreCase);
    private ListDensity _density = ListDensity.Comfortable;
    private ViewMode _view = ViewMode.List;
    private readonly ContentControl _root = new() { IsTabStop = true, UseSystemFocusVisuals = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _location = new() { FontSize = Theme.FontTitle, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceXs, Margin = new Thickness(-Theme.SpaceS, Theme.SpaceXs, 0, 0) };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceXs, Margin = new Thickness(-Theme.SpaceS, 0, 0, Theme.SpaceXs) };
    private readonly TextBlock _badge = new() { FontSize = Theme.FontCaption, Foreground = Theme.Accent };
    private readonly Image _logo = new() { Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _device = new() { FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock _operation = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _empty = new() { FontSize = Theme.FontBody, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListView _list = new();
    private readonly GridView _grid = new();
    private readonly WrapPanel _hints = new();
    private readonly StackPanel _footer = new() { Background = Theme.Surface };
    private readonly Grid _header = new();
    private readonly StackPanel _headerRight = new() { VerticalAlignment = VerticalAlignment.Center };
    private Grid? _layout;
    private readonly TextBlock _status = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _overlay = new();
    private IReadOnlyList<FileEntry>? _shownItems;
    private HashSet<string> _shownSelection = [];
    private int _shownFocus = -1;
    private object? _shownClipboard;
    private bool _fullScreen;

    public MainWindow()
        : this(dataDirectory: null)
    {
    }

    /// <summary>
    /// <paramref name="dataDirectory"/> diferente de null: modo de capturas (--render-screens). Preferências numa pasta
    /// temporária, sem serviço de atualização (nenhum acesso à rede) e layout fixado pelo gerador de capturas.
    /// </summary>
    internal MainWindow(string? dataDirectory)
    {
        Title = "ControlFS";
        var icon = Path.Join(AppContext.BaseDirectory, "controlfs.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        var data = dataDirectory ?? AppPaths.DataDirectory;
        var settingsStore = new JsonSettingsStore(data);
        _updates = dataDirectory is null ? GitHubReleaseUpdateService.CreateDefault(AppPaths.IsInstalled, Path.Join(data, "updates")) : null;
        // Temporários (staging, cópias parciais) registrados para limpeza na próxima inicialização se o app cair no meio.
        var temporaries = new TemporaryJournal(Path.Join(data, "operations"));
        _app = new AppController(new LocalFileSystemProvider(), new ArchiveService(temporaries), settingsStore, _updates, new WindowsShellService(),
            new FileOperationService(temporaries), new JsonControllerProfileStore(data), temporaries, new JsonOperationHistoryStore(data));
        _input = new InputHost(_app, DispatcherQueue);
        _icons = new IconLoader(_iconProvider);
        _tileIcons = new IconLoader(_iconProvider, IconLoader.TileIconSize);
        _icons.Invalidated += OnIconsInvalidated;
        _tileIcons.Invalidated += OnIconsInvalidated;

        AppLog.Info("MainWindow: serviços criados; montando layout");
        Content = _root;
        _root.Content = _layout = BuildLayout();
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
            UpdateLayoutProfile();
            _root.XamlRoot.Changed += (_, _) => UpdateLayoutProfile(); // tamanho, monitor ou DPI
        };
        // Tamanho do texto do Windows (Acessibilidade): o WinUI aumenta cada texto; o layout decide o que cabe.
        _uiSettings.TextScaleFactorChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLayoutProfile);

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
            _updates?.Dispose();
            _iconProvider.Dispose();
            _drives.Dispose();
        };

        _app.Changed += Render;
        _app.ExitRequested += Close;
        _app.CopyText = CopyToClipboard;
        _input.StatusChanged += Render;
        _app.SettingsChanged += settings =>
        {
            if (settings.Density == _density && settings.View == _view) return;
            _density = settings.Density;
            _view = settings.View;
            _list.ItemTemplate = EntryRowTemplate.Create(_density);
            _grid.ItemTemplate = EntryRowTemplate.CreateTile(_density);
            ShowActiveView();
        };
        _app.Start();
        // Pendrive conectado ou removido com o app aberto: os locais se atualizam sem reiniciar.
        _drives.Changed += () => DispatcherQueue.TryEnqueue(_app.RefreshDrives);
        ApplyLayout();
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
        var header = _header;
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        _logo.Source = Branding.Logo;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_logo, "ControlFS");
        titleStack.Children.Add(_logo);
        titleStack.Children.Add(_tabStrip);
        titleStack.Children.Add(_badge);
        titleStack.Children.Add(_location);
        titleStack.Children.Add(_crumbs);
        header.Children.Add(titleStack);
        var right = _headerRight;
        right.Children.Add(_device);
        right.Children.Add(_operation);
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        layout.Children.Add(header);

        // Lista e grade: as duas virtualizadas, preenchidas pelo mesmo código; só a ativa fica visível e com itens.
        _list.ItemTemplate = EntryRowTemplate.Create(_density);
        _grid.ItemTemplate = EntryRowTemplate.CreateTile(_density);
        _grid.Visibility = Visibility.Collapsed;
        ConfigureItems(_list, _icons);
        ConfigureItems(_grid, _tileIcons);
        _grid.SizeChanged += (_, _) => UpdateGridMetrics();
        var content = new Grid();
        content.Children.Add(_list);
        content.Children.Add(_grid);
        content.Children.Add(_empty);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);

        // Rodapé
        // Legendas quebram linha em vez de rolar para o lado: em 1280×720 todas continuam visíveis.
        var footer = _footer;
        footer.Children.Add(_status);
        footer.Children.Add(_hints);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        Grid.SetRowSpan(_overlay, 3);
        layout.Children.Add(_overlay);
        return layout;
    }

    private void ConfigureItems(ListViewBase view, IconLoader icons)
    {
        view.SelectionMode = ListViewSelectionMode.None; // o foco é desenhado pelo anel da linha (mesmo token dos menus)
        view.IsItemClickEnabled = true;
        view.IsTabStop = false;
        view.AllowFocusOnInteraction = false;
        view.ItemContainerTransitions = new TransitionCollection(); // sem animações de lista
        view.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue)
            {
                EntryRowTemplate.Recycle(args.ItemContainer, icons);
                return;
            }
            if (args.Item is not FileEntry entry) return;
            args.ItemContainer.HorizontalContentAlignment = HorizontalAlignment.Stretch; // o anel ocupa o bloco/linha inteiro
            args.ItemContainer.VerticalContentAlignment = VerticalAlignment.Stretch;
            EntryRowTemplate.Fill(args.ItemContainer, entry, args.ItemIndex == _shownFocus, _shownSelection.Contains(entry.Id), _app.IsCut(entry), icons, _specialFolders);
        };
        view.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FileEntry entry && _shownItems is { } items) _app.PointerActivateListItem(IndexOf(items, entry));
        };
    }

    private ListViewBase ActiveList => _view == ViewMode.Grid ? _grid : _list;

    private void OnIconsInvalidated()
    {
        _shownItems = null; // força recriar as linhas com ícones no novo tamanho
        Render();
    }

    /// <summary>Lista ↔ grade: a outra fica vazia e escondida; o item focado continua o mesmo (foco pela identidade).</summary>
    private void ShowActiveView()
    {
        var inactive = _view == ViewMode.Grid ? (ListViewBase)_list : _grid;
        inactive.ItemsSource = null;
        inactive.Visibility = Visibility.Collapsed;
        ActiveList.Visibility = Visibility.Visible;
        _shownItems = null; // recria os itens no novo modelo
        UpdateGridMetrics();
    }

    /// <summary>
    /// Blocos de tamanho fixo e número de colunas fixado pela largura: o AppController navega em 2D com as mesmas
    /// colunas que aparecem na tela, e pagina pelas linhas visíveis.
    /// </summary>
    private void UpdateGridMetrics()
    {
        var (width, height, _) = EntryRowTemplate.TileSize(_density);
        var available = _grid.ActualWidth - _grid.Padding.Left - _grid.Padding.Right;
        var columns = available > 0 ? Math.Max(1, (int)Math.Floor(available / width)) : 1;
        var rows = _grid.ActualHeight > 0 ? Math.Max(1, (int)Math.Floor(_grid.ActualHeight / height)) : 1;
        if (_grid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemWidth = width;
            panel.ItemHeight = height;
            panel.MaximumRowsOrColumns = columns;
        }
        _app.SetGridLayout(columns, rows);
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
        RenderTabs(_app.Screen == Screen.Browser, _app.Screen == Screen.Browser && pane.Region == PaneRegion.Tabs);
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
        if (_app.Screen != Screen.Home && pane.Region != PaneRegion.List) focus = -1; // um só foco visível: o da barra de caminho ou das abas
        var selection = _app.Screen == Screen.Home ? new HashSet<string>() : pane.List.SelectedIds.ToHashSet();
        if (focus >= items.Count) focus = -1;
        var sourceChanged = !ReferenceEquals(items, _shownItems);
        if (sourceChanged || !selection.SetEquals(_shownSelection) || !ReferenceEquals(_app.Clipboard, _shownClipboard))
        {
            _shownClipboard = _app.Clipboard;
            _shownItems = items;
            _shownSelection = selection;
            _shownFocus = focus;
            var view = ActiveList;
            view.ItemsSource = items.ToList();
            // Pasta nova: mede a lista antes de rolar, para o item focado já aparecer no primeiro quadro.
            if (sourceChanged && focus >= 0) view.UpdateLayout();
            if (_view == ViewMode.Grid) UpdateGridMetrics(); // o painel da grade só existe depois do primeiro layout
            if (focus >= 0) view.ScrollIntoView(items[focus]);
        }
        else if (focus != _shownFocus)
        {
            var view = ActiveList;
            if (_shownFocus >= 0 && view.ContainerFromIndex(_shownFocus) is SelectorItem previous) EntryRowTemplate.SetFocused(previous, false);
            _shownFocus = focus;
            if (focus >= 0)
            {
                view.ScrollIntoView(items[focus]);
                if (view.ContainerFromIndex(focus) is SelectorItem current) EntryRowTemplate.SetFocused(current, true);
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
                chip.Children.Add(ControllerGlyphs.Create(button, family, Math.Round(Theme.FontCaption * 1.6)));
            else
                chip.Children.Add(new Border
                {
                    Background = Theme.SurfaceRaised,
                    BorderBrush = Theme.Border,
                    BorderThickness = Theme.Hairline,
                    CornerRadius = Theme.Radius,
                    Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs / 2, Theme.SpaceS, Theme.SpaceXs / 2),
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
                MaxWidth = Theme.Scaled(260),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var chip = new Border { Child = content, CornerRadius = Theme.Radius, Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs / 2, Theme.SpaceS, Theme.SpaceXs / 2) };
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

    /// <summary>
    /// Faixa de abas do navegador: a ativa em destaque; com o foco na faixa (RB), a ativa recebe o anel de foco e LB/RB
    /// trocam de aba. Só aparece no navegador.
    /// </summary>
    private void RenderTabs(bool visible, bool focused)
    {
        _tabStrip.Children.Clear();
        _tabStrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var tabs = _app.Tabs;
        for (var i = 0; i < tabs.Count; i++)
        {
            var active = i == _app.ActiveTab;
            var title = AppController.TabTitle(tabs[i]);
            var chip = new Border
            {
                Child = new TextBlock
                {
                    Text = title,
                    FontSize = Theme.FontCaption,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = active ? Theme.Text : Theme.TextMuted,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = Theme.Scaled(200),
                },
                CornerRadius = Theme.Radius,
                Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs / 2, Theme.SpaceS, Theme.SpaceXs / 2),
            };
            Theme.ApplyFocus(chip, focused && active);
            if (active && !focused) chip.Background = Theme.SurfaceRaised; // ativa sem foco: destaque discreto
            var index = i;
            chip.Tapped += (_, _) => _app.PointerActivateTab(index);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, $"Aba {i + 1} de {tabs.Count}: {title}" + (active ? ", ativa" : string.Empty));
            _tabStrip.Children.Add(chip);
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

    /// <summary>
    /// Recalcula a faixa de layout (portátil, desktop, TV grande) pelo tamanho efetivo da janela, pelo DPI e pelo
    /// fator de texto do Windows. Só refaz as telas quando a faixa muda.
    /// </summary>
    private void UpdateLayoutProfile()
    {
        if (_layoutPinned || _root.XamlRoot is not { } xamlRoot) return;
        var size = xamlRoot.Size;
        var profile = LayoutBreakpoints.Select(size.Width, size.Height, xamlRoot.RasterizationScale, _uiSettings.TextScaleFactor);
        var previous = Theme.Viewport;
        if (Theme.SetLayout(profile, size) || EntryRowTemplate.IsNarrow(previous.Width) != EntryRowTemplate.IsNarrow(size.Width))
        {
            ApplyLayout();
        }
        else if (previous != size)
        {
            // Mesma faixa: só o que depende do tamanho exato (altura máxima de menus, largura do status).
            _headerRight.MaxWidth = Math.Max(240, size.Width * 0.4);
            Render();
        }
    }

    /// <summary>Gerador de capturas: fixa a faixa de layout de uma resolução/escala simulada.</summary>
    internal void PinLayout(LayoutProfile profile, Windows.Foundation.Size viewport, double simulatedTextScale)
    {
        _layoutPinned = true;
        Theme.SetLayout(profile, viewport, simulatedTextScale);
        ApplyLayout();
    }

    internal AppController Controller => _app;

    internal ContentControl RootHost => _root;

    internal Grid LayoutRoot => _layout!;

    internal IconLoader Icons => _icons;

    /// <summary>Gerador de capturas: quanto cada região ocupou e se o modal coube inteiro na área do app.</summary>
    internal string DescribeFit()
    {
        var viewport = Theme.Viewport;
        var fit = $"cabeçalho {_header.ActualHeight:0}, {(_view == ViewMode.Grid ? $"grade {_app.GridColumns}x{_app.GridRowsPerPage}" : "lista")} {ActiveList.ActualHeight:0}, rodapé {_footer.ActualHeight:0} de {viewport.Height:0} px efetivos";
        if (_overlay.Children.Count > 0 && _overlay.Children[0] is Panel { Children.Count: > 0 } scrim && scrim.Children[0] is FrameworkElement card)
        {
            var needed = card.ActualHeight + card.Margin.Top + card.Margin.Bottom;
            fit += $"; modal {card.ActualWidth:0}x{card.ActualHeight:0}" + (needed > viewport.Height + 0.5 ? " NÃO CABE" : " cabe");
        }
        var layoutTooTall = _header.ActualHeight + _footer.ActualHeight > viewport.Height - 2 * Theme.Scaled(48);
        return fit + (layoutTooTall ? " · LISTA ESPREMIDA" : string.Empty);
    }

    /// <summary>Aplica os tokens da faixa atual ao cabeçalho, à lista e ao rodapé e refaz a tela.</summary>
    private void ApplyLayout()
    {
        _header.Padding = new Thickness(Theme.SpaceL, Theme.SpaceM, Theme.SpaceL, Theme.SpaceS);
        _header.ColumnSpacing = Theme.SpaceM;
        _headerRight.MaxWidth = Math.Max(240, Theme.Viewport.Width * 0.4); // o caminho nunca é espremido pelo status
        _logo.Height = Theme.Layout.LogoHeight;
        _logo.Margin = new Thickness(0, 0, 0, Theme.SpaceXs);
        _location.FontSize = Theme.FontTitle;
        _crumbs.Spacing = Theme.SpaceXs;
        _tabStrip.Spacing = Theme.SpaceXs;
        _tabStrip.Margin = new Thickness(-Theme.SpaceS, 0, 0, Theme.SpaceXs);
        _crumbs.Margin = new Thickness(-Theme.SpaceS, Theme.SpaceXs, 0, 0);
        _badge.FontSize = _device.FontSize = _operation.FontSize = _status.FontSize = Theme.FontCaption;
        _empty.FontSize = Theme.FontBody;
        _list.Padding = _grid.Padding = new Thickness(Theme.SpaceM, 0, Theme.SpaceM, 0);
        _list.ItemTemplate = EntryRowTemplate.Create(_density);
        _grid.ItemTemplate = EntryRowTemplate.CreateTile(_density);
        _footer.Padding = new Thickness(Theme.SpaceL, Theme.SpaceS, Theme.SpaceL, Theme.SpaceM);
        _footer.Spacing = Theme.SpaceXs;
        _hints.HorizontalSpacing = Theme.SpaceL;
        _hints.VerticalSpacing = Theme.SpaceXs;
        // Ícones do sistema no tamanho em pixels físicos da linha (DPI × escala da faixa).
        var iconScale = Theme.Layout.RasterizationScale * Theme.Layout.FontScale * Theme.SimulatedTextScale;
        _icons.SetScale(iconScale);
        _tileIcons.SetScale(iconScale);
        UpdateGridMetrics();
        _shownItems = null; // recria as linhas com as novas medidas
        Render();
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
