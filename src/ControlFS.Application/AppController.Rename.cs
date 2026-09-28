using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>
    /// Renomear pelo teclado virtual: nome sem a extensão pré-selecionado (cursor antes da extensão), extensão alterada exige confirmação, nomes inválidos ou
    /// já existentes são recusados sem tocar no disco, e o foco permanece no item renomeado.
    /// </summary>
    internal void BeginRename(PaneState pane, FileEntry entry)
    {
        if (_fileOps is null || entry.FullPath is not { } path || pane.Location is not PhysicalLocation here) return;
        var isFile = entry.Kind == EntryKind.File;
        var extension = isFile ? Path.GetExtension(entry.Name) : string.Empty;
        var caret = isFile && extension.Length > 0 && extension.Length < entry.Name.Length ? entry.Name.Length - extension.Length : entry.Name.Length;
        // O nome sem a extensão já vem selecionado: digitar substitui só ele (example-file.zip → novo.zip).
        var keyboard = new VirtualKeyboard(TextFieldKind.FileName, $"Renomear “{entry.Name}”", entry.Name, initialCaret: caret, initialSelection: (0, caret));
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, async k =>
        {
            var newName = k.Text;
            if (string.Equals(newName, entry.Name, StringComparison.Ordinal))
            {
                CloseModal(modal!);
                return;
            }
            var newExtension = isFile ? Path.GetExtension(newName) : string.Empty;
            if (isFile && !string.Equals(extension, newExtension, StringComparison.OrdinalIgnoreCase))
            {
                k.Reopen(null);
                ConfirmExtensionChange(extension, newExtension, () => Track(ApplyRenameAsync(pane, here, path, newName, modal!)));
                return;
            }
            await ApplyRenameAsync(pane, here, path, newName, modal!);
        });
        PushModal(modal);
    }

    private async Task ApplyRenameAsync(PaneState pane, PhysicalLocation here, string path, string newName, KeyboardModal modal)
    {
        try
        {
            var renamed = await Task.Run(() => _fileOps!.Rename(path, newName));
            CloseModal(modal);
            RecordRename(path, renamed);
            RegisterRenameUndo(path, renamed.FullPath ?? Path.Join(Path.GetDirectoryName(path), renamed.Name), redo: false);
            await NavigateAsync(pane, here, pushHistory: false, focusId: renamed.Id);
            StatusMessage = $"Renomeado para \"{renamed.Name}\".";
        }
        catch (FileOperationException ex)
        {
            modal.Keyboard.Reopen(ex.Message);
        }
        RaiseChanged();
    }

    private void ConfirmExtensionChange(string from, string to, Action confirmed)
    {
        var dialog = new DialogModal("Alterar a extensão do arquivo?",
        [
            ("De", from.Length > 0 ? from : "(sem extensão)"),
            ("Para", to.Length > 0 ? to : "(sem extensão)"),
        ], sensitive: true)
        {
            Message = "O Windows usa a extensão para escolher o programa que abre o arquivo; ele pode deixar de abrir corretamente.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Alterar extensão", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            confirmed();
        }, icon: ActionIcon.Rename));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }
}
