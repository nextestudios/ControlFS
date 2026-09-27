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

    private sealed record Target(string Name, int Width, int Height, double Scale, double TextScale = 1)
    {
        public double EffectiveWidth => Width / Scale;
        public double EffectiveHeight => Height / Scale;
    }

    /// <summary>Critério de aceite do #36 (720p, 800p, 1080p, 4K) e as escalas que o Windows costuma usar nelas.</summary>
    private static readonly Target[] Targets =
    [
        new("1280x720", 1280, 720, 1),
        new("1280x800", 1280, 800, 1),
        new("1280x800-text150", 1280, 800, 1, TextScale: 1.5),
        new("1920x1080", 1920, 1080, 1),
        new("1920x1080-150", 1920, 1080, 1.5),
        new("3840x2160", 3840, 2160, 1),
        new("3840x2160-200", 3840, 2160, 2),
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
            }

            await RenderGlyphGalleryAsync(stage, layout, window, Path.Join(outputDirectory, "glyphs"));
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
