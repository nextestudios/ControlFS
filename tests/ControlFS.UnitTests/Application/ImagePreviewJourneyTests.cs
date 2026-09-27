using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class ImagePreviewJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Decodificador falso: registra o que foi pedido (o real usa o WIC, só no Windows).</summary>
    private sealed class FakeDecoder : IImageDecoder
    {
        public List<string> Decoded { get; } = [];

        public Task<PreviewImage> DecodeAsync(string path, int maxSide, CancellationToken cancellationToken)
        {
            Decoded.Add(Path.GetFileName(path));
            return Task.FromResult(new PreviewImage(2, 2, new byte[16]));
        }
    }

    [Fact]
    public void South_previews_images_in_folder_order_with_zoom_pan_and_refuses_bombs_without_decoding() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("a.png"), ImageFixtures.Png(640, 480));
        File.WriteAllBytes(_tmp.Sub("b-bomba.png"), ImageFixtures.Png(100_000, 100_000));
        File.WriteAllText(_tmp.Sub("c.txt"), "não é imagem");
        File.WriteAllBytes(_tmp.Sub("d.jpg"), ImageFixtures.Jpeg(4032, 3024));
        var decoder = new FakeDecoder();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), imageDecoder: decoder);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.png");
        d.Press(InputAction.Confirm);
        await d.Idle();

        var preview = Assert.IsType<ImagePreviewModal>(app.TopModal);
        Assert.Equal((640, 480), (preview.Info!.Width, preview.Info.Height));
        Assert.NotNull(preview.Image);

        // Zoom com RT; o direcional move a área visível sem sair da imagem; LT/Confirmar voltam a ajustar.
        d.Press(InputAction.PageDown);
        d.Press(InputAction.PageDown);
        Assert.Equal(2, preview.Zoom);
        for (var i = 0; i < 10; i++) d.Press(InputAction.NavigateRight);
        Assert.Equal(0.75, preview.CenterX, 3); // borda direita: metade da área visível (1/2 ÷ 2)
        Assert.Equal("a.png", preview.Current.Name); // com zoom, Direita move, não troca de imagem
        d.Press(InputAction.Confirm);
        Assert.Equal((1d, 0.5), (preview.Zoom, preview.CenterX));

        // Próxima na ordem da pasta, pulando o que não é imagem; a bomba é recusada antes do decodificador.
        d.Press(InputAction.NavigateRight);
        await d.Idle();
        Assert.Equal("b-bomba.png", preview.Current.Name);
        Assert.Null(preview.Image);
        Assert.Contains("Resolução alta demais", preview.Error, StringComparison.Ordinal);
        d.Press(InputAction.NextRegion);
        await d.Idle();
        Assert.Equal("d.jpg", preview.Current.Name);
        Assert.Equal(["a.png", "d.jpg"], decoder.Decoded);

        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Equal("d.jpg", app.Browser.List.Focused?.Name);
    });

    [Fact]
    public void Hints_fade_after_inactivity_and_any_input_reveals_them_while_still_acting() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("a.png"), ImageFixtures.Png(64, 64));
        File.WriteAllBytes(_tmp.Sub("b.png"), ImageFixtures.Png(64, 64));
        var now = TimeSpan.Zero;
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), imageDecoder: new FakeDecoder()) { Clock = () => now };
        app.Start();
        app.SetActiveController(ControllerFamily.PlayStation);
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.png");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var preview = Assert.IsType<ImagePreviewModal>(app.TopModal);

        // Ao abrir: só o que funciona (primeira imagem: sem "Anterior"; sem zoom: sem "Ajustar à tela"), com os botões da família em uso.
        Assert.Equal([InputAction.NextRegion, InputAction.PageDown, InputAction.Back], app.Prompts.Select(p => p.Action));
        Assert.All(app.Prompts, p => Assert.Equal(ControllerFamily.PlayStation, p.Family));
        Assert.False(preview.HintsFaded);

        now += AppController.PreviewHintsFadeAfter - TimeSpan.FromMilliseconds(1);
        app.TickControllers();
        Assert.False(preview.HintsFaded);
        now += TimeSpan.FromMilliseconds(1);
        app.TickControllers();
        Assert.True(preview.HintsFaded);

        // Qualquer entrada mostra as legendas de novo e ainda faz o que o botão faz (nunca é "engolida").
        d.Press(InputAction.NextRegion);
        await d.Idle();
        Assert.False(preview.HintsFaded);
        Assert.Equal("b.png", preview.Current.Name);
        Assert.Equal([InputAction.PreviousRegion, InputAction.PageDown, InputAction.Back], app.Prompts.Select(p => p.Action)); // última imagem
        now += AppController.PreviewHintsFadeAfter;
        app.TickControllers();
        Assert.True(preview.HintsFaded);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Equal("b.png", app.Browser.List.Focused?.Name);
    });
}
