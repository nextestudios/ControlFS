using ControlFS.App.Controls;
using ControlFS.App.Navigation;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Updates;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly ContentControl _root = new() { IsTabStop = true, UseSystemFocusVisuals = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _location = new() { FontSize = Theme.FontTitle, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _badge = new() { FontSize = Theme.FontCaption, Foreground = Theme.Accent };
    private readonly TextBlock _device = new() { FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _operation = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _empty = new() { FontSize = Theme.FontBody, Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListView _list = new();
    private readonly StackPanel _hints = new() { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceL };
    private readonly TextBlock _status = new() { FontSize = Theme.FontCaption, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _overlay = new();
    private IReadOnlyList<FileEntry>? _shownItems;
    private HashSet<string> _shownSelection = [];
    private bool _fullScreen;

    public MainWindow()
    {
        Title = "ControlFS";
        var settingsStore = new JsonSettingsStore(JsonSettingsStore.DefaultDirectory());
        _updates = GitHubReleaseUpdateService.CreateDefault();
        _app = new AppController(new LocalFileSystemProvider(), new ArchiveService(), settingsStore, _updates);
        _input = new InputHost(_app, DispatcherQueue);

        Content = _root;
        _root.Content = BuildLayout();
        _root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F11) { ToggleFullScreen(); e.Handled = true; return; }
            _input.OnKeyDown(e);
        };
        _root.CharacterReceived += (_, e) => _input.OnCharacter(e.Character);
        _root.Loaded += (_, _) => _root.Focus(FocusState.Programmatic);

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
        };

        _app.Changed += Render;
        _app.ExitRequested += Close;
        _input.StatusChanged += Render;
        _app.Start();
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
        titleStack.Children.Add(_badge);
        titleStack.Children.Add(_location);
        header.Children.Add(titleStack);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_device);
        right.Children.Add(_operation);
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        layout.Children.Add(header);

        // Lista
        _list.ItemTemplate = EntryRowTemplate.Create();
        _list.SelectionMode = ListViewSelectionMode.Single;
        _list.IsItemClickEnabled = true;
        _list.IsTabStop = false;
        _list.AllowFocusOnInteraction = false;
        _list.ItemContainerTransitions = new TransitionCollection(); // sem animações de lista
        _list.Padding = new Thickness(Theme.SpaceM, 0, Theme.SpaceM, 0);
        _list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.Item is not FileEntry entry) return;
            EntryRowTemplate.Fill(args.ItemContainer, entry, _shownSelection.Contains(entry.Id));
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
                _badge.Text = "INÍCIO";
                _location.Text = "Locais";
                break;
            case Screen.FolderPicker:
                _badge.Text = "ESCOLHER PASTA · " + _app.PickerTitle;
                _location.Text = pane.Location?.DisplayPath ?? "…";
                break;
            default:
                _badge.Text = pane.Location is ArchiveLocation ? "COMPACTADO · SOMENTE LEITURA" : "PASTA NO DISCO";
                _location.Text = pane.Location?.DisplayPath ?? "…";
                break;
        }
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
                _ => string.Empty,
            }
            : $"{op.Title} — {(op.Progress is { } p ? $"{p.ItemsProcessed}/{p.ItemsTotal?.ToString() ?? "?"}" : "…")} ({(op.State == OperationState.WaitingForUser ? "aguardando você" : "em andamento")})";

        // Lista (a identidade dos itens decide se o ItemsSource muda)
        IReadOnlyList<FileEntry> items = _app.Screen == Screen.Home ? _app.Places : pane.List.Items;
        var focus = _app.Screen == Screen.Home ? _app.PlacesFocus : pane.List.FocusIndex;
        var selection = _app.Screen == Screen.Home ? new HashSet<string>() : pane.List.SelectedIds.ToHashSet();
        if (!ReferenceEquals(items, _shownItems) || !selection.SetEquals(_shownSelection))
        {
            _shownItems = items;
            _shownSelection = selection;
            _list.ItemsSource = items.ToList();
        }
        if (focus >= 0 && focus < items.Count)
        {
            _list.SelectedIndex = focus;
            _list.ScrollIntoView(items[focus]);
        }
        _empty.Text = pane.IsLoading && _app.Screen != Screen.Home ? "Carregando…" : items.Count == 0 ? "Pasta vazia" : string.Empty;
        if (_app.Screen != Screen.Home && pane.InaccessibleCount > 0 && _app.StatusMessage is null)
            _status.Text = $"{pane.InaccessibleCount} item(ns) sem permissão de leitura foram omitidos.";
        else
            _status.Text = _app.StatusMessage ?? string.Empty;

        // Rodapé: somente ações válidas no contexto, com o rótulo do dispositivo em uso
        _hints.Children.Clear();
        var map = new ActionMap(_app.Settings.Convention);
        foreach (var hint in _app.Hints)
        {
            var glyph = device is not null && map.ControlFor(hint.Action) is { } control
                ? ButtonGlyphs.For(control, _app.Settings.LabelStyle)
                : ButtonGlyphs.KeyboardFor(hint.Action);
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            chip.Children.Add(new Border
            {
                Background = Theme.SurfaceRaised,
                BorderBrush = Theme.Border,
                BorderThickness = Theme.Hairline,
                CornerRadius = Theme.Radius,
                Padding = new Thickness(Theme.SpaceS, 2, Theme.SpaceS, 2),
                Child = new TextBlock { Text = glyph, FontSize = Theme.FontCaption, Foreground = Theme.Text, FontWeight = FontWeights.SemiBold },
            });
            chip.Children.Add(new TextBlock { Text = hint.Label, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center });
            _hints.Children.Add(chip);
        }

        // Camada modal
        _overlay.Children.Clear();
        if (ModalView.Build(_app) is { } modal) _overlay.Children.Add(modal);
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
