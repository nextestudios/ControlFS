using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.App.Views;
using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Appearance;
using ControlFS.Core.Input;
using ControlFS.Core.Layout;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;

namespace ControlFS.App.Diagnostics;

/// <summary>
/// Modo de desenvolvimento <c>--render-screens &lt;pasta&gt;</c>: monta as telas reais (início, pasta, menu, teclado) em
/// cada resolução/escala-alvo, salva PNGs com <see cref="RenderTargetBitmap"/> e fecha o app. Também gera a galeria dos
/// glifos dos botões (todas as famílias, tema escuro e claro, 100/200/300%). Usado pelo workflow Smoke: a tela do
/// runner do GitHub é pequena, então a resolução é simulada — a árvore é disposta no tamanho efetivo (pixels ÷
/// escala) e desenhada com uma transformação de escala, como o Windows faria com aquele DPI.
/// Não toca nos dados do usuário: preferências numa pasta temporária, sem rede, arquivos de exemplo em %TEMP%.
/// </summary>
internal static class ScreenRenderer
{
    public const string Switch = "--render-screens";

    private sealed record Target(string Name, int Width, int Height, double Scale, double TextScale = 1, bool Modals = false)
    {
        public double EffectiveWidth => Width / Scale;
        public double EffectiveHeight => Height / Scale;
    }

    /// <summary>Critério de aceite do #36 (720p, 800p, 1080p, 4K) e as escalas que o Windows costuma usar nelas.</summary>
    private static readonly Target[] Targets =
    [
        new("1280x720", 1280, 720, 1, Modals: true),
        new("1280x800", 1280, 800, 1, Modals: true),
        new("1280x800-text150", 1280, 800, 1, TextScale: 1.5),
        new("1920x1080", 1920, 1080, 1, Modals: true),
        new("1920x1080-150", 1920, 1080, 1.5),
        new("3840x2160", 3840, 2160, 1, Modals: true),
        new("3840x2160-200", 3840, 2160, 2, Modals: true),
        new("3840x2160-300", 3840, 2160, 3),
    ];

    private static readonly StringBuilder Report = new();

    /// <summary><c>--only a,b</c>: só as capturas cujo nome começa com um dos prefixos (ex.: <c>m1,2d,glyphs</c>).</summary>
    private static string[]? _only;

    /// <summary><c>--sizes a,b</c>: só estes alvos, pelo nome exato (ex.: <c>1920x1080,1280x720</c>).</summary>
    private static string[]? _sizes;

    private static string[]? ListArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return null;
        var items = args[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Length == 0 || items.Any(i => string.Equals(i, "all", StringComparison.OrdinalIgnoreCase)) ? null : items;
    }

    private static bool Wanted(string capture) =>
        _only is null || _only.Any(p => capture.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Algum filtro cai dentro do grupo (ex.: <c>m1</c> pede o grupo <c>m</c> dos modais).</summary>
    private static bool WantedGroup(string prefix) =>
        _only is null || _only.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || prefix.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pasta de saída se o app foi aberto com <c>--render-screens &lt;pasta&gt;</c>.</summary>
    public static string? OutputDirectory(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }

    public static async Task RunAsync(string outputDirectory, string[] args)
    {
        _only = ListArgument(args, "--only");
        _sizes = ListArgument(args, "--sizes");
        var work = Path.Join(Path.GetTempPath(), "ControlFS-render-" + Guid.NewGuid().ToString("N")[..8]);
        MainWindow? window = null;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var sample = CreateSampleFolder(Path.Join(work, "files"));
            var shortcuts = CreateShortcutFolder(Path.Join(work, "files", "Área de trabalho"));
            var modals = CreateModalFolder(Path.Join(work, "files", "Modais"));
            window = new MainWindow(Path.Join(work, "data"));
            var loaded = new TaskCompletionSource();
            window.RootHost.Loaded += (_, _) => loaded.TrySetResult();
            window.Activate();
            await loaded.Task;

            // A árvore sai da janela e vai para um palco do tamanho-alvo (o Canvas não recorta o que passa da janela).
            var layout = window.LayoutRoot;
            window.RootHost.Content = null;
            var stage = new Grid { Background = Theme.Background, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            layout.HorizontalAlignment = HorizontalAlignment.Left;
            layout.VerticalAlignment = VerticalAlignment.Top;
            stage.Children.Add(layout);
            var canvas = new Canvas();
            canvas.Children.Add(stage);
            window.RootHost.Content = canvas;

            var app = window.Controller;
            app.SetActiveController(ControllerFamily.Xbox); // legendas com glifos, como com um controle em uso
            window.SimulateSolidSurfaces(false); // painel fosco; a reserva sólida tem a própria captura
            SetTheme(app, ThemeMode.Dark, AccentColor.Cyan); // as referências são do tema escuro; o claro tem capturas próprias (7*)
            var targets = Targets.Where(t => _sizes is null || _sizes.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (targets.Length == 0 && _sizes is not null) throw new ArgumentException("--sizes: nenhum alvo conhecido (" + string.Join(", ", Targets.Select(t => t.Name)) + ")");
            foreach (var target in targets)
            {
                window.AppWindow.Resize(new SizeInt32(target.Width, target.Height)); // o Windows limita à tela; ajuda a virtualização
                window.PinLayout(LayoutBreakpoints.Select(target.EffectiveWidth, target.EffectiveHeight, target.Scale, target.TextScale),
                    new Windows.Foundation.Size(target.EffectiveWidth, target.EffectiveHeight), target.TextScale);
                stage.Width = target.Width;
                stage.Height = target.Height;
                layout.Width = target.EffectiveWidth;
                layout.Height = target.EffectiveHeight;
                layout.RenderTransform = new ScaleTransform { ScaleX = target.Scale, ScaleY = target.Scale };
                var dir = Path.Join(outputDirectory, "screens", target.Name);
                Directory.CreateDirectory(dir);
                Report.AppendLine($"{target.Name}: {target.Width}x{target.Height} @ {target.Scale:0.##}x, texto {target.TextScale:0.##}x → {Theme.Layout.Tier} (fonte {Theme.Layout.FontScale:0.##}x)");

                app.GoHome();
                // Barra de título no tema (#230): a faixa do topo (logo, tela cheia e os botões do Windows imitados) no escuro
                // e no claro, a 1080p.
                if (target.Name == "1920x1080" && WantedGroup("0"))
                {
                    await CaptureAsync(stage, target, dir, "0-title-bar-dark", window);
                    SetTheme(app, ThemeMode.Light, AccentColor.Cyan);
                    app.GoHome();
                    await CaptureAsync(stage, target, dir, "0b-title-bar-light", window);
                    SetTheme(app, ThemeMode.Dark, AccentColor.Cyan);
                    app.GoHome();
                }
                await CaptureAsync(stage, target, dir, "1-home", window);
                app.Handle(InputAction.PreviousRegion); // L1: acesso rápido da barra superior
                app.Handle(InputAction.NavigateRight);
                await CaptureAsync(stage, target, dir, "1b-home-top-bar", window);
                app.Handle(InputAction.Back);

                // Início em grade (fase B): cartões das pastas principais com contagem/tamanho reais e das unidades.
                app.Handle(InputAction.ChangeView);
                for (var i = 0; i < 10 && app.PlacesFocus != app.HomeSections[0].Places[0]; i++) app.Handle(InputAction.PageUp); // primeiro cartão
                await app.WhenIdleAsync(); // contagens e tamanhos calculados
                await CaptureAsync(stage, target, dir, "1c-home-grid", window);
                app.Handle(InputAction.PageDown); // primeira unidade
                await CaptureAsync(stage, target, dir, "1d-home-grid-drives", window);
                ChooseQuickAccess(app, "Meu computador");
                await app.WhenIdleAsync();
                await CaptureAsync(stage, target, dir, "1e-this-pc-grid", window);
                app.Handle(InputAction.ChangeView);
                await CaptureAsync(stage, target, dir, "1f-this-pc-list", window);
                app.GoHome();

                app.OpenPhysical(sample);
                await app.WhenIdleAsync();
                app.Handle(InputAction.ToggleSelection);
                app.Handle(InputAction.NavigateDown);
                app.Handle(InputAction.ToggleSelection);
                app.Handle(InputAction.NavigateDown);
                app.Handle(InputAction.NavigateDown); // foco no nome longo (mostra a quebra em até três linhas)
                await CaptureAsync(stage, target, dir, "2-folder", window);
                // Lista (fase C): ordenada por tamanho, do maior para o menor (seta no título "Tamanho").
                ChooseAppMenu(app, "Ordenar por"); // tipo
                ChooseAppMenu(app, "Ordenar por"); // tamanho
                ChooseAppMenu(app, "Ordem");
                await CaptureAsync(stage, target, dir, "2c-folder-sorted-size", window);
                ChooseAppMenu(app, "Ordenar por"); // data
                ChooseAppMenu(app, "Ordenar por"); // nome
                ChooseAppMenu(app, "Ordem");

                // Painel de detalhes (fase C): pasta, imagem (miniatura real) e compactado (formato e arquivos contados).
                await FocusAsync(app, stage, "Fotos da viagem");
                await CaptureAsync(stage, target, dir, "2d-details-folder", window);
                await FocusAsync(app, stage, "logo.png");
                await CaptureAsync(stage, target, dir, "2e-details-image", window);
                await FocusAsync(app, stage, "backup-2026-09.zip");
                await CaptureAsync(stage, target, dir, "2f-details-archive", window);
                await FocusAsync(app, stage, "arquivo sem extensão");
                app.Handle(InputAction.PreviousRegion); // L1: pasta de cima na barra de caminho
                await CaptureAsync(stage, target, dir, "2b-folder-path-bar", window);
                app.Handle(InputAction.Back);

                // Abas (#176): com 2+ a faixa aparece no cabeçalho, sem legenda própria; Cima na barra superior leva a ela.
                ChooseAppMenu(app, "Abas");
                ChooseOpenMenu(app, "Nova aba");
                await app.WhenIdleAsync();
                app.Handle(InputAction.PreviousRegion);
                app.Handle(InputAction.NavigateUp);
                await CaptureAsync(stage, target, dir, "2g-folder-tabs", window);
                app.Handle(InputAction.Back);
                ChooseAppMenu(app, "Abas");
                ChooseOpenMenu(app, "Fechar aba");
                await app.WhenIdleAsync();

                // Dois painéis (#56): o direito abre na mesma pasta; L3 ativa o direito, que entra numa subpasta.
                ChooseAppMenu(app, "Dois painéis");
                await app.WhenIdleAsync();
                await CaptureAsync(stage, target, dir, "2h-dual-pane", window);
                app.Handle(InputAction.SwitchPane);
                await FocusAsync(app, stage, "Fotos da viagem");
                app.Handle(InputAction.Confirm);
                await app.WhenIdleAsync();
                await CaptureAsync(stage, target, dir, "2i-dual-pane-right", window);
                app.Handle(InputAction.SwitchPane);
                ChooseAppMenu(app, "Dois painéis");
                await app.WhenIdleAsync();

                ChooseAppMenu(app, "Densidade");
                await CaptureAsync(stage, target, dir, "3-folder-compact", window);

                // Grade (#29): compacta e confortável, com o foco uma linha abaixo (navegação 2D).
                app.Handle(InputAction.ChangeView);
                // Aviso flutuante (auditoria de UX, P2-1): "Exibição em grade." no canto, com o rodapé da mesma altura.
                await CaptureAsync(stage, target, dir, "3a-status-toast", window);
                app.Handle(InputAction.NavigateDown);
                await CaptureAsync(stage, target, dir, "3b-folder-grid-compact", window);
                ChooseAppMenu(app, "Densidade");
                await CaptureAsync(stage, target, dir, "3c-folder-grid", window);
                // Painel de detalhes na grade (#177): 3c mostra o automático (ao lado onde cabe, fora nos portáteis); 3d,
                // o contrário pelo menu (portátil: a grade perde colunas). Depois volta ao automático.
                ChooseAppMenu(app, "Painel de detalhes");
                await SettleAsync(stage);
                await app.WhenIdleAsync();
                await CaptureAsync(stage, target, dir, "3d-folder-grid-details-toggled", window);
                ChooseAppMenu(app, "Painel de detalhes");
                app.Handle(InputAction.NavigateUp);
                app.Handle(InputAction.ChangeView);

                app.Handle(InputAction.OpenAppMenu);
                await CaptureAsync(stage, target, dir, "4-menu", window);
                // #227: a mesma largura com o foco numa linha de descrição longa (4a) e num bloco (4).
                FocusMenuItem(app, "Dois painéis");
                await CaptureAsync(stage, target, dir, "4a-menu-long-row", window);
                FocusMenuItem(app, "Configurações");
                app.Handle(InputAction.Confirm);
                FocusMenuItem(app, "Ordem");
                await CaptureAsync(stage, target, dir, "4b-settings", window);
                FocusMenuItem(app, "Mira por giroscópio");
                await CaptureAsync(stage, target, dir, "4c-settings-long-row", window);
                CloseModals(app);

                app.Handle(InputAction.Search);
                app.TypeText("relatório");
                await CaptureAsync(stage, target, dir, "5-keyboard", window);
                CloseModals(app);

                // Atalhos (#168): jogos da Steam pelo título e com o ícone que declaram, site comum, .lnk e ícone ausente.
                app.OpenPhysical(shortcuts);
                await app.WhenIdleAsync();
                await FocusAsync(app, stage, "Valheim.url");
                await CaptureAsync(stage, target, dir, "6-shortcuts-list", window);
                app.Handle(InputAction.ChangeView);
                await CaptureAsync(stage, target, dir, "6b-shortcuts-grid", window);
                app.Handle(InputAction.ChangeView);

                // Telas que a auditoria de UX pediu (8*): resultados de busca, pasta vazia, dentro de um compactado,
                // visualização de imagem e a Lixeira (a do runner, real).
                if (WantedGroup("8"))
                {
                    app.OpenPhysical(sample);
                    await app.WhenIdleAsync();
                    app.Handle(InputAction.Search);
                    app.TypeText("foto");
                    app.Handle(InputAction.OpenAppMenu); // Concluir
                    await WaitForAsync(() => app.TopModal is null && app.Browser.Location is Core.Models.SearchLocation);
                    await app.WhenIdleAsync();
                    await CaptureAsync(stage, target, dir, "8a-search-results", window);
                    app.OpenPhysical(Path.Join(sample, "Rascunhos"));
                    await app.WhenIdleAsync();
                    await CaptureAsync(stage, target, dir, "8b-empty-folder", window);
                    app.OpenPhysical(sample);
                    await app.WhenIdleAsync();
                    await FocusAsync(app, stage, "backup-2026-09.zip");
                    app.Handle(InputAction.Confirm);
                    await WaitForAsync(() => app.Browser.Location is Core.Models.ArchiveLocation);
                    await app.WhenIdleAsync();
                    await CaptureAsync(stage, target, dir, "8c-inside-archive", window);
                    app.OpenPhysical(sample);
                    await app.WhenIdleAsync();
                    await FocusAsync(app, stage, "logo.png");
                    app.Handle(InputAction.Confirm);
                    if (await WaitForAsync(() => app.TopModal is Application.State.ImagePreviewModal { IsLoading: false }))
                        await CaptureAsync(stage, target, dir, "8d-image-preview", window);
                    CloseModals(app);
                    // Numa pasta, L1 vai ao caminho: R1 leva ao primeiro atalho e a direita até a Lixeira.
                    app.Handle(InputAction.NextRegion);
                    var bin = app.QuickAccess.ToList().FindIndex(q => q.Label == "Lixeira");
                    for (var i = 0; i < 20 && app.QuickAccessFocus < bin; i++) app.Handle(InputAction.NavigateRight);
                    app.Handle(InputAction.Confirm);
                    await WaitForAsync(() => app.Browser.Location is Core.Models.RecycleBinLocation);
                    await app.WhenIdleAsync();
                    await CaptureAsync(stage, target, dir, "8e-recycle-bin", window);
                    app.GoHome();
                }

                // Tema claro e cor de destaque (#37): pasta, menu e teclado no claro; pasta com outro destaque; volta ao escuro.
                if (WantedGroup("7"))
                {
                    SetTheme(app, ThemeMode.Light, AccentColor.Cyan);
                    app.OpenPhysical(sample);
                    await app.WhenIdleAsync();
                    app.Handle(InputAction.ToggleSelection);
                    app.Handle(InputAction.NavigateDown);
                    await CaptureAsync(stage, target, dir, "7-light-folder", window);
                    app.Handle(InputAction.OpenAppMenu);
                    await CaptureAsync(stage, target, dir, "7b-light-menu", window);
                    CloseModals(app);
                    app.Handle(InputAction.Search);
                    app.TypeText("relatório");
                    await CaptureAsync(stage, target, dir, "7c-light-keyboard", window);
                    CloseModals(app);
                    SetTheme(app, ThemeMode.Light, AccentColor.Magenta);
                    await CaptureAsync(stage, target, dir, "7d-light-accent-magenta", window);
                    SetTheme(app, ThemeMode.Dark, AccentColor.Amber);
                    await CaptureAsync(stage, target, dir, "7e-dark-accent-amber", window);
                    SetTheme(app, ThemeMode.Dark, AccentColor.Cyan);
                    app.Handle(InputAction.ToggleSelection);
                }

                if (WantedGroup("o")) await CaptureOnboardingAsync(app, window, stage, target, dir, sample);
                if (target.Modals && WantedGroup("m")) await CaptureModalsAsync(app, window, stage, target, dir, modals);
            }

            if (Wanted("glyphs")) await RenderGlyphGalleryAsync(stage, layout, window, Path.Join(outputDirectory, "glyphs"));
            if (Wanted("icons")) await RenderIconGalleryAsync(stage, layout, window, Path.Join(outputDirectory, "icons"));
            await File.WriteAllTextAsync(Path.Join(outputDirectory, "report.txt"), Report.ToString());
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            AppLog.Crash(ex, "ScreenRenderer");
            Report.AppendLine("FALHA: " + ex);
            try { await File.WriteAllTextAsync(Path.Join(outputDirectory, "report.txt"), Report.ToString()); } catch (IOException) { }
            Environment.ExitCode = 1;
        }
        finally
        {
            window?.Controller.ReleaseMediaForShutdown();
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            // Mesma saída do app com mídia usada (#224): sem descarregar as DLLs, com o código já definido.
            if (Infrastructure.Media.Playback.WindowsMediaPlayerFactory.PlaybackUsed)
                Infrastructure.Windows.Diagnostics.ProcessTermination.TerminateCurrent((uint)Environment.ExitCode);
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    /// <summary>
    /// Modais (#172), só com ações semânticas: menu de ações (foco inicial e numa ação perigosa), confirmação de
    /// exclusão (fosca e com a reserva sólida de transparência reduzida), resumo da extração, senha no teclado, resultado
    /// com erro, Central de Operações e detalhes, menu do seletor de pasta e Sobre.
    /// </summary>
    private static async Task CaptureModalsAsync(AppController app, MainWindow window, FrameworkElement stage, Target target, string dir, string folder)
    {
        CloseModals(app);
        app.OpenPhysical(folder);
        await app.WhenIdleAsync();

        await FocusAsync(app, stage, "relatório.txt");
        app.Handle(InputAction.OpenContextMenu);
        await app.WhenIdleAsync(); // o menu de arquivo detecta o formato antes de abrir
        await CaptureAsync(stage, target, dir, "m1-menu-actions", window);
        FocusMenuItem(app, "Excluir");
        await CaptureAsync(stage, target, dir, "m1b-menu-destructive-focus", window);
        app.Handle(InputAction.Confirm);
        await CaptureAsync(stage, target, dir, "m2-confirm-delete", window);
        window.SimulateSolidSurfaces(true);
        await CaptureAsync(stage, target, dir, "m2b-confirm-delete-solid", window);
        window.SimulateSolidSurfaces(false);
        CloseModals(app);

        // Compactado com senha: resumo da extração, teclado de senha e o resultado com a senha errada.
        await FocusAsync(app, stage, "protegido.zip");
        app.Handle(InputAction.OpenContextMenu);
        await app.WhenIdleAsync(); // abre em "Extrair para \"protegido\""
        app.Handle(InputAction.Confirm);
        await CaptureAsync(stage, target, dir, "m3-extract-summary", window);
        app.Handle(InputAction.Confirm); // Extrair
        if (await WaitForAsync(() => app.TopModal is Application.State.KeyboardModal))
        {
            var first = app.TopModal;
            app.TypeText("senha errada");
            await CaptureAsync(stage, target, dir, "m4-password", window);
            app.Handle(InputAction.OpenAppMenu); // Concluir: senha errada reabre o teclado com o erro
            if (await WaitForAsync(() => app.TopModal is Application.State.KeyboardModal k && !ReferenceEquals(k, first) && !k.IsBusy))
            {
                await CaptureAsync(stage, target, dir, "m5-password-error", window);
                app.TypeText("certa");
                app.Handle(InputAction.OpenAppMenu);
                if (await WaitForAsync(() => app.TopModal is Application.State.DialogModal))
                    await CaptureAsync(stage, target, dir, "m5b-result", window);
            }
        }
        CloseModals(app);

        // Erro: a pasta sumiu do disco depois de listada.
        await app.WhenIdleAsync();
        var gone = Path.Join(folder, "Pasta removida");
        if (Directory.Exists(gone)) Directory.Delete(gone);
        await FocusAsync(app, stage, "Pasta removida");
        app.Handle(InputAction.Confirm);
        if (await WaitForAsync(() => app.TopModal is Application.State.DialogModal))
            await CaptureAsync(stage, target, dir, "m5c-error", window);
        CloseModals(app);
        Directory.CreateDirectory(gone);
        app.Handle(InputAction.OpenAppMenu);
        FocusMenuItem(app, "Atualizar");
        app.Handle(InputAction.Confirm);
        await app.WhenIdleAsync();

        ChooseAppMenu(app, "Operações");
        await CaptureAsync(stage, target, dir, "m6-operations", window);
        app.Handle(InputAction.Confirm);
        await CaptureAsync(stage, target, dir, "m7-operation-details", window);
        CloseModals(app);

        // Seletor de pasta (Extrair para…): o menu do seletor e depois "Cancelar escolha".
        await FocusAsync(app, stage, "protegido.zip");
        app.Handle(InputAction.OpenContextMenu);
        await app.WhenIdleAsync();
        FocusMenuItem(app, "Extrair para…");
        app.Handle(InputAction.Confirm);
        await app.WhenIdleAsync();
        app.Handle(InputAction.OpenAppMenu);
        await CaptureAsync(stage, target, dir, "m8-picker-menu", window);
        FocusMenuItem(app, "Cancelar escolha");
        app.Handle(InputAction.Confirm);
        CloseModals(app);

        ChooseAppMenu(app, "Sobre");
        await CaptureAsync(stage, target, dir, "m9-about", window);
        CloseModals(app);

        // Celular como controle (#223): QR Code e a permissão no PC, sem rede (canal falso só para as capturas).
        if (Wanted("m10"))
        {
            var phone = new ScreenPhoneLink();
            app.AttachPhoneLink(phone);
            app.BeginPhonePairing();
            await CaptureAsync(stage, target, dir, "m10-phone-pairing", window);
            phone.Raise(new Core.Contracts.PhoneAwaitingConfirmation(phone.Session, "192.168.0.23", "482 913"));
            if (await WaitForAsync(() => app.TopModal is Application.State.DialogModal { IsSensitive: true }))
                await CaptureAsync(stage, target, dir, "m10b-phone-allow", window);
            CloseModals(app);
        }

        // PDF de verdade desenhado pelo Windows.Data.Pdf (#59): página 2 de 2.
        if (Wanted("mp"))
        {
            await FocusAsync(app, stage, "manual.pdf");
            app.Handle(InputAction.Confirm);
            if (await WaitForAsync(() => app.TopModal is Application.State.PdfPreviewModal { IsLoading: false }))
            {
                app.Handle(InputAction.NextRegion);
                await WaitForAsync(() => app.TopModal is Application.State.PdfPreviewModal { IsLoading: false });
                await CaptureAsync(stage, target, dir, "mp-pdf-preview", window);
            }
            CloseModals(app);
        }

        // Áudio de verdade no MediaPlayer do Windows (#60): pausado para a captura não depender do tempo.
        if (Wanted("ma"))
        {
            await FocusAsync(app, stage, "tom.wav");
            app.Handle(InputAction.Confirm);
            if (await WaitForAsync(() => app.TopModal is Application.State.AudioPreviewModal a && a.Status.State != Core.Contracts.MediaPlaybackState.Opening))
            {
                if (app.TopModal is Application.State.AudioPreviewModal { Status.State: Core.Contracts.MediaPlaybackState.Playing }) app.Handle(InputAction.Confirm);
                app.Handle(InputAction.NavigateRight);
                await CaptureAsync(stage, target, dir, "ma-audio-preview", window);
            }
            CloseModals(app);
        }

        // Edição leve de texto (#62): a visualização em modo de edição, com a linha em foco destacada.
        if (Wanted("mt"))
        {
            await FocusAsync(app, stage, "relatório.txt");
            app.Handle(InputAction.Confirm);
            if (await WaitForAsync(() => app.TopModal is Application.State.TextPreviewModal { IsLoading: false }))
            {
                app.Handle(InputAction.OpenContextMenu);
                if (await WaitForAsync(() => app.TopModal is Application.State.TextPreviewModal { Editor: not null }))
                    await CaptureAsync(stage, target, dir, "mt-text-edit", window);
            }
            CloseModals(app);
        }

        // Vídeo em tela cheia (#61, #170): AVI sem compressão gerado aqui; pausado e com um destino de busca em preparo.
        // Sem dispositivo de som no runner o Windows pode recusar: a captura mostra então a mensagem de erro sobre o vídeo.
        if (Wanted("mv"))
        {
            await FocusAsync(app, stage, "clipe.avi");
            app.Handle(InputAction.Confirm);
            if (await WaitForAsync(() => app.TopModal is Application.State.VideoPlayerModal v && v.Status.State != Core.Contracts.MediaPlaybackState.Opening || app.TopModal is Application.State.VideoPlayerModal { Error: not null }))
            {
                if (app.TopModal is Application.State.VideoPlayerModal { Status.State: Core.Contracts.MediaPlaybackState.Playing }) app.Handle(InputAction.Confirm);
                app.Handle(InputAction.NavigateRight);
                await CaptureAsync(stage, target, dir, "mv-video-player", window);
            }
            CloseModals(app);
        }
        app.GoHome();
    }

    /// <summary>
    /// Boas-vindas e tutorial guiado (#231), só com ações semânticas: boas-vindas, controles, ajustes básicos e o convite; o
    /// tutorial começa numa pasta de exemplo, anda de verdade pelos passos (foco, abrir, voltar, Ações) e é pulado no fim.
    /// </summary>
    private static async Task CaptureOnboardingAsync(AppController app, MainWindow window, FrameworkElement stage, Target target, string dir, string folder)
    {
        CloseModals(app);
        app.OpenPhysical(folder);
        await app.WhenIdleAsync();
        app.ShowOnboarding();
        await CaptureAsync(stage, target, dir, "o1-onboarding-welcome", window);
        app.Handle(InputAction.NextRegion);
        await CaptureAsync(stage, target, dir, "o2-onboarding-controls", window);
        app.Handle(InputAction.NextRegion);
        app.Handle(InputAction.NavigateDown); // foco em "Legendas", com a descrição
        await CaptureAsync(stage, target, dir, "o3-onboarding-basics", window);
        app.Handle(InputAction.NextRegion);
        app.Handle(InputAction.NextRegion);
        await CaptureAsync(stage, target, dir, "o4-onboarding-tutorial", window);
        app.Handle(InputAction.Confirm); // Começar tutorial (no início)
        app.OpenPhysical(folder); // a pasta de exemplo, para as capturas não mostrarem pastas do runner
        await app.WhenIdleAsync();
        await CaptureAsync(stage, target, dir, "o5-tutorial-move", window);
        app.Handle(InputAction.NavigateDown); // 1: mover o foco
        await FocusAsync(app, stage, "Fotos da viagem");
        app.Handle(InputAction.Confirm); // 2: abrir uma pasta
        await app.WhenIdleAsync();
        app.Handle(InputAction.Back); // 3: voltar
        await app.WhenIdleAsync();
        app.Handle(InputAction.OpenContextMenu); // 4: Ações…
        await app.WhenIdleAsync();
        await CaptureAsync(stage, target, dir, "o6-tutorial-actions-open", window);
        app.Handle(InputAction.Back); // …e fechar
        await CaptureAsync(stage, target, dir, "o7-tutorial-top-bar", window);
        app.SkipTutorial();
        CloseModals(app);
    }

    /// <summary>Move o foco do menu aberto até o item que começa com <paramref name="prefix"/> (só setas).</summary>
    private static void FocusMenuItem(AppController app, string prefix)
    {
        if (app.TopModal is not Application.State.MenuModal menu) return;
        var index = menu.Items.ToList().FindIndex(i => i.Label.StartsWith(prefix, StringComparison.Ordinal));
        if (index >= 0) StepTo(app, menu, index);
    }

    /// <summary>
    /// Só setas, como no controle: um bloco de uma grade descendo até ela e depois por linha e coluna; um item da lista
    /// descendo (a lista dá a volta e passa pelas grades).
    /// </summary>
    private static void StepTo(AppController app, Application.State.MenuModal menu, int index)
    {
        if (menu.GridOf(index) is { } grid)
        {
            for (var guard = 0; !grid.Contains(menu.FocusIndex) && guard < 500; guard++) app.Handle(InputAction.NavigateDown);
            while (grid.Contains(menu.FocusIndex) && grid.Row(menu.FocusIndex) > grid.Row(index)) app.Handle(InputAction.NavigateUp);
            while (grid.Contains(menu.FocusIndex) && grid.Row(menu.FocusIndex) < grid.Row(index)) app.Handle(InputAction.NavigateDown);
            for (var guard = 0; menu.FocusIndex != index && guard < 8; guard++)
                app.Handle(menu.FocusIndex > index ? InputAction.NavigateLeft : InputAction.NavigateRight);
            return;
        }
        for (var guard = 0; menu.FocusIndex != index && guard < 500; guard++) app.Handle(InputAction.NavigateDown);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++) await Task.Delay(100);
        return condition();
    }

    /// <summary>Pasta dos modais: um texto e um ZIP com senha (ZipCrypto, 389 bytes; o mesmo dos testes, senha "senha-certa").</summary>
    private static string CreateModalFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Join(folder, "relatório.txt"), "Relatório de exemplo.");
        Directory.CreateDirectory(Path.Join(folder, "Pasta removida"));
        File.WriteAllBytes(Path.Join(folder, "protegido.zip"), Convert.FromBase64String(ProtectedZip));
        File.WriteAllBytes(Path.Join(folder, "manual.pdf"), SamplePdf());
        File.WriteAllBytes(Path.Join(folder, "tom.wav"), SampleWav(seconds: 30));
        File.WriteAllBytes(Path.Join(folder, "clipe.avi"), SampleAvi(160, 90, frames: 40, fps: 2));
        return folder;
    }

    /// <summary>AVI sem compressão (RGB 24 bits), só vídeo: faixas de cor que mudam a cada quadro.</summary>
    private static byte[] SampleAvi(int width, int height, int frames, int fps)
    {
        var stride = ((width * 3) + 3) & ~3;
        var frameBytes = stride * height;
        using var memory = new MemoryStream();
        using var w = new BinaryWriter(memory);
        void Fourcc(string code) => w.Write(System.Text.Encoding.ASCII.GetBytes(code));
        long Begin(string code) { Fourcc(code); w.Write(0); return memory.Position; }
        void End(long start)
        {
            var end = memory.Position;
            memory.Position = start - 4;
            w.Write((int)(end - start));
            memory.Position = end;
        }

        var riff = Begin("RIFF");
        Fourcc("AVI ");
        var hdrl = Begin("LIST");
        Fourcc("hdrl");
        var avih = Begin("avih");
        w.Write(1_000_000 / fps); w.Write(frameBytes * fps); w.Write(0); w.Write(0x10); w.Write(frames); w.Write(0); w.Write(1);
        w.Write(frameBytes); w.Write(width); w.Write(height); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        End(avih);
        var strl = Begin("LIST");
        Fourcc("strl");
        var strh = Begin("strh");
        Fourcc("vids"); Fourcc("DIB "); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0); w.Write(1); w.Write(fps); w.Write(0);
        w.Write(frames); w.Write(frameBytes); w.Write(-1); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
        End(strh);
        var strf = Begin("strf");
        w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24); w.Write(0); w.Write(frameBytes); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        End(strf);
        End(strl);
        End(hdrl);
        var movi = Begin("LIST");
        var moviStart = memory.Position;
        Fourcc("movi");
        var offsets = new List<int>();
        var frame = new byte[frameBytes];
        for (var f = 0; f < frames; f++)
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = (y * stride) + (x * 3);
                    var band = ((x * 4 / width) + f) % 4;
                    (frame[i], frame[i + 1], frame[i + 2]) = band switch { 0 => ((byte)0xFF, (byte)0xC7, (byte)0x11), 1 => ((byte)0x1A, (byte)0x10, (byte)0x06), 2 => ((byte)0x4E, (byte)0xC1, (byte)0xF2), _ => ((byte)0xFC, (byte)0xF8, (byte)0xF5) };
                }
            offsets.Add((int)(memory.Position - moviStart));
            Fourcc("00db");
            w.Write(frameBytes);
            w.Write(frame);
        }
        End(movi);
        var idx = Begin("idx1");
        foreach (var offset in offsets)
        {
            Fourcc("00db"); w.Write(0x10); w.Write(offset); w.Write(frameBytes);
        }
        End(idx);
        End(riff);
        w.Flush();
        return memory.ToArray();
    }

    /// <summary>WAV PCM 16 bits mono 8 kHz, silêncio (a captura não toca som no runner).</summary>
    private static byte[] SampleWav(int seconds)
    {
        const int rate = 8000;
        var bytes = rate * seconds * 2;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        writer.Write(new byte[bytes]);
        writer.Flush();
        return memory.ToArray();
    }

    /// <summary>PDF de duas páginas A4 com texto (Helvetica, fonte padrão de todo leitor) e um retângulo colorido.</summary>
    private static byte[] SamplePdf()
    {
        static string Page(string title, string body, string color) =>
            $"BT /F1 36 Tf 72 740 Td ({title}) Tj ET BT /F1 16 Tf 72 700 Td ({body}) Tj ET {color} rg 72 360 451 300 re f";
        string[] contents = [Page("ControlFS", "Pagina 1 de 2", "0 0.6 0.8"), Page("Manual", "Pagina 2 de 2: desenhada pelo Windows.Data.Pdf", "0.9 0.4 0.1")];
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R 6 0 R] /Count 2 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        foreach (var content in contents)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {objects.Count + 2} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private const string ProtectedZip =
        "UEsDBAoACQAAALqIOl0bTTExIAAAABQAAAALAAAAc2VncmVkby50eHTxaJajxtfA2YEoujO4doqsCyhhAsJ0bWsM2cfhseKhz1BLBwgbTTExIAAAABQAAABQSwMECgAAAAAAuog6XQAAAAAAAAAAAAAAAAUAAABkb2NzL1BLAwQKAAkAAAC6iDpdEKquTBEAAAAFAAAADQAAAGRvY3Mvbm90YS50eHSx7BFtMkmzDm5HlScqgRPgVlBLBwgQqq5MEQAAAAUAAABQSwECHgMKAAkAAAC6iDpdG00xMSAAAAAUAAAACwAAAAAAAAABAAAApIEAAAAAc2VncmVkby50eHRQSwECHgMKAAAAAAC6iDpdAAAAAAAAAAAAAAAABQAAAAAAAAAAABAA7UFZAAAAZG9jcy9QSwECHgMKAAkAAAC6iDpdEKquTBEAAAAFAAAADQAAAAAAAAABAAAApIF8AAAAZG9jcy9ub3RhLnR4dFBLBQYAAAAAAwADAKcAAADIAAAAAAA=";

    /// <summary>Galeria dos ícones das ações (#172): cada símbolo com o nome, para conferir a fonte de ícones do runner.</summary>
    private static async Task RenderIconGalleryAsync(Grid stage, Grid layout, MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        window.PinLayout(LayoutProfile.Default, new Windows.Foundation.Size(1920, 1080), 1);
        var icons = Enum.GetValues<ActionIcon>().Where(i => i != ActionIcon.None).ToArray();
        const int columns = 6;
        var grid = new Grid { Background = Theme.Background, Padding = new Thickness(24), ColumnSpacing = 24, RowSpacing = 14 };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        for (var r = 0; r < (icons.Length + columns - 1) / columns; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < icons.Length; i++)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            cell.Children.Add(new TextBlock { Text = ActionIcons.Glyph(icons[i]), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 24, Foreground = Theme.Accent, Width = 32 });
            cell.Children.Add(new TextBlock { Text = icons[i].ToString() + (ActionIcons.IsDestructive(icons[i]) ? " ⚠" : string.Empty), FontSize = 16, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center });
            Add(grid, cell, i / columns, i % columns);
        }
        stage.Children.Clear();
        stage.Children.Add(grid);
        grid.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = (int)Math.Ceiling(grid.DesiredSize.Width);
        var height = (int)Math.Ceiling(grid.DesiredSize.Height);
        stage.Width = width;
        stage.Height = height;
        await SettleAsync(stage);
        await SaveAsync(stage, width, height, Path.Join(directory, "action-icons.png"));
        Report.AppendLine($"action-icons: {icons.Length} ícones");
        stage.Children.Clear();
        stage.Children.Add(layout);
    }

    /// <summary>Abre o menu do app e escolhe o item que começa com <paramref name="prefix"/>, só com ações semânticas.</summary>
    /// <summary>Tema e destaque pelos próprios itens de Configurações (cada escolha avança uma opção).</summary>
    private static void SetTheme(AppController app, ThemeMode theme, AccentColor accent)
    {
        for (var i = 0; i < 4 && app.Settings.Theme != theme; i++) ChooseAppMenu(app, "Tema");
        for (var i = 0; i < 8 && app.Settings.Accent != accent; i++) ChooseAppMenu(app, "Cor de destaque");
    }

    private static void ChooseAppMenu(AppController app, string prefix)
    {
        app.Handle(InputAction.OpenAppMenu);
        ChooseOpenMenu(app, prefix);
    }

    /// <summary>Escolhe o item que começa com <paramref name="prefix"/> no menu aberto (fecha os menus se não houver).</summary>
    private static void ChooseOpenMenu(AppController app, string prefix)
    {
        if (app.TopModal is not Application.State.MenuModal menu) return;
        var index = menu.Items.ToList().FindIndex(i => i.Label.StartsWith(prefix, StringComparison.Ordinal));
        if (index < 0 && menu.Title == "Menu" && menu.Items.ToList().FindIndex(i => i.Label.StartsWith("Configurações", StringComparison.Ordinal)) is var settings and >= 0)
        {
            // Ajustes moram em Menu → Configurações (#193); alternar um ajuste mantém o menu aberto, então ele é fechado.
            StepTo(app, menu, settings);
            app.Handle(InputAction.Confirm);
            ChooseOpenMenu(app, prefix);
            if (app.TopModal is Application.State.MenuModal { Title: "Configurações" }) app.Handle(InputAction.Back);
            return;
        }
        if (index < 0)
        {
            CloseModals(app);
            return;
        }
        StepTo(app, menu, index);
        app.Handle(InputAction.Confirm);
    }

    /// <summary>Foco no item pelo nome (setas, como no controle) e espera o painel de detalhes terminar de ler/medir.</summary>
    private static async Task FocusAsync(AppController app, FrameworkElement stage, string name)
    {
        var list = app.ActivePane.List;
        var target = list.Items.ToList().FindIndex(i => i.Name == name);
        if (target < 0) return;
        while (list.FocusIndex != target) app.Handle(list.FocusIndex < target ? InputAction.NavigateDown : InputAction.NavigateUp);
        await SettleAsync(stage); // o painel é publicado no AppController depois do Render
        await app.WhenIdleAsync();
    }

    /// <summary>L1 e direita até o atalho <paramref name="label"/> da barra superior, e Confirmar.</summary>
    private static void ChooseQuickAccess(AppController app, string label)
    {
        app.Handle(InputAction.PreviousRegion);
        var index = app.QuickAccess.ToList().FindIndex(q => q.Label == label);
        if (index < 0 || app.FocusRegion != Application.State.PaneRegion.QuickAccess)
        {
            app.Handle(InputAction.Back);
            return;
        }
        while (app.QuickAccessFocus < index) app.Handle(InputAction.NavigateRight);
        app.Handle(InputAction.Confirm);
    }

    private static void CloseModals(AppController app)
    {
        for (var i = 0; i < 10 && app.TopModal is not null; i++) app.Handle(InputAction.Back);
    }

    /// <summary>Pasta de exemplo com nomes realistas: acentos, nome longo, compactado, subpastas, tamanhos variados.</summary>
    private static string CreateSampleFolder(string root)
    {
        var folder = Path.Join(root, "Projetos 2026", "Relatórios trimestrais", "Apresentações do cliente");
        Directory.CreateDirectory(folder);
        foreach (var sub in new[] { "Fotos da viagem", "Contratos assinados", "Rascunhos" }) Directory.CreateDirectory(Path.Join(folder, sub));
        string[] files =
        [
            "Orçamento final.xlsx", "Relatório anual de desempenho financeiro e operacional da empresa — versão revisada para a diretoria.pdf",
            "backup-2026-09.zip", "notas.txt", "Apresentação.pptx", "foto_0001.jpg", "foto_0002.jpg", "foto_0003.jpg", "música tema.mp3",
            "setup.exe", "planilha de custos (cópia).xlsx", "leia-me.md", "vídeo institucional.mp4", "logo.png", "contrato.docx",
            "dados.csv", "arquivo sem extensão", "ícones.7z", "roteiro.docx", "cronograma.xlsx",
        ];
        var size = 700;
        var now = DateTime.Now;
        for (var i = 0; i < files.Length; i++)
        {
            var file = Path.Join(folder, files[i]);
            File.WriteAllBytes(file, new byte[size]);
            size = size * 7 % 900_000 + 1_000;
            // Datas variadas para as datas amigáveis da lista: hoje, ontem e dias anteriores.
            File.SetLastWriteTime(file, (i % 3) switch { 0 => now.AddMinutes(-7 * i), 1 => now.Date.AddDays(-1).AddHours(18).AddMinutes(i), _ => now.Date.AddDays(-2 - i).AddHours(9).AddMinutes(45) });
        }
        // Uma imagem e um compactado de verdade para o painel de detalhes (miniatura, formato e arquivos contados).
        var logo = Path.Join(AppContext.BaseDirectory, "controlfs-logo.png");
        if (File.Exists(logo)) File.Copy(logo, Path.Join(folder, "logo.png"), overwrite: true);
        var zip = Path.Join(folder, "backup-2026-09.zip");
        File.Delete(zip);
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var name in new[] { "contrato.docx", "notas.txt", "fotos/praia.jpg", "fotos/montanha.jpg" })
                using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write(new string('x', 4096));
        File.SetLastWriteTime(zip, DateTime.Now.Date.AddDays(-1).AddHours(18).AddMinutes(5));
        return folder;
    }

    /// <summary>
    /// Área de trabalho de exemplo com atalhos: dois "jogos da Steam" (.url steam://) com ícones diferentes de um .dll do
    /// Windows, um com ícone ausente (reserva), um site (https) e um .lnk. Nada é aberto; a Steam não precisa existir.
    /// </summary>
    private static string CreateShortcutFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        var imageres = Path.Join(Environment.SystemDirectory, "imageres.dll");
        void Url(string name, string url, string? icon, int index) => File.WriteAllText(Path.Join(folder, name),
            $"[InternetShortcut]\r\nURL={url}\r\n" + (icon is null ? string.Empty : $"IconFile={icon}\r\nIconIndex={index}\r\n"));
        Url("Valheim.url", "steam://rungameid/892970", imageres, 109);
        Url("Dead Space.url", "steam://rungameid/1693980", imageres, 76);
        Url("Aniimo.url", "steam://rungameid/2", Path.Join(folder, "sem-icone.ico"), 0);
        Url("ControlFS no GitHub.url", "https://github.com/nextestudios/ControlFS", null, 0);
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is not null && Activator.CreateInstance(type) is { } shell)
            {
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                var flags = System.Reflection.BindingFlags.InvokeMethod;
                var link = type.InvokeMember("CreateShortcut", flags, null, shell, [Path.Join(folder, "Prompt de comando.lnk")], culture)!;
                link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, [Path.Join(Environment.SystemDirectory, "cmd.exe")], culture);
                link.GetType().InvokeMember("Save", flags, null, link, null, culture);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Reflection.TargetInvocationException)
        {
            Report.AppendLine("  (sem WScript.Shell: .lnk de exemplo não criado)");
        }
        return folder;
    }

    private static async Task CaptureAsync(FrameworkElement stage, Target target, string directory, string name, MainWindow window)
    {
        if (!Wanted(name)) return;
        await SettleAsync(stage);
        var file = Path.Join(directory, name + ".png");
        await SaveAsync(stage, target.Width, target.Height, file);
        Report.AppendLine($"  {name}: {window.DescribeFit()}");
    }

    /// <summary>
    /// Deixa o layout, a virtualização da lista e os ícones do sistema (assíncronos) assentarem: pelo menos dois quadros
    /// desenhados e nenhum ícone a caminho (limite de ~3 s), em vez de esperas fixas.
    /// </summary>
    private static async Task SettleAsync(FrameworkElement element)
    {
        for (var i = 0; i < 60; i++)
        {
            element.UpdateLayout();
            await NextFrameAsync();
            if (i >= 1 && IconLoader.Loading == 0) break;
            await Task.Delay(50);
        }
        element.UpdateLayout();
        await NextFrameAsync();
    }

    /// <summary>Próximo quadro desenhado (ou 100 ms, se a janela não estiver desenhando).</summary>
    private static async Task NextFrameAsync()
    {
        var frame = new TaskCompletionSource();
        void OnRendering(object? sender, object e) => frame.TrySetResult();
        CompositionTarget.Rendering += OnRendering;
        try
        {
            await Task.WhenAny(frame.Task, Task.Delay(100));
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private static async Task SaveAsync(UIElement element, int width, int height, string file)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element, width, height);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        await using var stream = File.Create(file);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }

    // ---------- Galeria de glifos (#33) ----------

    private static readonly ControllerFamily[] Families = [ControllerFamily.Xbox, ControllerFamily.PlayStation, ControllerFamily.Nintendo, ControllerFamily.Generic];

    /// <summary>
    /// Uma linha por botão, uma coluna por família; cada célula é o glifo no tamanho do rodapé ao lado de um texto
    /// curto (alinhamento com o texto, como no rodapé). Estreita o bastante para caber em 4096 px a 300%, o limite do
    /// RenderTargetBitmap. Escuro e claro, a 100%, 200% e 300%.
    /// </summary>
    private static async Task RenderGlyphGalleryAsync(Grid stage, Grid layout, MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        window.PinLayout(LayoutProfile.Default, new Windows.Foundation.Size(1920, 1080), 1);
        stage.Children.Clear();
        foreach (var dark in new[] { true, false })
        {
            foreach (var scale in new[] { 1, 2, 3 })
            {
                var gallery = BuildGallery(dark);
                gallery.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
                gallery.HorizontalAlignment = HorizontalAlignment.Left;
                gallery.VerticalAlignment = VerticalAlignment.Top;
                stage.Children.Clear();
                stage.Children.Add(gallery);
                gallery.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                var width = (int)Math.Ceiling(gallery.DesiredSize.Width * scale);
                var height = (int)Math.Ceiling(gallery.DesiredSize.Height * scale);
                stage.Width = width;
                stage.Height = height;
                stage.Background = gallery.Background;
                await SettleAsync(stage);
                await SaveAsync(stage, width, height, Path.Join(directory, $"glyphs-{(dark ? "dark" : "light")}-{scale * 100}.png"));
                Report.AppendLine($"glyphs-{(dark ? "dark" : "light")}-{scale * 100}: {width}x{height}");
            }
        }
        stage.Children.Clear();
        stage.Children.Add(layout);
    }

    private static Grid BuildGallery(bool dark)
    {
        var background = dark ? Theme.Background : new SolidColorBrush(ColorHelper.FromArgb(255, 0xF4, 0xF5, 0xF7));
        var text = dark ? Theme.Text : new SolidColorBrush(ColorHelper.FromArgb(255, 0x1B, 0x1D, 0x21));
        var muted = dark ? Theme.TextMuted : new SolidColorBrush(ColorHelper.FromArgb(255, 0x55, 0x5B, 0x66));
        var palette = dark ? GlyphPalette.Dark : GlyphPalette.Light;
        var glyphHeight = Math.Round(Theme.FontCaption * 1.6); // o mesmo tamanho do rodapé
        var grid = new Grid { Background = background, Padding = new Thickness(16), ColumnSpacing = 24, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in Families) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var buttons = Enum.GetValues<ControllerButton>();
        for (var r = 0; r <= buttons.Length; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(grid, new TextBlock { Text = dark ? "Tema escuro" : "Tema claro", FontSize = Theme.FontCaption, FontWeight = FontWeights.SemiBold, Foreground = text }, 0, 0);
        for (var c = 0; c < Families.Length; c++)
            Add(grid, new TextBlock { Text = Families[c].ToString(), FontSize = Theme.FontCaption, FontWeight = FontWeights.SemiBold, Foreground = text }, 0, c + 1);
        for (var r = 0; r < buttons.Length; r++)
        {
            Add(grid, new TextBlock { Text = buttons[r].ToString(), FontSize = Theme.FontCaption, Foreground = muted, VerticalAlignment = VerticalAlignment.Center }, r + 1, 0);
            for (var c = 0; c < Families.Length; c++)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
                chip.Children.Add(ControllerGlyphs.Create(buttons[r], Families[c], glyphHeight, palette));
                chip.Children.Add(new TextBlock { Text = "Abrir", FontSize = Theme.FontCaption, Foreground = muted, VerticalAlignment = VerticalAlignment.Center });
                Add(grid, chip, r + 1, c + 1);
            }
        }
        return grid;
    }

    private static void Add(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>Canal de celular das capturas: devolve um pareamento fixo e nunca abre porta.</summary>
    private sealed class ScreenPhoneLink : Core.Contracts.IPhoneLink
    {
        public int Session { get; private set; }

        public Core.Contracts.PhonePairing Start(int addressIndex = 0) =>
            new(++Session, "http://192.168.0.10:53187/q2VvJb7t0nP5RzLx3Ya1Hg#k=Z3JhbmRlLWV4ZW1wbG8tZGUtY2hhdmUtcGFyYS1jYXB0dXJh", "192.168.0.10", 0, 2,
                Core.Remote.PhoneSession.PairingLifetime);

        public bool Confirm(int session) => false;
        public void Stop(int session, Core.Remote.PhoneEndReason reason) { }
        public void PublishUi(bool keyboard, bool locked) { }
        public void Raise(Core.Contracts.PhoneLinkEvent e) => Event?.Invoke(e);
        public event Action<Core.Contracts.PhoneLinkEvent>? Event;
        public void Dispose() { }
    }
}
