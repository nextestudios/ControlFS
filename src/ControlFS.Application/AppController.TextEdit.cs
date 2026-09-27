using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Edição leve a partir da visualização de texto (#62). Norte entra na edição; a linha em foco é editada no teclado virtual;
/// Start salva (com confirmação) numa troca atômica que guarda o original ao lado; Voltar com alterações pede confirmação.
/// Binários, arquivos grandes, somente leitura e codificações que não voltam iguais são recusados antes de editar.
/// </summary>
public sealed partial class AppController
{
    /// <summary>A visualização permite tentar editar (texto inteiro na tela, sem erro).</summary>
    private static bool CanOfferEdit(TextPreviewModal modal) =>
        modal is { Editor: null, IsOpeningEditor: false, Error: null, Document: { IsTruncated: false } } && modal.Entry.FullPath is not null;

    private void BeginTextEdit(TextPreviewModal modal)
    {
        if (!CanOfferEdit(modal))
        {
            if (modal.Document is { IsTruncated: true }) StatusMessage = "Só dá para editar arquivos que cabem inteiros na visualização.";
            return;
        }
        modal.IsOpeningEditor = true;
        Track(OpenTextEditorAsync(modal, modal.Entry.FullPath!));
    }

    private async Task OpenTextEditorAsync(TextPreviewModal modal, string path)
    {
        try
        {
            var editor = await Task.Run(() =>
            {
                var info = new FileInfo(path);
                if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                    throw new PreviewException("O arquivo é somente leitura: tire o atributo no Windows para editar.");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
                var document = TextEditDocument.Load(stream);
                return new TextEditor(document, info.Length, info.LastWriteTimeUtc);
            });
            modal.Editor = editor;
            editor.MoveCursor(modal.Top);
            modal.RevealCursor();
            StatusMessage = "Editando: Sul edita a linha, Norte insere ou apaga, Start salva.";
        }
        catch (PreviewException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Não foi possível abrir para editar: " + ex.Message;
        }
        modal.IsOpeningEditor = false;
        RaiseChanged();
    }

    private void HandleTextEditing(TextPreviewModal modal, TextEditor editor, InputAction action)
    {
        var page = Math.Max(1, modal.PageLines - 1);
        switch (action)
        {
            case InputAction.Back: LeaveTextEdit(modal, editor); return;
            case InputAction.NavigateUp: editor.MoveCursor(editor.Cursor - 1); break;
            case InputAction.NavigateDown: editor.MoveCursor(editor.Cursor + 1); break;
            case InputAction.PageUp: editor.MoveCursor(editor.Cursor - page); break;
            case InputAction.PageDown: editor.MoveCursor(editor.Cursor + page); break;
            case InputAction.PreviousRegion: editor.MoveCursor(0); break;
            case InputAction.NextRegion: editor.MoveCursor(int.MaxValue); break;
            case InputAction.NavigateLeft: modal.ShiftColumns(-TextPreviewModal.ColumnStep); break;
            case InputAction.NavigateRight: modal.ShiftColumns(TextPreviewModal.ColumnStep); break;
            case InputAction.Confirm: EditLine(modal, editor, replace: true, below: false); break;
            case InputAction.OpenContextMenu: ShowLineMenu(modal, editor); break;
            case InputAction.OpenAppMenu: ConfirmSaveText(modal, editor); break;
        }
        modal.RevealCursor();
    }

    /// <summary>Teclado virtual numa linha: a em foco (substituir) ou uma nova acima/abaixo.</summary>
    private void EditLine(TextPreviewModal modal, TextEditor editor, bool replace, bool below)
    {
        var current = editor.Lines[editor.Cursor].Text;
        if (replace && current.Length > TextEditDocument.MaxLineLength)
        {
            StatusMessage = $"Linha longa demais para o teclado virtual (mais de {TextEditDocument.MaxLineLength} caracteres). Use o aplicativo padrão.";
            return;
        }
        var number = editor.Cursor + 1 + (replace ? 0 : below ? 1 : 0);
        var keyboard = new VirtualKeyboard(TextFieldKind.Generic, replace ? $"Linha {number}" : $"Nova linha {number}", replace ? current : string.Empty,
            maxLength: TextEditDocument.MaxLineLength);
        KeyboardModal? prompt = null;
        prompt = new KeyboardModal(keyboard, k =>
        {
            // Uma linha nunca ganha quebras (colar com o teclado físico): as quebras continuam as do arquivo.
            var text = k.Text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
            CloseModal(prompt!);
            if (replace) editor.ReplaceLine(text);
            else editor.Insert(text, below);
            modal.RevealCursor();
            return Task.CompletedTask;
        });
        PushModal(prompt);
    }

    private void ShowLineMenu(TextPreviewModal modal, TextEditor editor)
    {
        var line = editor.Cursor + 1;
        const string section = "Linha", file = "Arquivo";
        MenuItem[] items =
        [
            new($"Editar a linha {line}", () => EditLine(modal, editor, replace: true, below: false), Icon: ActionIcon.Rename, Section: section),
            new("Inserir linha abaixo…", () => EditLine(modal, editor, replace: false, below: true), Icon: ActionIcon.MoveDown, Section: section),
            new("Inserir linha acima…", () => EditLine(modal, editor, replace: false, below: false), Icon: ActionIcon.MoveUp, Section: section),
            new($"Apagar a linha {line}", () =>
            {
                editor.DeleteLine();
                modal.RevealCursor();
            }, Detail: "Dá para desfazer até salvar.", Icon: ActionIcon.Delete, Section: section),
            new("Desfazer", () =>
            {
                editor.Undo();
                modal.RevealCursor();
            }, editor.CanUndo ? null : "Nada para desfazer.", Icon: ActionIcon.Undo, Section: file),
            new("Salvar…", () => ConfirmSaveText(modal, editor), editor.IsModified ? null : "Nenhuma alteração para salvar.", Icon: ActionIcon.Accept, Section: file),
            new("Sair da edição", () => LeaveTextEdit(modal, editor), Icon: ActionIcon.Close, Section: file),
        ];
        PushModal(new MenuModal($"{modal.Entry.Name} · linha {line}", items) { Icon = ActionIcon.Text });
    }

    /// <summary>Sai da edição; com alterações, pergunta antes de descartar (o foco começa em continuar editando).</summary>
    private void LeaveTextEdit(TextPreviewModal modal, TextEditor editor)
    {
        if (!editor.IsModified)
        {
            modal.Editor = null;
            return;
        }
        var dialog = new DialogModal("Descartar as alterações?", [("Arquivo", modal.Entry.Name), ("Linhas alteradas", editor.ChangedLines().ToString(System.Globalization.CultureInfo.CurrentCulture))])
        {
            Message = "O arquivo no disco não foi alterado.",
        };
        var keep = new DialogOption("Continuar editando", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Back);
        dialog.Options.Add(new DialogOption("Descartar", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            modal.Editor = null;
            StatusMessage = "Alterações descartadas.";
        }, icon: ActionIcon.Erase));
        dialog.Options.Add(keep);
        dialog.BackOption = keep;
        PushModal(dialog);
    }

    /// <summary>Confirmação antes de substituir o arquivo: o que muda e onde fica a cópia do original.</summary>
    private void ConfirmSaveText(TextPreviewModal modal, TextEditor editor)
    {
        if (!editor.IsModified)
        {
            StatusMessage = "Nenhuma alteração para salvar.";
            return;
        }
        var path = modal.Entry.FullPath!;
        var dialog = new DialogModal("Salvar as alterações?", [
            ("Arquivo", modal.Entry.Name),
            ("Linhas alteradas", editor.ChangedLines().ToString(System.Globalization.CultureInfo.CurrentCulture)),
            ("Codificação", editor.Document.EncodingName),
            ("Cópia do original", Path.GetFileName(AtomicFileWriter.BackupPathFor(path))),
        ])
        {
            Message = "O arquivo é substituído de uma vez (nunca fica pela metade); o original fica na cópia ao lado.",
            Icon = ActionIcon.Text,
        };
        var keep = new DialogOption("Continuar editando", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Back);
        dialog.Options.Add(new DialogOption("Salvar", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            Track(SaveTextAsync(modal, editor, path, force: false));
        }, icon: ActionIcon.Accept));
        dialog.Options.Add(keep);
        dialog.BackOption = keep;
        PushModal(dialog);
    }

    private enum SaveOutcome
    {
        Saved,
        ChangedOnDisk,
    }

    private async Task SaveTextAsync(TextPreviewModal modal, TextEditor editor, string path, bool force)
    {
        var bytes = editor.Document.Encode(editor.Lines);
        var journal = _temporaries;
        SaveOutcome outcome;
        try
        {
            outcome = await Task.Run(() =>
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new FileNotFoundException("O arquivo não existe mais.", path);
                if (!force && (info.Length != editor.Length || info.LastWriteTimeUtc != editor.LastWriteUtc)) return SaveOutcome.ChangedOnDisk;
                var temp = AtomicFileWriter.TempPathFor(path);
                using var registration = journal?.Register(temp, TemporaryKind.PartialFile);
                AtomicFileWriter.Replace(path, temp, bytes, AtomicFileWriter.BackupPathFor(path));
                return SaveOutcome.Saved;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O original está intacto e as alterações continuam na edição.
            ShowMessage("Não foi possível salvar", [("Arquivo", modal.Entry.Name), ("Motivo", ex.Message), ("Suas alterações", "continuam na edição")], icon: ActionIcon.Error);
            return;
        }
        if (outcome == SaveOutcome.ChangedOnDisk)
        {
            ConfirmOverwriteChanged(modal, editor, path);
            return;
        }
        modal.Editor = null;
        modal.IsLoading = true;
        StatusMessage = $"Salvo. O original ficou em {Path.GetFileName(AtomicFileWriter.BackupPathFor(path))}.";
        await LoadTextPreviewAsync(modal, path);
    }

    /// <summary>Outro programa mexeu no arquivo depois que a edição começou: substituir só com confirmação explícita.</summary>
    private void ConfirmOverwriteChanged(TextPreviewModal modal, TextEditor editor, string path)
    {
        var dialog = new DialogModal("O arquivo mudou no disco", [("Arquivo", modal.Entry.Name)])
        {
            Message = "Outro programa alterou este arquivo depois que você começou a editar. Salvar substitui a versão dele pela sua (ela vai para a cópia de segurança).",
        };
        var cancel = new DialogOption("Continuar editando", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Back);
        dialog.Options.Add(new DialogOption("Substituir mesmo assim", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            Track(SaveTextAsync(modal, editor, path, force: true));
        }, icon: ActionIcon.Replace));
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }
}
