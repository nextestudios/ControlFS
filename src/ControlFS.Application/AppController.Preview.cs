using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Core.Preview;

namespace ControlFS.Application;

public sealed partial class AppController
{
    public PreviewLimits PreviewLimits { get; set; } = PreviewLimits.Default;

    /// <summary>Renderizador de PDF (#59); null: a visualização de PDF não existe nesta compilação.</summary>
    public IPdfRenderer? PdfRenderer { get; init; }

    /// <summary>Tempo sem entrada até as legendas das visualizações com zoom se recolherem (#171).</summary>
    public static readonly TimeSpan PreviewHintsFadeAfter = TimeSpan.FromSeconds(4);

    internal static bool IsPreviewableImage(FileEntry entry) =>
        entry is { Kind: EntryKind.File, FullPath: not null, IsBlocked: false } && ImageHeader.IsImageExtension(entry.Extension);

    private string? ImagePreviewUnavailable => _imageDecoder is null ? "Visualização de imagens indisponível nesta compilação." : null;

    /// <summary>Abre a visualização na imagem focada; Esquerda/Direita percorrem as imagens da pasta na ordem da lista.</summary>
    internal void OpenImagePreview(PaneState pane, FileEntry entry)
    {
        if (_imageDecoder is null) return;
        var images = pane.List.Items.Where(IsPreviewableImage).ToList();
        var index = images.FindIndex(e => e.Id == entry.Id);
        if (index < 0)
        {
            images = [entry];
            index = 0;
        }
        var modal = new ImagePreviewModal(pane, images, index) { LastInput = Clock() };
        PushModal(modal);
        LoadPreviewImage(modal);
    }

    private void LoadPreviewImage(ImagePreviewModal modal)
    {
        modal.Loading?.Cancel();
        modal.Loading?.Dispose();
        var cts = modal.Loading = new CancellationTokenSource();
        var generation = ++modal.Generation;
        modal.Image = null;
        modal.Info = null;
        modal.Error = null;
        modal.IsLoading = true;
        modal.ResetView();
        Track(LoadPreviewImageAsync(modal, modal.Current.FullPath!, generation, cts.Token));
    }

    private async Task LoadPreviewImageAsync(ImagePreviewModal modal, string path, int generation, CancellationToken cancellationToken)
    {
        var limits = PreviewLimits;
        try
        {
            // Cabeçalho e limites antes de decodificar: nada grande demais chega ao decodificador.
            var info = await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
                return ImagePreviewPolicy.Inspect(stream, limits);
            }, cancellationToken);
            var image = await _imageDecoder!.DecodeAsync(path, limits.MaxDecodedSide, cancellationToken);
            if (generation != modal.Generation) return;
            modal.Info = info;
            modal.Image = image;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (PreviewException ex) when (generation == modal.Generation)
        {
            modal.Error = ex.Message;
        }
        catch (Exception ex) when (generation == modal.Generation && ex is IOException or UnauthorizedAccessException)
        {
            modal.Error = "Não foi possível ler o arquivo: " + ErrorText(ex, "Visualização");
        }
        if (generation != modal.Generation) return;
        modal.IsLoading = false;
        RaiseChanged();
    }

    private void HandleImagePreview(ImagePreviewModal modal, InputAction action)
    {
        modal.LastInput = Clock();
        modal.HintsFaded = false;
        var zoomed = modal.ZoomIndex > 0;
        switch (action)
        {
            case InputAction.Back:
                ClosePreview(modal);
                break;
            case InputAction.PreviousRegion:
            case InputAction.NavigateLeft when !zoomed:
                StepImage(modal, -1);
                break;
            case InputAction.NextRegion:
            case InputAction.NavigateRight when !zoomed:
                StepImage(modal, 1);
                break;
            case InputAction.NavigateLeft: modal.Pan(-1, 0); break;
            case InputAction.NavigateRight: modal.Pan(1, 0); break;
            case InputAction.NavigateUp: modal.Pan(0, -1); break;
            case InputAction.NavigateDown: modal.Pan(0, 1); break;
            case InputAction.PageDown: if (modal.Image is not null) modal.ChangeZoom(1); break;
            case InputAction.PageUp: modal.ChangeZoom(-1); break;
            case InputAction.Confirm: modal.ResetView(); break;
        }
    }

    private void StepImage(ImagePreviewModal modal, int delta)
    {
        var target = modal.Index + delta;
        if (target < 0 || target >= modal.Images.Count)
        {
            StatusMessage = delta < 0 ? "Esta é a primeira imagem da pasta." : "Esta é a última imagem da pasta.";
            return;
        }
        modal.Index = target;
        LoadPreviewImage(modal);
    }

    /// <summary>Chamado pelo laço de entrada: recolhe as legendas da imagem/PDF depois de um tempo sem entrada.</summary>
    private void TickPreviews()
    {
        TickMedia();
        if (TopModal is ZoomablePreviewModal { HintsFaded: false } preview && Clock() - preview.LastInput >= PreviewHintsFadeAfter)
        {
            preview.HintsFaded = true;
            RaiseChanged();
        }
    }

    /// <summary>Fecha e deixa o foco da lista na última imagem vista.</summary>
    private void ClosePreview(ImagePreviewModal modal)
    {
        modal.Loading?.Cancel();
        modal.Generation++;
        CloseModal(modal);
        modal.Pane.List.FocusById(modal.Current.Id);
    }

    // ---------- Texto (#58) ----------

    /// <summary>Sul abre direto a visualização de texto: extensões de texto que não executam nada ao abrir no Windows.</summary>
    internal static bool OpensAsText(FileEntry entry) =>
        entry is { Kind: EntryKind.File, FullPath: { } path, IsBlocked: false } && TextPreview.IsTextExtension(entry.Extension) &&
        !ExecutableFiles.IsPotentiallyExecutable(path);

    /// <summary>Visualização somente leitura do início do arquivo, dentro dos limites; binários são recusados com mensagem.</summary>
    internal void OpenTextPreview(PaneState pane, FileEntry entry)
    {
        if (entry.FullPath is not { } path) return;
        var modal = new TextPreviewModal(pane, entry);
        PushModal(modal);
        Track(LoadTextPreviewAsync(modal, path));
    }

    private async Task LoadTextPreviewAsync(TextPreviewModal modal, string path)
    {
        var limits = PreviewLimits;
        try
        {
            modal.Document = await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
                return TextPreview.Read(stream, limits);
            });
        }
        catch (PreviewException ex)
        {
            modal.Error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            modal.Error = "Não foi possível ler o arquivo: " + ErrorText(ex, "Visualização");
        }
        modal.IsLoading = false;
        RaiseChanged();
    }

    /// <summary>Publicado pela tela: quantas linhas cabem (para paginar). Não redesenha.</summary>
    public void ReportTextPreviewPage(int lines)
    {
        if (TopModal is TextPreviewModal modal && lines > 0 && modal.PageLines != lines)
        {
            modal.PageLines = lines;
            modal.ScrollBy(0);
        }
    }

    private void HandleTextPreview(TextPreviewModal modal, InputAction action)
    {
        if (modal.Editor is { } editor)
        {
            HandleTextEditing(modal, editor, action);
            return;
        }
        if (modal.IsOpeningEditor) return;
        var page = Math.Max(1, modal.PageLines - 1); // uma linha de contexto entre as páginas
        switch (action)
        {
            case InputAction.Back:
                CloseModal(modal);
                modal.Pane.List.FocusById(modal.Entry.Id);
                break;
            case InputAction.NavigateUp: modal.ScrollBy(-1); break;
            case InputAction.NavigateDown: modal.ScrollBy(1); break;
            case InputAction.PageUp: modal.ScrollBy(-page); break;
            case InputAction.PageDown: modal.ScrollBy(page); break;
            case InputAction.PreviousRegion: modal.ScrollTo(0); break;
            case InputAction.NextRegion: modal.ScrollTo(int.MaxValue); break;
            case InputAction.NavigateLeft: modal.ShiftColumns(-TextPreviewModal.ColumnStep); break;
            case InputAction.NavigateRight: modal.ShiftColumns(TextPreviewModal.ColumnStep); break;
            case InputAction.Confirm: modal.Monospace = !modal.Monospace; break;
            case InputAction.OpenContextMenu: BeginTextEdit(modal); break;
        }
    }

    // ---------- PDF (#59) ----------

    internal static bool IsPdf(FileEntry entry) =>
        entry is { Kind: EntryKind.File, FullPath: not null, IsBlocked: false } && PdfPreviewPolicy.IsPdfExtension(entry.Extension);

    private string? PdfPreviewUnavailable => PdfRenderer is null ? "Visualização de PDF indisponível nesta compilação." : null;

    internal void OpenPdfPreview(PaneState pane, FileEntry entry)
    {
        if (PdfRenderer is null || entry.FullPath is null) return;
        var modal = new PdfPreviewModal(pane, entry) { LastInput = Clock() };
        PushModal(modal);
        LoadPdf(modal, null);
    }

    /// <summary>Confere o arquivo, abre o documento (com a senha digitada, se houver) e desenha a primeira página.</summary>
    private void LoadPdf(PdfPreviewModal modal, string? password)
    {
        var cts = BeginPdfWork(modal);
        modal.IsLoading = true;
        modal.NeedsPassword = false;
        Track(LoadPdfAsync(modal, modal.Entry.FullPath!, password, modal.Generation, cts.Token));
    }

    private CancellationTokenSource BeginPdfWork(PdfPreviewModal modal)
    {
        modal.Loading?.Cancel();
        modal.Loading?.Dispose();
        var cts = modal.Loading = new CancellationTokenSource(PreviewLimits.PdfTimeout);
        modal.Generation++;
        modal.Error = null;
        return cts;
    }

    private async Task LoadPdfAsync(PdfPreviewModal modal, string path, string? password, int generation, CancellationToken cancellationToken)
    {
        var limits = PreviewLimits;
        try
        {
            await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
                PdfPreviewPolicy.Inspect(stream, limits);
            }, cancellationToken);
            var document = await PdfRenderer!.OpenAsync(path, password, cancellationToken);
            if (generation != modal.Generation || modal.IsClosed)
            {
                document.Dispose();
                return;
            }
            modal.Document?.Dispose();
            modal.Document = document;
            modal.TotalPages = document.PageCount;
            modal.PageCount = Math.Min(document.PageCount, limits.MaxPdfPages);
            modal.PageIndex = 0;
            if (modal.PageCount == 0)
            {
                modal.Error = "Este PDF não tem páginas.";
                modal.IsLoading = false;
                RaiseChanged();
                return;
            }
        }
        catch (Exception ex) when (generation == modal.Generation && !modal.IsClosed && PdfError(modal, ex, cancellationToken))
        {
            modal.IsLoading = false;
            RaiseChanged();
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        RenderPdfPage(modal);
    }

    /// <summary>Traduz a falha para a tela; false quando é só um cancelamento pedido (fechar, trocar de página).</summary>
    private bool PdfError(PdfPreviewModal modal, Exception ex, CancellationToken cancellationToken)
    {
        switch (ex)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                // Mesma geração e aberto: foi o prazo (PdfTimeout) que venceu. O arquivo não trava a tela, vira erro.
                modal.Error = "O PDF demorou demais para abrir ou desenhar esta página.";
                return true;
            case OperationCanceledException:
                return false;
            case PdfPasswordException password:
                modal.NeedsPassword = true;
                modal.Error = password.Message;
                return true;
            case PreviewException preview:
                modal.Error = preview.Message;
                return true;
            case IOException or UnauthorizedAccessException:
                modal.Error = "Não foi possível ler o arquivo: " + ErrorText(ex, "Visualização");
                return true;
            default:
                modal.Error = "Não foi possível mostrar este PDF.";
                return true;
        }
    }

    private void RenderPdfPage(PdfPreviewModal modal)
    {
        if (modal.Document is not { } document) return;
        var cts = BeginPdfWork(modal);
        modal.Page = null;
        modal.IsLoading = true;
        modal.ResetView();
        Track(RenderPdfPageAsync(modal, document, modal.PageIndex, modal.Generation, cts.Token));
    }

    private async Task RenderPdfPageAsync(PdfPreviewModal modal, IPdfDocument document, int index, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var page = await document.RenderPageAsync(index, PreviewLimits.PdfRenderSide, cancellationToken);
            if (generation != modal.Generation || modal.IsClosed) return;
            modal.Page = page;
        }
        catch (Exception ex) when (generation == modal.Generation && !modal.IsClosed && PdfError(modal, ex, cancellationToken))
        {
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (generation != modal.Generation || modal.IsClosed) return;
        modal.IsLoading = false;
        RaiseChanged();
    }

    private void HandlePdfPreview(PdfPreviewModal modal, InputAction action)
    {
        modal.LastInput = Clock();
        modal.HintsFaded = false;
        if (modal.NeedsPassword)
        {
            if (action == InputAction.Confirm) AskPdfPassword(modal);
            else if (action == InputAction.Back) ClosePdfPreview(modal);
            return;
        }
        var zoomed = modal.ZoomIndex > 0;
        switch (action)
        {
            case InputAction.Back:
                ClosePdfPreview(modal);
                break;
            case InputAction.PreviousRegion:
            case InputAction.NavigateLeft when !zoomed:
                StepPdfPage(modal, -1);
                break;
            case InputAction.NextRegion:
            case InputAction.NavigateRight when !zoomed:
                StepPdfPage(modal, 1);
                break;
            case InputAction.NavigateLeft: modal.Pan(-1, 0); break;
            case InputAction.NavigateRight: modal.Pan(1, 0); break;
            case InputAction.NavigateUp: modal.Pan(0, -1); break;
            case InputAction.NavigateDown: modal.Pan(0, 1); break;
            case InputAction.PageDown: if (modal.Page is not null) modal.ChangeZoom(1); break;
            case InputAction.PageUp: modal.ChangeZoom(-1); break;
            case InputAction.Confirm: modal.ResetView(); break;
        }
    }

    private void StepPdfPage(PdfPreviewModal modal, int delta)
    {
        if (modal.Document is null) return;
        var target = modal.PageIndex + delta;
        if (target < 0 || target >= modal.PageCount)
        {
            StatusMessage = delta < 0 ? "Esta é a primeira página."
                : modal.TotalPages > modal.PageCount ? $"Limite de {modal.PageCount} páginas nesta visualização; abra no aplicativo padrão para ver o resto."
                : "Esta é a última página.";
            return;
        }
        modal.PageIndex = target;
        RenderPdfPage(modal);
    }

    /// <summary>Senha pelo teclado virtual (campo mascarado, sem sugestões); nunca é guardada.</summary>
    private void AskPdfPassword(PdfPreviewModal modal)
    {
        var keyboard = new Core.Text.VirtualKeyboard(Core.Text.TextFieldKind.Password, $"Senha de {modal.Entry.Name}");
        KeyboardModal? prompt = null;
        prompt = new KeyboardModal(keyboard, k =>
        {
            if (k.Length == 0)
            {
                k.Reopen("Digite a senha ou cancele.");
                return Task.CompletedTask;
            }
            var secret = k.TakeSecret();
            CloseModal(prompt!);
            LoadPdf(modal, secret);
            return Task.CompletedTask;
        });
        PushModal(prompt);
    }

    /// <summary>Fecha, libera o arquivo e deixa o foco da lista no PDF.</summary>
    private void ClosePdfPreview(PdfPreviewModal modal)
    {
        modal.IsClosed = true;
        modal.Loading?.Cancel();
        modal.Generation++;
        modal.Document?.Dispose();
        modal.Document = null;
        modal.Page = null;
        CloseModal(modal);
        modal.Pane.List.FocusById(modal.Entry.Id);
    }
}
