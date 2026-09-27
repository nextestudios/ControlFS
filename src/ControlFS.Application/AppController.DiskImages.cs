using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Montar e desmontar imagens (#73, #74) com o suporte nativo do Windows. Norte numa .iso/.img/.vhd/.vhdx → "Montar
/// imagem" abre a unidade nova; Norte numa unidade que é uma imagem montada (por qualquer programa) → "Desmontar
/// imagem…", com confirmação. A montagem roda fora da thread de UI; erros vêm explicados.
/// </summary>
public sealed partial class AppController
{
    public IDiskImageService? DiskImages { get; init; }

    private MenuItem? MountItem(string file) => DiskImages is null || !DiskImageFormats.IsMountable(file) ? null
        : new MenuItem("Montar imagem", () => BeginMount(file),
            Detail: DiskImageFormats.IsOptical(file) ? "Abre o conteúdo como uma unidade (somente leitura), como o \"Montar\" do Windows." : "Disco rígido virtual: o Windows pode pedir administrador.",
            Icon: ActionIcon.Drive, Section: "Abrir");

    internal void BeginMount(string imagePath)
    {
        if (DiskImages is null) return;
        var dialog = new DialogModal("Montando imagem", [("Imagem", Path.GetFileName(imagePath))]) { Icon = ActionIcon.Drive, Message = "Aguardando o Windows criar a unidade…" };
        PushModal(dialog);
        Track(MountAsync(imagePath, dialog));
    }

    private async Task MountAsync(string imagePath, DialogModal dialog)
    {
        var images = DiskImages!;
        string root;
        try
        {
            root = await Task.Run(() => images.Mount(imagePath, CancellationToken.None));
        }
        catch (FileOperationException ex)
        {
            CloseModal(dialog);
            ShowMessage("Não foi possível montar", [("Imagem", Path.GetFileName(imagePath))], ex.Message, icon: ActionIcon.Error);
            return;
        }
        CloseModal(dialog);
        RefreshDrives();
        Screen = Screen.Browser;
        await NavigateAsync(Browser, new PhysicalLocation(root), pushHistory: Browser.Location is not null);
        SetStatus($"Imagem montada em {root.TrimEnd('\\')}. Para desmontar: Meu computador → Norte na unidade → Desmontar imagem.");
    }

    /// <summary>Descobre fora da thread de UI se a unidade é uma imagem montada e então abre o menu com (ou sem) "Desmontar".</summary>
    private bool DeferForImage(string? driveRoot, Action<string?> show)
    {
        if (DiskImages is not { } images || driveRoot is null) return false;
        Track(CheckImageAsync(images, driveRoot, show));
        return true;
    }

    private async Task CheckImageAsync(IDiskImageService images, string driveRoot, Action<string?> show)
    {
        string? image = null;
        try { image = await Task.Run(() => images.ImageBehind(driveRoot)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileOperationException) { }
        if (TopModal is null) show(image);
    }

    private MenuItem UnmountItem(string driveRoot, string image) => new("Desmontar imagem…", () => ConfirmUnmount(driveRoot, image),
        Detail: $"Imagem: {Path.GetFileName(image)}", Icon: ActionIcon.Close);

    private void ConfirmUnmount(string driveRoot, string image)
    {
        var dialog = new DialogModal("Desmontar a imagem?", [("Unidade", driveRoot), ("Imagem", image)], sensitive: true)
        {
            Message = "Programas com arquivos abertos nesta unidade perdem o acesso a eles: salve e feche esses arquivos antes. O arquivo da imagem não é alterado.",
            Icon = ActionIcon.Drive,
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Desmontar", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            Track(UnmountAsync(driveRoot));
        }, icon: ActionIcon.Close));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    private async Task UnmountAsync(string driveRoot)
    {
        var images = DiskImages!;
        try
        {
            await Task.Run(() => images.Unmount(driveRoot));
        }
        catch (FileOperationException ex)
        {
            ShowMessage("Não foi possível desmontar", [("Unidade", driveRoot)], ex.Message, icon: ActionIcon.Error);
            return;
        }
        // Abas que mostravam algo dentro da unidade voltam para Meu computador.
        foreach (var tab in _tabs.Where(t => t.Location is PhysicalLocation here &&
            here.FullPath.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase)).ToList())
            Track(NavigateAsync(tab, ThisPcLocation.Instance, pushHistory: false));
        RefreshDrives();
        SetStatus($"Imagem desmontada ({driveRoot.TrimEnd('\\')}).");
    }
}
