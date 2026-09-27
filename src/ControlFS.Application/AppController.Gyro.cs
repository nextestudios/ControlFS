using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Mira por giroscópio no teclado virtual (#77, experimental, desligada por padrão): uma camada de ponteiro sobre o foco
/// do <see cref="VirtualKeyboard"/>. O ponteiro parte do centro da tecla focada e anda em frações de tecla; quando cruza
/// para outra tecla, ela recebe o foco (Sul digita como sempre). Nas bordas o ponteiro para (nunca dá a volta). O
/// direcional continua valendo: mover o foco por ele reancora o ponteiro na tecla nova. R3 recentraliza.
/// </summary>
public sealed partial class AppController
{
    private GyroAim? _aim;

    /// <summary>Publicado pela camada de entrada: o controle ativo tem giroscópio e a mira está ligada (mostra "Recentralizar").</summary>
    public bool GyroAimAvailable { get; private set; }

    public void SetGyroAimAvailable(bool available)
    {
        if (GyroAimAvailable == available) return;
        GyroAimAvailable = available;
        RaiseChanged();
    }

    /// <summary>Deslocamento do ponteiro, em teclas (dx para a direita, dy para baixo), vindo do filtro do giroscópio.</summary>
    public void AimKeyboard(double dx, double dy)
    {
        if (!Settings.GyroKeyboard || TopModal is not KeyboardModal { IsBusy: false } modal || modal.Keyboard.Rows.Count == 0) return;
        if (dx == 0 && dy == 0) return;
        var keyboard = modal.Keyboard;
        if (_aim is not { } aim || !ReferenceEquals(aim.Keyboard, keyboard) || aim.Row != keyboard.Row || aim.Column != keyboard.Column)
        {
            var (cx, cy) = keyboard.FocusCenter; // primeira mira, outro teclado ou o direcional moveu o foco
            aim = new GyroAim(keyboard, cx, cy, keyboard.Row, keyboard.Column);
        }
        var x = Math.Clamp(aim.X + dx, 0, VirtualKeyboardLayouts.Columns - 0.001);
        var y = Math.Clamp(aim.Y + dy, 0, keyboard.Rows.Count - 0.001);
        var (row, column) = keyboard.KeyAt(x, y);
        var moved = row != aim.Row || column != aim.Column;
        if (moved) keyboard.FocusKey(row, column); // na faixa de sugestões, só sai dela quando o ponteiro chega a outra tecla
        _aim = new GyroAim(keyboard, x, y, keyboard.Row, keyboard.Column);
        if (moved) RaiseChanged();
    }

    /// <summary>R3 no teclado com a mira ligada: o ponteiro volta ao centro da tecla focada.</summary>
    private bool HandleGyroRecenter(InputAction action)
    {
        if (action != InputAction.ChangeView || !Settings.GyroKeyboard) return false;
        _aim = null;
        return true;
    }

    internal void ToggleGyroKeyboard()
    {
        UpdateSettings(s => s with { GyroKeyboard = !s.GyroKeyboard });
        _aim = null;
        StatusMessage = Settings.GyroKeyboard
            ? "Mira por giroscópio ligada (experimental): no teclado virtual, gire o controle para apontar; R3 recentraliza."
            : "Mira por giroscópio desligada.";
    }

    private sealed record GyroAim(VirtualKeyboard Keyboard, double X, double Y, int Row, int Column);
}
