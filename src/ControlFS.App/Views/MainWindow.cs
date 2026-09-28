using ControlFS.App.Controls;
using ControlFS.App.Navigation;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Appearance;
using ControlFS.Core.Contracts;
using ControlFS.Core.Layout;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Updates;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Shell;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ControlFS.App.Views;

/// <summary>
/// Janela única: cabeçalho (local + estado), lista virtualizada, rodapé de comandos contextuais
/// e camada modal. Toda interação vira InputAction no AppController.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "InputHost, o serviço de atualização, o canal do celular, os ícones e o monitor de unidades são descartados no evento Closed da janela.")]
public sealed class MainWindow : Window
{
    private readonly AppController _app;
    private readonly InputHost _input;
    private readonly GitHubReleaseUpdateService? _updates;

    /// <summary>Celular como controle (#223). Só escuta na rede durante uma sessão que o usuário abriu; null nas capturas.</summary>
    private readonly Infrastructure.Remote.PhoneLinkServer? _phone;
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
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
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceXs, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _badge = new() { FontSize = Theme.FontCaption, Foreground = Theme.Accent, TextTrimming = TextTrimming.CharacterEllipsis };
    /// <summary>Placa atrás do logo: transparente no tema escuro; escura no claro (o nome no logo é claro, #37).</summary>
    private readonly Border _logoPlate = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Background = Theme.LogoPlate };
    private readonly Image _logo = new() { Height = 44, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Uniform };
    private readonly IconLoader _navIcons;
    private readonly IconLoader _cardIcons;
    private readonly TopBarView _topBar;
    private readonly HomeView _home;
    private readonly TextBlock _device = new() { FontSize = Theme.FontBody, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock _operation = new() { FontSize = Theme.FontBody, Foreground = Theme.Text, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Right, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _empty = new() { FontSize = Theme.FontBody, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListView _list = new();
    /// <summary>Lista (fase C): cartão com o cabeçalho das colunas e as linhas.</summary>
    private readonly Border _listCard = new();
    private readonly ListHeaderView _listHeader;
    private readonly IconLoader _detailIcons;
    private readonly DetailsPanelView _details;
    private readonly PaneView _paneView;
    private readonly TextBlock _paneCaption = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Visibility = Visibility.Collapsed };
    private readonly Grid _content = new();
    private bool _dualShown;
    private int _dualMainColumn;
    private bool _detailsShown;
    private bool _detailsFit = true;
    private EntryRowTemplate.ListColumns? _columns;
    private int _shownStatsVersion = -1;
    private int _shownGitVersion = -1;
    private readonly GridView _grid = new();
    private readonly WrapPanel _hints = new();
    private readonly StackPanel _footer = new();
    private readonly Border _footerBar = new() { Background = Theme.Surface, BorderBrush = Theme.Border };
    private readonly Grid _header = new();
    private readonly StackPanel _headerRight = new() { VerticalAlignment = VerticalAlignment.Center };
    private Grid? _layout;
    private readonly TextBlock _status = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _overlay = new();
    private readonly VideoPlayerView _video = new();

    private IReadOnlyList<FileEntry>? _shownItems;
    private HashSet<string> _shownSelection = [];
    private int _shownFocus = -1;
    private object? _shownClipboard;

    /// <summary>Barra de título do tema (#230): o cabeçalho ocupa a faixa do título; os botões do Windows ficam por cima, à direita.</summary>
    private readonly TitleBarView _titleBar;

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
            new FileOperationService(temporaries), new JsonControllerProfileStore(data), temporaries, new JsonOperationHistoryStore(data),
            new Preview.WicImageDecoder(), new WindowsRecycleBin())
        {
            PdfRenderer = new Infrastructure.Media.Pdf.WindowsPdfRenderer(),
            Terminal = new TerminalLauncher(),
            Git = new Infrastructure.Git.GitStatusReader(),
            MediaPlayer = new Infrastructure.Media.Playback.WindowsMediaPlayerFactory(),
            DiskImages = new Infrastructure.Windows.DiskImages.VirtualDiskService(),
            PlaybackPositions = new JsonPlaybackPositionStore(data),
        };
        if (dataDirectory is null)
        {
            _phone = new Infrastructure.Remote.PhoneLinkServer();
            _app.AttachPhoneLink(_phone);
        }
        _input = new InputHost(_app, DispatcherQueue);
        _icons = new IconLoader(_iconProvider);
        _tileIcons = new IconLoader(_iconProvider, IconLoader.TileIconSize);
        _navIcons = new IconLoader(_iconProvider, TopBarView.IconSize);
        _topBar = new TopBarView(_app, _navIcons);
        _cardIcons = new IconLoader(_iconProvider, HomeView.IconSize);
        _home = new HomeView(_app, _cardIcons);
        _listHeader = new ListHeaderView(_app);
        _detailIcons = new IconLoader(_iconProvider, DetailsPanelView.IconSize);
        _details = new DetailsPanelView(_detailIcons);
        _paneView = new PaneView(_app, _icons);
        // Nas capturas (--render-screens) a janela não muda de modo nem de barra de título: só o espaço dos botões é reservado.
        _titleBar = new TitleBarView(this, _header, _tabs, _app, live: dataDirectory is null);
        _detailIcons.Invalidated += () =>
        {
            _details.ApplyLayout(); // refaz o ícone grande no novo tamanho
            Render();
        };
        _home.SizeChanged += Render;
        _cardIcons.Invalidated += () =>
        {
            _home.ApplyLayout(); // refaz os cartões com ícones no novo tamanho
            Render();
        };
        _icons.Invalidated += OnIconsInvalidated;
        _tileIcons.Invalidated += OnIconsInvalidated;
        _navIcons.Invalidated += () =>
        {
            _topBar.ApplyLayout(); // refaz os atalhos com ícones no novo tamanho
            Render();
        };

        AppLog.Info("MainWindow: serviços criados; montando layout");
        Content = _root;
        _root.Content = _layout = BuildLayout();
        AppLog.Info("MainWindow: layout montado");
        _root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F11) { _app.ToggleFullScreen(); e.Handled = true; return; }
            _input.OnKeyDown(e);
        };
        _root.CharacterReceived += (_, e) => _input.OnCharacter(e.Character);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite); // avisos e resultados são lidos sem mover o foco
        _root.Loaded += (_, _) =>
        {
            _root.Focus(FocusState.Programmatic);
            UpdateLayoutProfile();
            _root.XamlRoot.Changed += (_, _) =>
            {
                UpdateLayoutProfile(); // tamanho, monitor ou DPI
                _titleBar.Update();
            };
            _titleBar.Update();
        };
        // Tamanho do texto do Windows (Acessibilidade): o WinUI aumenta cada texto; o layout decide o que cabe.
        _uiSettings.TextScaleFactorChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLayoutProfile);
        // Transparência, alto contraste e animações do Windows: o painel dos modais fica sólido e sem transição.
        UpdateVisualEffects(render: false);
        _uiSettings.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(() => UpdateVisualEffects());
        // Alto contraste: o evento próprio (AccessibilitySettings.HighContrastChanged) não existe em apps de desktop; a
        // troca de tema de contraste também muda as cores do sistema, e esse evento chega.
        // Também é o aviso de que o modo de apps do Windows (claro/escuro) mudou: o tema automático acompanha (#37).
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateVisualEffects();
            ApplyTheme(_app.Settings);
        });

        Activated += (_, e) =>
        {
            var active = e.WindowActivationState != WindowActivationState.Deactivated;
            _input.OnWindowActivated(active);
            _titleBar.SetActive(active);
            if (active) _root.Focus(FocusState.Programmatic);
        };
        Closed += (_, _) =>
        {
            _app.ReleaseMediaForShutdown();
            _app.PrepareShutdown(); // instala em silêncio uma atualização verificada, se o usuário deixou ligado
            _input.Dispose();
            _phone?.Dispose(); // para de escutar e avisa o celular
            _updates?.Dispose();
            _iconProvider.Dispose();
            _drives.Dispose();
            // #224: com mídia usada, sai sem descarregar as DLLs (tudo já foi salvo: preferências e abas gravam a cada
            // mudança). O encerramento normal derrubava o processo no renderizador de software do Windows (WARP).
            if (Infrastructure.Media.Playback.WindowsMediaPlayerFactory.PlaybackUsed)
            {
                AppLog.Info("Saída direta: mídia do Windows usada nesta sessão");
                Infrastructure.Windows.Diagnostics.ProcessTermination.TerminateCurrent(0);
            }
        };

        _app.Changed += Render;
        _app.ExitRequested += Close;
        _app.ModalBodyScrollRequested += ModalView.ScrollBody;
        _app.CopyText = CopyToClipboard;
        _input.StatusChanged += Render;
        _app.SettingsChanged += settings =>
        {
            ApplyTheme(settings);
            if (settings.Density == _density && settings.View == _view) return;
            _density = settings.Density;
            _view = settings.View;
            UpdateListColumns(force: true);
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
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // cabeçalho
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // barra superior
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // aviso do local (compactado, seletor)
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // rodapé

        // Cabeçalho: logo com o nome, abas (só com 2+) e, à direita, controle em uso e operação/atualização.
        var header = _header;
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _logo.Source = Branding.Logo;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_logo, "ControlFS");
        _logoPlate.Child = _logo;
        header.Children.Add(_logoPlate);
        _tabs.Children.Add(_tabStrip);
        Grid.SetColumn(_tabs, 1);
        header.Children.Add(_tabs);
        var right = _headerRight;
        right.Children.Add(_device);
        right.Children.Add(_operation);
        Grid.SetColumn(right, 2);
        header.Children.Add(right);
        layout.Children.Add(header);
        // Botão de tela cheia colado aos botões do Windows (minimizar, maximizar, fechar), na mesma faixa do cabeçalho.
        layout.Children.Add(_titleBar.Buttons);

        Grid.SetRow(_topBar.Root, 1);
        layout.Children.Add(_topBar.Root);
        Grid.SetRow(_badge, 2);
        layout.Children.Add(_badge);

        // Lista e grade: as duas virtualizadas, preenchidas pelo mesmo código; só a ativa fica visível e com itens.
        UpdateListColumns(force: true);
        _grid.ItemTemplate = EntryRowTemplate.CreateTile(_density);
        _grid.Visibility = Visibility.Collapsed;
        ConfigureItems(_list, _icons);
        ConfigureItems(_grid, _tileIcons);
        _grid.SizeChanged += (_, _) => UpdateGridMetrics();
        // Lista: cartão com o cabeçalho das colunas em cima das linhas; aparece e some junto com a lista.
        var listBody = new Grid();
        listBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // título do painel (só com dois painéis)
        listBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        listBody.Children.Add(_paneCaption);
        Grid.SetRow(_listHeader.Root, 1);
        listBody.Children.Add(_listHeader.Root);
        Grid.SetRow(_list, 2);
        listBody.Children.Add(_list);
        _listCard.Child = listBody;
        _list.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => _listCard.Visibility = _list.Visibility);
        _listCard.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width && UpdateListColumns(force: false)) Render();
        };
        // Painel de detalhes à direita da lista (fase C); grade e início em cartões ocupam as duas colunas.
        var content = _content;
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(_listCard);
        Grid.SetColumn(_details.Root, 1);
        _details.Root.Visibility = Visibility.Collapsed;
        content.Children.Add(_details.Root);
        content.Children.Add(_paneView.Root); // dois painéis (#56): o painel sem foco, na outra coluna
        Grid.SetColumnSpan(_grid, 2);
        Grid.SetColumnSpan(_home.Root, 2);
        content.Children.Add(_grid);
        content.Children.Add(_home.Root);
        content.Children.Add(_empty);
        Grid.SetRow(content, 3);
        layout.Children.Add(content);

        // Rodapé
        // Legendas quebram linha em vez de rolar para o lado: em 1280×720 todas continuam visíveis.
        var footer = _footer;
        footer.Children.Add(_status);
        footer.Children.Add(_hints);
        _footerBar.Child = footer;
        Grid.SetRow(_footerBar, 4);
        layout.Children.Add(_footerBar);

        // Vídeo (#61, #170): cobre a janela inteira, sob a camada modal (menus e diálogos aparecem por cima dele).
        Grid.SetRowSpan(_video.Root, 5);
        layout.Children.Add(_video.Root);
        Grid.SetRowSpan(_overlay, 5);
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
        // #182: o fundo cinza de "mouse em cima"/"pressionado" do próprio ListViewItem/GridViewItem fica na linha sob o
        // ponteiro parado enquanto o controle move o foco (a lista rola por baixo dele) e parece um segundo foco. O único
        // foco visível é o anel do ControlFS; marcação, recorte e bloqueio têm os próprios desenhos na linha.
        foreach (var key in SystemItemHighlightKeys) view.Resources[key] = Theme.Transparent;
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
            // O cartão/linha desenha o próprio espaço (na lista, o cabeçalho usa as mesmas medidas para ficar alinhado).
            args.ItemContainer.Margin = args.ItemContainer.Padding = new Thickness(0);
            args.ItemContainer.MinHeight = 0;
            args.ItemContainer.UseSystemFocusVisuals = false;
            EntryRowTemplate.Fill(args.ItemContainer, entry, args.ItemIndex == _shownFocus, _shownSelection.Contains(entry.Id), _app.IsCut(entry), icons, _specialFolders, RowContext());
        };
        view.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FileEntry entry && _shownItems is { } items) _app.PointerActivateListItem(IndexOf(items, entry));
        };
    }

    private ListViewBase ActiveList => _view == ViewMode.Grid ? _grid : _list;

    /// <summary>Recursos de destaque do contêiner da lista/grade anulados (ver <see cref="ConfigureItems"/>).</summary>
    private static readonly string[] SystemItemHighlightKeys =
    [
        "ListViewItemBackgroundPointerOver", "ListViewItemBackgroundPressed",
        "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed",
        "GridViewItemBackgroundPointerOver", "GridViewItemBackgroundPressed",
        "GridViewItemBackgroundSelected", "GridViewItemBackgroundSelectedPointerOver", "GridViewItemBackgroundSelectedPressed",
        "ListViewItemRevealBackgroundPointerOver", "ListViewItemRevealBackgroundPressed",
        "GridViewItemRevealBackgroundPointerOver", "GridViewItemRevealBackgroundPressed",
    ];

    private EntryRowTemplate.RowContext? _rowContext;

    /// <summary>Contexto das linhas (tipo, soma das pastas do início, marcação, relógio), refeito a cada Render.</summary>
    private EntryRowTemplate.RowContext RowContext() => _rowContext ??= new EntryRowTemplate.RowContext(
        _app.TypeNameOf, HomeFolderSize, _app.ListHeader.CanMark, DateTime.Now, _app.GitState);

    /// <summary>Pastas principais no início: a mesma soma real dos cartões da grade ("Calculando…" enquanto roda).</summary>
    private string? HomeFolderSize(FileEntry entry) =>
        _app.Screen == Screen.Home && entry is { Kind: EntryKind.KnownFolder, FullPath: { } path, IsBlocked: false } && _app.IsHomeStatFolder(path)
            ? EntryText.FolderSize(_app.FolderStatsFor(path))
            : null;

    /// <summary>
    /// Colunas da lista pela largura do cartão e pela densidade. Só troca o modelo das linhas quando algo muda (a coluna de
    /// tipo sai numa lista estreita; a caixa de marcação, onde não se marca).
    /// </summary>
    private bool UpdateListColumns(bool force)
    {
        var width = _listCard.ActualWidth - _listCard.Padding.Left - _listCard.Padding.Right - _list.Padding.Left - _list.Padding.Right;
        var columns = EntryRowTemplate.Columns(_density, width, _app.Screen != Screen.Home && _app.ListHeader.CanMark);
        if (!force && columns == _columns) return false;
        var rebuild = force || columns.Key != _columns?.Key;
        _columns = columns;
        if (!rebuild) return false;
        _list.ItemTemplate = EntryRowTemplate.Create(columns);
        _shownItems = null; // recria as linhas no novo modelo
        return true;
    }

    /// <summary>Refaz o conteúdo das linhas já criadas (somas do início chegando), sem recriar a lista.</summary>
    private void RefillRealized(IReadOnlyList<FileEntry> items, HashSet<string> selection)
    {
        _shownStatsVersion = _app.FolderStatsVersion;
        _shownGitVersion = _app.GitStatusVersion;
        var context = RowContext();
        var (view, icons) = _view == ViewMode.Grid ? ((ListViewBase)_grid, _tileIcons) : (_list, _icons);
        for (var i = 0; i < items.Count; i++)
            if (view.ContainerFromIndex(i) is SelectorItem container)
                EntryRowTemplate.Fill(container, items[i], i == _shownFocus, selection.Contains(items[i].Id), _app.IsCut(items[i]), icons, _specialFolders, context);
    }

    private void OnIconsInvalidated()
    {
        _shownItems = null; // força recriar as linhas com ícones no novo tamanho
        _paneView.Invalidate();
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
        if (_home.Root.Visibility == Visibility.Visible) return; // cartões publicam as próprias colunas
        // Colunas pela largura disponível (não pela resolução): o cartão estica para ocupar a linha inteira.
        var (minWidth, height, _) = EntryRowTemplate.TileSize(_density);
        var gap = EntryRowTemplate.TileGap;
        var available = _grid.ActualWidth - _grid.Padding.Left - _grid.Padding.Right;
        var columns = available > 0 ? Math.Max(1, (int)Math.Floor((available + 0.5) / (minWidth + gap))) : 1;
        var width = available > 0 ? Math.Floor(available / columns) : minWidth + gap;
        height += gap;
        var rows = _grid.ActualHeight > 0 ? Math.Max(1, (int)Math.Floor(_grid.ActualHeight / height)) : 1;
        if (_grid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            var reflow = panel.MaximumRowsOrColumns != columns;
            panel.ItemWidth = width;
            panel.ItemHeight = height;
            panel.MaximumRowsOrColumns = columns;
            // Colunas mudaram (painel de detalhes entrou/saiu, janela redimensionada): o item focado continua à vista.
            if (reflow && _shownItems is { } items && _shownFocus >= 0 && _shownFocus < items.Count)
            {
                var focused = items[_shownFocus];
                DispatcherQueue.TryEnqueue(() => _grid.ScrollIntoView(focused));
            }
        }
        _app.SetGridLayout(columns, rows);
    }

    private void Render()
    {
        // Cabeçalho e barra superior. O aviso abaixo da barra só aparece quando diz algo que o caminho não diz.
        var pane = _app.ActivePane;
        _badge.Text = _app.Screen switch
        {
            Screen.FolderPicker => "ESCOLHER PASTA · " + _app.PickerTitle,
            Screen.Browser when pane.Location is ArchiveLocation => "COMPACTADO · SOMENTE LEITURA" + (_app.ArchiveSummary is { } summary ? " · " + summary : string.Empty),
            Screen.Browser when _app.GitSummary is { } git => git,
            _ => string.Empty,
        };
        _badge.Visibility = _badge.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _logoPlate.Visibility = _logo.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        _topBar.Render();
        RenderTabs(_app.Screen == Screen.Browser && _app.Tabs.Count > 1, _app.Screen == Screen.Browser && pane.Region == PaneRegion.Tabs);
        var device = _input.ActiveDevice;
        _device.Text = !_input.BackendReady
            ? $"Controles indisponíveis ({_input.BackendError}) · use teclado/mouse"
            : device is not null
                ? device.Name + (_input.Devices.Count > 1 ? $" (+{_input.Devices.Count - 1})" : string.Empty)
                : _input.Devices.Count > 0 ? $"{Plural.Of(_input.Devices.Count, "controle", "controles")} — pressione um botão para ativar" : "Nenhum controle — teclado disponível";
        if (_app.PhoneStatusText is { } phone) _device.Text += " · " + phone;
        var op = _app.Operations.Current;
        _operation.Text = op is null
            ? _app.UpdateState switch
            {
                UpdateState.Ready => $"⬆ Atualização {_app.ReadyUpdate!.Manifest.Version} pronta (Menu → Atualizações)",
                UpdateState.Downloading => "⬆ Baixando atualização…",
                UpdateState.AvailableManual => $"⬆ Nova versão {_app.AvailableUpdate!.Version} disponível",
                _ => _app.Clipboard is { } clip ? $"Área de transferência: {Plural.Of(clip.Paths.Count, "item", "itens")} {(clip.IsCut ? Plural.Word(clip.Paths.Count, "recortado", "recortados") : Plural.Word(clip.Paths.Count, "copiado", "copiados"))} — Ações → Colar" : string.Empty,
            }
            : $"{op.Title} — {(op.Progress is { } p ? $"{p.ItemsProcessed}/{p.ItemsTotal?.ToString() ?? "?"}" : "…")} ({(op.State switch { OperationState.WaitingForUser => "aguardando você", OperationState.Paused => "pausada", _ => "em andamento" })})";

        // Lista (a identidade dos itens decide se o ItemsSource muda)
        if (!ReferenceEquals(_app.Places, _shownPlaces))
        {
            // Pastas especiais (Downloads, Documentos…) mostram o ícone próprio também dentro das pastas do disco.
            _shownPlaces = _app.Places;
            _specialFolders = new HashSet<string>(_app.Places.Where(p => p.Kind == EntryKind.KnownFolder && p.FullPath is not null).Select(p => p.FullPath!), StringComparer.OrdinalIgnoreCase);
        }
        var dual = _app.DualPaneActive && _app.Screen == Screen.Browser;
        var thisPcCards = !dual && _app.Screen == Screen.Browser && _view == ViewMode.Grid && pane is { Location: ThisPcLocation, IsLoading: false } && pane.List.Items.Count > 0;
        if (ShowHomeCards(_app.Screen == Screen.Home && _view == ViewMode.Grid, thisPcCards))
        {
            if (thisPcCards) _home.RenderThisPc(pane);
            else _home.RenderHome();
        }
        else
            RenderItems(pane);
        RenderDualPane(dual);
        RenderDetails();
        RenderFooter();
        _titleBar.Render();
    }

    /// <summary>
    /// Início em grade: os cartões por seção substituem a grade virtualizada (poucos itens, tamanhos diferentes por
    /// seção). Devolve se os cartões estão à mostra.
    /// </summary>
    private bool ShowHomeCards(bool home, bool thisPc)
    {
        var show = home || thisPc;
        var visible = show ? Visibility.Visible : Visibility.Collapsed;
        if (_cardsShowThisPc != thisPc) _home.Reset(); // outra página nos mesmos cartões
        _cardsShowThisPc = thisPc;
        if (_home.Root.Visibility != visible)
        {
            _home.Root.Visibility = visible;
            ActiveList.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            if (show)
            {
                ActiveList.ItemsSource = null;
                _empty.Text = string.Empty;
            }
            _shownItems = null; // ao sair do início, a lista/grade é preenchida de novo
        }
        return show;
    }

    private bool _cardsShowThisPc;

    private void RenderItems(PaneState pane)
    {
        _rowContext = null;
        if (_view == ViewMode.List)
        {
            UpdateListColumns(force: false);
            if (_columns is { } columns) _listHeader.Render(columns, _app.ListHeader, _list.Padding);
        }
        IReadOnlyList<FileEntry> items = _app.Screen == Screen.Home ? _app.Places : pane.List.Items;
        var focus = _app.Screen == Screen.Home ? _app.PlacesFocus : pane.List.FocusIndex;
        if (_app.FocusRegion != PaneRegion.List) focus = -1; // um só foco visível: o da barra superior ou das abas
        var selection = _app.Screen == Screen.Home ? new HashSet<string>() : pane.List.SelectedIds.ToHashSet();
        if (focus >= items.Count) focus = -1;
        var sourceChanged = !ReferenceEquals(items, _shownItems);
        if (sourceChanged) (_shownStatsVersion, _shownGitVersion) = (_app.FolderStatsVersion, _app.GitStatusVersion);
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
        else if (_app.GitStatusVersion != _shownGitVersion)
        {
            // Status do Git chegando depois da lista: só as linhas já criadas ganham a marca.
            RefillRealized(items, selection);
        }
        else if (_app.FolderStatsVersion != _shownStatsVersion && _app.Screen == Screen.Home && _view == ViewMode.List)
        {
            // Somas das pastas principais chegando: só as linhas já criadas mudam (sem recriar a lista nem rolar).
            RefillRealized(items, selection);
        }
        if (!sourceChanged && focus != _shownFocus)
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
    }

    /// <summary>
    /// Painel de detalhes ao lado da lista e da grade (também no início e em Meu computador em cartões). Cada exibição
    /// tem a sua escolha (Configurações → Painel de detalhes); sem escolha, ele aparece onde cabe sem apertar o
    /// conteúdo (a lista com o nome legível, a grade com pelo menos duas colunas) e sai em portáteis e janelas estreitas.
    /// Mostrado pelo menu num espaço apertado, ele estreita e a grade recalcula as colunas com a largura que sobra. A
    /// visibilidade é publicada no AppController, que só mede/decodifica o item focado com o painel à mostra.
    /// </summary>
    private void RenderDetails()
    {
        var content = _home.Root.Visibility == Visibility.Visible || ActiveList.Visibility == Visibility.Visible;
        var (width, fits) = DetailsLayout();
        var show = content && !_dualShown && (_app.DetailsPanelPreference ?? fits);
        if (show != _detailsShown || fits != _detailsFit)
        {
            if (show != _detailsShown)
            {
                _detailsShown = show;
                _details.Root.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ApplyDetailsSpace();
            }
            _detailsFit = fits;
            DispatcherQueue.TryEnqueue(() => _app.SetDetailsPanelVisible(show, fits)); // fora do Render (o aviso refaz a tela)
        }
        if (!show) return;
        _details.Root.Width = width;
        _details.Render(_app.Details, _specialFolders);
    }

    /// <summary>
    /// Largura do painel (≈ um quarto da tela, como na referência) e se ele cabe sem apertar o conteúdo: na lista o nome
    /// precisa de espaço mesmo sem a coluna de tipo; na grade ficam pelo menos duas colunas. Quando não cabe (e o menu
    /// pediu o painel), ele estreita até o mínimo, deixando uma coluna inteira da grade ou o nome legível na lista.
    /// </summary>
    private (double Width, bool Fits) DetailsLayout()
    {
        var width = Math.Clamp(Math.Round(Theme.Viewport.Width * 0.24), Theme.Scaled(360), Theme.Scaled(560));
        double room; // largura do conteúdo com o painel à mostra, além do próprio painel
        double needed; // o mínimo que o conteúdo precisa para não cortar nada essencial
        bool fits;
        if (_view == ViewMode.Grid)
        {
            var tile = EntryRowTemplate.TileSize(_density).MinWidth + EntryRowTemplate.TileGap;
            room = Theme.Viewport.Width - Theme.SpaceL - _grid.Padding.Left - GridTrailingGutter(true);
            fits = room - width >= (2 * tile) - 0.5;
            needed = tile;
        }
        else
        {
            room = Theme.Viewport.Width - (2 * Theme.SpaceL) - DetailsGap - _list.Padding.Left - _list.Padding.Right;
            var columns = EntryRowTemplate.Columns(_density, room - width, mark: true) with { Type = false };
            fits = room - width - columns.FixedWidth >= Theme.Scaled(340);
            needed = columns.FixedWidth + Theme.Scaled(340);
        }
        if (!fits) width = Math.Max(Theme.Scaled(280), Math.Min(width, Math.Floor(room - needed)));
        return (width, fits);
    }

    private static double DetailsGap => Theme.Space(28);

    /// <summary>Margem direita da grade: a mesma do início, ou só a distância até o painel quando ele está à mostra.</summary>
    private static double GridTrailingGutter(bool details) =>
        details ? DetailsGap - (EntryRowTemplate.TileGap / 2) : Theme.SpaceL + Theme.Space(28) - (EntryRowTemplate.TileGap / 2);

    /// <summary>
    /// O conteúdo cede a coluna do painel: a lista e a grade encolhem (a grade recalcula as colunas ao medir a nova
    /// largura) e os cartões do início refazem as colunas. Foco, marcação e item em foco continuam os mesmos.
    /// </summary>
    private void ApplyDetailsSpace()
    {
        _listCard.Margin = ListCardMargin;
        var span = _detailsShown || _dualShown ? 1 : 2;
        Grid.SetColumnSpan(_grid, span);
        Grid.SetColumnSpan(_home.Root, span);
        var padding = _grid.Padding;
        _grid.Padding = new Thickness(padding.Left, padding.Top, GridTrailingGutter(_detailsShown), padding.Bottom);
        _home.SetTrailingGutter(_detailsShown ? DetailsGap : null);
    }

    /// <summary>Margens do cartão da lista (à direita, o espaço até o painel quando ele aparece).</summary>
    private Thickness ListCardMargin => _dualShown ? DualMargin(_dualMainColumn) : new(Theme.SpaceL, Theme.Space(20), _detailsShown ? DetailsGap : Theme.SpaceL, Theme.Space(24));

    /// <summary>Dois painéis: margem externa igual à da lista e meia distância entre os painéis.</summary>
    private static Thickness DualMargin(int column) => column == 0
        ? new(Theme.SpaceL, Theme.Space(20), Theme.SpaceS, Theme.Space(24))
        : new(Theme.SpaceS, Theme.Space(20), Theme.SpaceL, Theme.Space(24));

    /// <summary>
    /// Dois painéis (#56): o painel ativo usa a lista/grade principal na coluna dele (esquerda ou direita, sempre a mesma
    /// para cada painel), contornado em ciano e com o título "ativo"; o outro aparece esmaecido na outra coluna. O painel
    /// de detalhes sai (não cabe). No início, em portáteis ou com um painel, tudo volta ao layout de sempre.
    /// </summary>
    private void RenderDualPane(bool dual)
    {
        var column = _app.SecondPaneFocused ? 1 : 0;
        if (dual != _dualShown || (dual && column != _dualMainColumn))
        {
            _dualShown = dual;
            _dualMainColumn = column;
            _content.ColumnDefinitions[1].Width = dual ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            var main = dual ? column : 0;
            Grid.SetColumn(_listCard, main);
            Grid.SetColumn(_grid, main);
            Grid.SetColumn(_empty, main);
            Grid.SetColumn(_paneView.Root, 1 - main);
            _paneView.Root.Margin = DualMargin(1 - main);
            _listCard.BorderBrush = Theme.Accent;
            _listCard.BorderThickness = dual ? Theme.FocusRing : new Thickness(0);
            _paneCaption.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
            if (dual) _paneView.Show();
            else _paneView.Hide();
            ApplyDetailsSpace();
        }
        if (!dual || _app.OtherPane is not { } other) return;
        _paneCaption.Text = $"{_app.PaneName(_app.Browser).ToUpperInvariant()} · ATIVO";
        _paneView.Render(other, _list.ItemTemplate, RowContext, _specialFolders);
    }

    private void RenderFooter()
    {
        var pane = _app.ActivePane;
        var search = _app.Screen == Screen.Home ? null : pane.ActiveSearch;
        if (search is not null && _app.StatusMessage is null)
            _status.Text = search.Summary; // parcial, concluída ou cancelada, e as pastas puladas
        else if (_app.Screen != Screen.Home && pane.InaccessibleCount > 0 && _app.StatusMessage is null)
            _status.Text = $"{Plural.Of(pane.InaccessibleCount, "item", "itens")} sem permissão de leitura {Plural.Word(pane.InaccessibleCount, "foi omitido", "foram omitidos")}.";
        else
            _status.Text = _app.StatusMessage ?? string.Empty;
        _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; // sem aviso, o rodapé fica só com as legendas

        // Rodapé: somente ações válidas no contexto, com a legenda do dispositivo em uso (glifo do controle ou tecla)
        _hints.Children.Clear();
        var glyphHeight = FooterGlyphHeight;
        // L1 e R1 aparecem nas pontas da barra superior enquanto o foco está no conteúdo.
        var regionsShownAbove = _app.TopModal is null && _app.FocusRegion == PaneRegion.List;
        foreach (var prompt in _app.Prompts)
        {
            if (regionsShownAbove && prompt.Action is InputAction.PreviousRegion or InputAction.NextRegion) continue;
            _hints.Children.Add(ModalView.PromptChip(prompt, glyphHeight, FooterLabelSize));
        }
        // Com um modal aberto, as legendas ficam no próprio painel: as do rodapé somem sem mudar a altura dele.
        _hints.Opacity = _app.TopModal is null ? 1 : 0;

        _video.Render(_app);

        // Camada modal
        // O mesmo painel mantido (só o foco mudou) fica na árvore: tirar e recolocar refaria a rolagem e o painel fosco.
        var modal = ModalView.Build(_app);
        if (modal is null) _overlay.Children.Clear();
        else if (_overlay.Children.Count != 1 || !ReferenceEquals(_overlay.Children[0], modal))
        {
            _overlay.Children.Clear();
            _overlay.Children.Add(modal);
        }
        RestoreKeyboardFocus();
        Announce();
    }

    /// <summary>Glifos do rodapé grandes o bastante para ler a distância (a referência usa botões redondos de ~2,5× o texto).</summary>
    private static double FooterGlyphHeight => Math.Round(Theme.FontBody * (Theme.Layout.Tier == Core.Layout.LayoutTier.Compact ? 1.9 : 2.3));

    /// <summary>Texto das legendas: um pouco maior fora dos portáteis, como na referência.</summary>
    private static double FooterLabelSize => Theme.Layout.Tier == Core.Layout.LayoutTier.Compact ? Theme.FontBody : Theme.Font(20);

    /// <summary>
    /// Narrador: o foco do XAML fica na raiz, então cada mudança do foco lógico (item, menu, diálogo, tecla) vira uma
    /// notificação de UI Automation com o texto que o AppController descreve. Mensagens do rodapé são uma região viva.
    /// </summary>
    private void Announce()
    {
        if (_status.Text != _announcedStatus)
        {
            _announcedStatus = _status.Text;
            if (_status.Text.Length > 0) (FrameworkElementAutomationPeer.FromElement(_status) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_status))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        if (_app.TakeAnnouncement() is not { Length: > 0 } text) return;
        AutomationProperties.SetName(_root, text);
        (FrameworkElementAutomationPeer.FromElement(_root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_root))?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, text, "ControlFS.Focus");
    }

    private string? _announcedStatus;

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
    /// Faixa de abas do navegador: a ativa em destaque; com o foco na faixa (Cima na barra superior), a ativa recebe o
    /// anel de foco e L1/R1 trocam de aba. Só aparece no navegador com 2+ abas: com uma, repetiria a pasta do caminho.
    /// Sem legenda de botão ao lado (#176).
    /// </summary>
    private void RenderTabs(bool visible, bool focused)
    {
        _tabStrip.Children.Clear();
        _tabs.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var tabs = _app.Tabs;
        for (var i = 0; i < tabs.Count; i++)
        {
            var active = i == _app.ActiveTab;
            var title = _app.TabLabel(i);
            var chip = new Border
            {
                Child = new TextBlock
                {
                    Text = title,
                    FontSize = Theme.FontCaption + 1,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = active ? Theme.Text : Theme.TextMuted,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = Theme.Scaled(200),
                },
                CornerRadius = Theme.Radius,
                Padding = new Thickness(Theme.SpaceS + Theme.SpaceXs, Theme.SpaceXs, Theme.SpaceS + Theme.SpaceXs, Theme.SpaceXs),
            };
            Theme.ApplyFocus(chip, focused && active);
            if (active && !focused)
            {
                chip.Background = Theme.SurfaceRaised; // ativa sem foco: destaque discreto
                chip.BorderBrush = Theme.Border;
            }
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
        if (Theme.SetLayout(profile, size))
        {
            ApplyLayout();
        }
        else if (previous != size)
        {
            // Mesma faixa: só o que depende do tamanho exato (altura máxima de menus, largura do status).
            _headerRight.MaxWidth = Math.Max(240, size.Width * 0.35);
            _topBar.Invalidate();
            Render();
        }
    }

    /// <summary>
    /// Efeitos de transparência desligados ou alto contraste: painéis sólidos (e fundo mais escuro) nos modais; animações
    /// do Windows desligadas: modais sem transição. Lido na abertura e a cada mudança nas Configurações.
    /// </summary>
    private void UpdateVisualEffects(bool render = true)
    {
        if (_layoutPinned) return; // capturas: o gerador escolhe
        var solid = !Setting(() => _uiSettings.AdvancedEffectsEnabled, fallback: true) || Setting(() => _accessibility.HighContrast, fallback: false);
        var reduceMotion = !Setting(() => _uiSettings.AnimationsEnabled, fallback: true);
        if (solid == Theme.SolidSurfaces && reduceMotion == Theme.ReduceMotion) return;
        Theme.SolidSurfaces = solid;
        Theme.ReduceMotion = reduceMotion;
        if (render) Render();
    }

    /// <summary>
    /// Tema e destaque (#37): resolve o automático pelo modo de apps do Windows e troca as cores dos pincéis compartilhados
    /// (tudo na tela muda na hora). Os controles do sistema (rolagem, barra de progresso) seguem pelo RequestedTheme; o que
    /// usa cores derivadas (degradê do painel de detalhes, linhas criadas com medidas) é refeito pelo ApplyLayout.
    /// </summary>
    private void ApplyTheme(AppSettings settings)
    {
        var dark = ThemePalettes.IsDark(settings.Theme, SystemIsDark());
        var changed = Theme.Apply(ThemePalettes.Build(dark, settings.Accent));
        // Botões da barra de título (#230): sempre, também na abertura e quando só o alto contraste mudou.
        _titleBar.ApplyColors(Setting(() => _accessibility.HighContrast, fallback: false));
        if (!changed) return;
        _root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        if (_root.XamlRoot is not null) ApplyLayout();
    }

    /// <summary>Modo de apps do Windows: escuro quando a cor de fundo do sistema é escura (sem resposta: escuro, o padrão do app).</summary>
    private bool SystemIsDark()
    {
        try
        {
            var background = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return background.R + background.G + background.B < 3 * 128;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return true;
        }
    }

    /// <summary>Lê uma configuração do Windows; se o sistema não responder, usa a reserva (o painel continua legível).</summary>
    private static bool Setting(Func<bool> read, bool fallback)
    {
        try
        {
            return read();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return fallback;
        }
    }

    /// <summary>Gerador de capturas: força o painel sólido (transparência reduzida) para conferir a reserva.</summary>
    internal void SimulateSolidSurfaces(bool solid)
    {
        Theme.SolidSurfaces = solid;
        Theme.ReduceMotion = true;
        Render();
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
        var fit = $"cabeçalho {_header.ActualHeight:0} + barra {_topBar.Root.ActualHeight:0}, {(_home.Root.Visibility == Visibility.Visible ? $"início em cartões ({string.Join(", ", _app.HomeSections.Select(s => $"{s.Title} {_app.HomeColumns(s.Kind)} col."))})" : _view == ViewMode.Grid ? $"grade {_app.GridColumns}x{_app.GridRowsPerPage}" : "lista")} {(_home.Root.Visibility == Visibility.Visible ? _home.Root.ActualHeight : ActiveList.ActualHeight):0}, rodapé {_footerBar.ActualHeight:0} de {viewport.Height:0} px efetivos";
        if (_overlay.Children.Count > 0 && _overlay.Children[0] is Panel scrim && scrim.Children.OfType<FrameworkElement>().FirstOrDefault(c => Equals(c.Tag, ModalView.CardTag)) is { } card)
        {
            var needed = card.ActualHeight + card.Margin.Top + card.Margin.Bottom;
            fit += $"; modal {card.ActualWidth:0}x{card.ActualHeight:0}" + (needed > viewport.Height + 0.5 ? " NÃO CABE" : " cabe");
        }
        var layoutTooTall = _header.ActualHeight + _topBar.Root.ActualHeight + _badge.ActualHeight + _footerBar.ActualHeight > viewport.Height - 2 * Theme.Scaled(48);
        return fit + (layoutTooTall ? " · LISTA ESPREMIDA" : string.Empty);
    }

    /// <summary>Aplica os tokens da faixa atual ao cabeçalho, à lista e ao rodapé e refaz a tela.</summary>
    private void ApplyLayout()
    {
        _header.Padding = new Thickness(Theme.SpaceL + Theme.SpaceXs, Theme.SpaceM, Theme.SpaceL + _titleBar.ReservedWidth, Theme.SpaceM);
        _header.ColumnSpacing = Theme.SpaceXl;
        _headerRight.MaxWidth = Math.Max(240, Theme.Viewport.Width * 0.35); // as abas nunca são espremidas pelo status
        _logo.Height = Theme.Layout.LogoHeight;
        _logoPlate.CornerRadius = new CornerRadius(Theme.Scaled(12));
        _logoPlate.Padding = Theme.IsDark ? new Thickness(0) : new Thickness(Theme.SpaceS, Theme.SpaceXs, Theme.SpaceM, Theme.SpaceXs);
        _tabStrip.Spacing = Theme.SpaceXs;
        _tabs.Spacing = Theme.SpaceS;
        _badge.Margin = new Thickness(Theme.SpaceL + Theme.SpaceS, 0, Theme.SpaceL, Theme.SpaceS);
        _topBar.ApplyLayout();
        _home.ApplyLayout();
        _home.SetTrailingGutter(_detailsShown ? DetailsGap : null);
        _badge.FontSize = _status.FontSize = Theme.FontCaption;
        // Controle em uso e operação na faixa do título (#230): no tamanho do corpo, legíveis de longe.
        _device.FontSize = _operation.FontSize = Theme.FontBody;
        _empty.FontSize = Theme.FontBody;
        // Lista (fase C): cartão escuro com cantos arredondados, recuado como na referência; linhas quase até a borda.
        _listCard.Background = Theme.SurfaceRaised;
        _listCard.CornerRadius = new CornerRadius(Theme.Scaled(14));
        _listCard.Margin = ListCardMargin;
        _details.Root.Margin = new Thickness(0, Theme.Space(20), Theme.SpaceL, Theme.Space(24));
        _details.ApplyLayout();
        _paneView.ApplyLayout();
        _paneCaption.FontSize = Theme.FontCaption;
        _paneCaption.Foreground = Theme.Accent;
        _paneCaption.Margin = new Thickness(Theme.Space(18), Theme.Space(4), Theme.Space(18), Theme.Space(6));
        // Dois painéis só onde cabem com o nome legível: portáteis e janelas estreitas ficam com um (a escolha continua salva).
        var dualFits = Theme.Layout.Tier != Core.Layout.LayoutTier.Compact && Theme.Viewport.Width >= 1200;
        DispatcherQueue.TryEnqueue(() => _app.SetDualPaneFits(dualFits));
        _listCard.Padding = new Thickness(0, Theme.Space(6), 0, Theme.Space(10));
        _list.Padding = new Thickness(Theme.Space(10), Theme.Space(6), Theme.Space(10), 0);
        // Grade: cartões alinhados com os do início (margem lateral igual, meia distância entre cartões de cada lado).
        var gutter = GridTrailingGutter(false);
        _grid.Padding = new Thickness(gutter, Theme.Space(16), GridTrailingGutter(_detailsShown), Theme.Space(16));
        UpdateListColumns(force: true);
        _grid.ItemTemplate = EntryRowTemplate.CreateTile(_density);
        _footerBar.BorderThickness = new Thickness(0, Theme.Hairline.Top, 0, 0);
        // Rodapé mais alto fora dos portáteis (a referência tem ~110 px a 1080p); em 720p/800p a lista precisa do espaço.
        var compact = Theme.Layout.Tier == Core.Layout.LayoutTier.Compact;
        var footerVertical = compact ? Theme.SpaceM : Theme.Space(26);
        _footer.Padding = new Thickness(Theme.SpaceL + Theme.SpaceS, footerVertical, Theme.SpaceL, footerVertical);
        _footer.Spacing = Theme.SpaceS;
        _hints.HorizontalSpacing = compact ? Theme.SpaceXl : Theme.Space(52);
        _hints.VerticalSpacing = Theme.SpaceS;
        // Ícones do sistema no tamanho em pixels físicos da linha (DPI × escala da faixa).
        var iconScale = Theme.Layout.RasterizationScale * Theme.Layout.FontScale * Theme.SimulatedTextScale;
        _icons.SetScale(iconScale);
        _tileIcons.SetScale(iconScale);
        _navIcons.SetScale(iconScale);
        _cardIcons.SetScale(iconScale);
        _detailIcons.SetScale(iconScale);
        UpdateGridMetrics();
        _shownItems = null; // recria as linhas com as novas medidas
        Render();
    }

    /// <summary>
    /// Espaço dos botões da barra de título mudou (tela cheia, DPI, Windows 10/11): o cabeçalho reserva a nova largura à
    /// direita para o status nunca ficar sob minimizar/maximizar/fechar.
    /// </summary>
    internal void OnTitleBarInsetChanged() =>
        _header.Padding = new Thickness(Theme.SpaceL + Theme.SpaceXs, Theme.SpaceM, Theme.SpaceL + _titleBar.ReservedWidth, Theme.SpaceM);

    private static int IndexOf(IReadOnlyList<FileEntry> items, FileEntry entry)
    {
        for (var i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], entry) || items[i].Id == entry.Id) return i;
        return -1;
    }
}
