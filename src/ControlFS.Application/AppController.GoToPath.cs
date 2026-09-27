using ControlFS.Application.State;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// "Ir para caminho…": o teclado virtual (campo de caminho, aceita separadores e ":") abre com a pasta atual
/// selecionada; Concluir navega. Caminho inválido ou inexistente mostra o erro sem sair do teclado. O caminho de um
/// arquivo abre a pasta dele com o foco no arquivo.
/// </summary>
public sealed partial class AppController
{
    internal void BeginGoToPath(PaneState pane)
    {
        var start = Screen == Screen.Home ? string.Empty : pane.LastValidPhysical?.FullPath ?? string.Empty;
        var keyboard = new VirtualKeyboard(TextFieldKind.Path, "Ir para caminho", start, initialSelection: (0, start.Length));
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, async k =>
        {
            var (target, focus, error) = await Task.Run(() => ResolveTypedPath(k.Text));
            if (error is not null)
            {
                k.Reopen(error);
                return;
            }
            CloseModal(modal!);
            if (pane.Mode == PaneMode.Browse && Screen == Screen.Home)
            {
                Browser.Back.Clear();
                Browser.Forward.Clear();
                Browser.Location = null;
                Screen = Screen.Browser;
            }
            await NavigateAsync(pane, new PhysicalLocation(target!), pushHistory: pane.Location is not null, focusId: focus);
        });
        PushModal(modal);
    }

    /// <summary>Pasta de destino e, quando o caminho é de um arquivo, o item a focar nela.</summary>
    private (string? Folder, string? Focus, string? Error) ResolveTypedPath(string text)
    {
        var (path, error) = TypedPath.Normalize(text);
        if (path is null) return (null, null, error);
        try
        {
            if (_fs.DirectoryExists(path)) return (path, null, null);
            if (File.Exists(path) && Path.GetDirectoryName(path) is { } parent) return (parent, Path.GetFileName(path), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, null, $"Não foi possível acessar {path}: {ex.Message}");
        }
        return (null, null, $"Pasta não encontrada: {path}");
    }
}
