using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using ControlFS.App.Resources;
using ControlFS.App.Views;
using ControlFS.Application;
using ControlFS.Core.Actions;
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

    /// <summary>Pasta de saída se o app foi aberto com <c>--render-screens &lt;pasta&gt;</c>.</summary>
    public static string? OutputDirectory(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
    }

    public static async Task RunAsync(string outputDirectory)
    {
        var work = Path.Join(Path.GetTempPath(), "ControlFS-render-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var sample = CreateSampleFolder(Path.Join(work, "files"));
            var shortcuts = CreateShortcutFolder(Path.Join(work, "files", "Área de trabalho"));
            var modals = CreateModalFolder(Path.Join(work, "files", "Modais"));
            var window = new MainWindow(Path.Join(work, "data"));
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
            foreach (var target in Targets)
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
                await CaptureAsync(stage, target, dir, "1-home", window);
                app.Handle(InputAction.PreviousRegion); // LB: acesso rápido da barra superior
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
                app.Handle(InputAction.PreviousRegion); // LB: pasta de cima na barra de caminho
                await CaptureAsync(stage, target, dir, "2b-folder-path-bar", window);
                app.Handle(InputAction.Back);

                ChooseAppMenu(app, "Densidade");
                await CaptureAsync(stage, target, dir, "3-folder-compact", window);

                // Grade (#29): compacta e confortável, com o foco uma linha abaixo (navegação 2D).
                app.Handle(InputAction.ChangeView);
                app.Handle(InputAction.NavigateDown);
                await CaptureAsync(stage, target, dir, "3b-folder-grid-compact", window);
                ChooseAppMenu(app, "Densidade");
                await CaptureAsync(stage, target, dir, "3c-folder-grid", window);
                app.Handle(InputAction.NavigateUp);
                app.Handle(InputAction.ChangeView);

                app.Handle(InputAction.OpenAppMenu);
                for (var i = 0; i < 15; i++) app.Handle(InputAction.NavigateDown); // item bem abaixo da dobra
                await CaptureAsync(stage, target, dir, "4-menu", window);
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

                if (target.Modals) await CaptureModalsAsync(app, window, stage, target, dir, modals);
            }

            await RenderGlyphGalleryAsync(stage, layout, window, Path.Join(outputDirectory, "glyphs"));
            await RenderIconGalleryAsync(stage, layout, window, Path.Join(outputDirectory, "icons"));
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
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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
        app.GoHome();
    }

    /// <summary>Move o foco do menu aberto até o item que começa com <paramref name="prefix"/> (só setas).</summary>
    private static void FocusMenuItem(AppController app, string prefix)
    {
        if (app.TopModal is not Application.State.MenuModal menu) return;
        var index = menu.Items.ToList().FindIndex(i => i.Label.StartsWith(prefix, StringComparison.Ordinal));
        if (index < 0) return;
        while (menu.FocusIndex != index) app.Handle(InputAction.NavigateDown);
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
        return folder;
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
    private static void ChooseAppMenu(AppController app, string prefix)
    {
        app.Handle(InputAction.OpenAppMenu);
        if (app.TopModal is not Application.State.MenuModal menu) return;
        var index = menu.Items.ToList().FindIndex(i => i.Label.StartsWith(prefix, StringComparison.Ordinal));
        if (index < 0)
        {
            CloseModals(app);
            return;
        }
        while (menu.FocusIndex != index) app.Handle(InputAction.NavigateDown);
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

    /// <summary>LB e direita até o atalho <paramref name="label"/> da barra superior, e Confirmar.</summary>
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
        await SettleAsync(stage);
        var file = Path.Join(directory, name + ".png");
        await SaveAsync(stage, target.Width, target.Height, file);
        Report.AppendLine($"  {name}: {window.DescribeFit()}");
    }

    /// <summary>Deixa o layout, a virtualização da lista e os ícones do sistema (assíncronos) assentarem.</summary>
    private static async Task SettleAsync(FrameworkElement element)
    {
        for (var i = 0; i < 4; i++)
        {
            element.UpdateLayout();
            await Task.Delay(250);
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
}
