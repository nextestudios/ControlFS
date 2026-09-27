using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Visualização de PDF (#59) com um renderizador falso (o real usa o Windows.Data.Pdf: WindowsIntegrationTests).</summary>
public class PdfPreviewJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static readonly byte[] PdfBytes = "%PDF-1.7\n%âãÏÓ\n"u8.ToArray();

    private sealed class FakeRenderer(int pages, string? password = null, bool hang = false) : IPdfRenderer
    {
        public List<string> Opened { get; } = [];
        public List<int> Rendered { get; } = [];
        public int Disposed { get; private set; }

        public async Task<IPdfDocument> OpenAsync(string path, string? given, CancellationToken cancellationToken)
        {
            Opened.Add(Path.GetFileName(path));
            if (hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (password is not null && given != password) throw new PdfPasswordException(given is not null);
            return new Document(this, pages);
        }

        private sealed class Document(FakeRenderer owner, int pages) : IPdfDocument
        {
            public int PageCount => pages;

            public Task<PreviewImage> RenderPageAsync(int index, int maxSide, CancellationToken cancellationToken)
            {
                owner.Rendered.Add(index);
                return Task.FromResult(new PreviewImage(2, 3, new byte[24]));
            }

            public void Dispose() => owner.Disposed++;
        }
    }

    private (AppController App, Driver Driver) Boot(FakeRenderer renderer, PreviewLimits? limits = null)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService()) { PdfRenderer = renderer };
        if (limits is not null) app.PreviewLimits = limits;
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        return (app, d);
    }

    [Fact]
    public void South_opens_a_pdf_pages_with_the_controller_zooms_and_closing_releases_it_while_fake_pdfs_never_reach_the_renderer() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("manual.pdf"), PdfBytes);
        File.WriteAllBytes(_tmp.Sub("virus.pdf"), [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]); // executável renomeado
        var renderer = new FakeRenderer(pages: 3);
        var (app, d) = Boot(renderer);
        await d.FocusItem("manual.pdf");
        d.Press(InputAction.Confirm);
        await d.Idle();

        var pdf = Assert.IsType<PdfPreviewModal>(app.TopModal);
        Assert.Equal((3, 0), (pdf.PageCount, pdf.PageIndex));
        Assert.NotNull(pdf.Page);
        Assert.Equal([InputAction.NextRegion, InputAction.PageDown, InputAction.Back], app.Prompts.Select(p => p.Action));

        d.Press(InputAction.NavigateRight);
        await d.Idle();
        d.Press(InputAction.NextRegion);
        await d.Idle();
        Assert.Equal(2, pdf.PageIndex);
        d.Press(InputAction.NavigateRight); // última: fica, com aviso
        Assert.Equal(2, pdf.PageIndex);
        Assert.Equal("Esta é a última página.", app.StatusMessage);
        Assert.Equal([0, 1, 2], renderer.Rendered);

        // Com zoom, o direcional move a página em vez de trocar; Sul volta a ajustar.
        d.Press(InputAction.PageDown);
        d.Press(InputAction.NavigateLeft);
        Assert.Equal((2, 1.5), (pdf.PageIndex, pdf.Zoom));
        Assert.True(pdf.CenterX < 0.5);
        d.Press(InputAction.Confirm);
        Assert.Equal(1, pdf.Zoom);

        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Equal(1, renderer.Disposed);
        Assert.Equal("manual.pdf", app.Browser.List.Focused?.Name);

        // Conteúdo que não é PDF é recusado antes do renderizador, com o motivo.
        await d.FocusItem("virus.pdf");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var refused = Assert.IsType<PdfPreviewModal>(app.TopModal);
        Assert.Equal("O conteúdo não é um PDF válido.", refused.Error);
        Assert.Equal(["manual.pdf"], renderer.Opened);
        Assert.Equal([InputAction.Back], app.Prompts.Select(p => p.Action));
    });

    [Fact]
    public void Password_protected_pdf_asks_on_the_masked_keyboard_retries_after_a_wrong_password_and_never_keeps_it() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("contrato.pdf"), PdfBytes);
        var renderer = new FakeRenderer(pages: 1, password: "certa");
        var (app, d) = Boot(renderer);
        await d.FocusItem("contrato.pdf");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var pdf = Assert.IsType<PdfPreviewModal>(app.TopModal);
        Assert.True(pdf.NeedsPassword);
        Assert.Equal("Este PDF é protegido por senha.", pdf.Error);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Digitar senha");

        d.Press(InputAction.Confirm);
        var kb = await d.WaitKeyboard();
        Assert.Equal(TextFieldKind.Password, kb.Keyboard.Kind);
        d.TypeOnKeyboard(kb, "errada");
        d.PressKey(kb, KeyKind.Done);
        await d.Idle();
        Assert.Same(pdf, app.TopModal);
        Assert.Equal("Senha incorreta.", pdf.Error);

        d.Press(InputAction.Confirm);
        kb = await d.WaitKeyboard();
        d.TypeOnKeyboard(kb, "certa");
        d.PressKey(kb, KeyKind.Done);
        await d.Idle();
        Assert.False(pdf.NeedsPassword);
        Assert.NotNull(pdf.Page);
        Assert.Equal(1, pdf.PageCount);
        Assert.DoesNotContain(app.Prompts, p => p.Action is InputAction.PreviousRegion or InputAction.NextRegion); // uma página só
    });

    [Fact]
    public void A_pdf_that_hangs_the_renderer_becomes_an_error_within_the_time_limit() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("trava.pdf"), PdfBytes);
        var (app, d) = Boot(new FakeRenderer(pages: 1, hang: true), PreviewLimits.Default with { PdfTimeout = TimeSpan.FromMilliseconds(50) });
        await d.FocusItem("trava.pdf");
        d.Press(InputAction.Confirm);
        await d.Idle();
        var pdf = Assert.IsType<PdfPreviewModal>(app.TopModal);
        Assert.Equal("O PDF demorou demais para abrir ou desenhar esta página.", pdf.Error);
        Assert.False(pdf.IsLoading);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
    });
}
