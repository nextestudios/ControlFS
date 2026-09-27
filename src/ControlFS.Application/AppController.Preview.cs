using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Preview;

namespace ControlFS.Application;

public sealed partial class AppController
{
    public PreviewLimits PreviewLimits { get; set; } = PreviewLimits.Default;

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
        var modal = new ImagePreviewModal(pane, images, index);
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
            modal.Error = "Não foi possível ler o arquivo: " + ex.Message;
        }
        if (generation != modal.Generation) return;
        modal.IsLoading = false;
        RaiseChanged();
    }

    private void HandleImagePreview(ImagePreviewModal modal, InputAction action)
    {
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

    /// <summary>Fecha e deixa o foco da lista na última imagem vista.</summary>
    private void ClosePreview(ImagePreviewModal modal)
    {
        modal.Loading?.Cancel();
        modal.Generation++;
        CloseModal(modal);
        modal.Pane.List.FocusById(modal.Current.Id);
    }
}
