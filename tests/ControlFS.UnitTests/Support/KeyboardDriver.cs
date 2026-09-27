using System.Globalization;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;

namespace ControlFS.UnitTests.Support;

/// <summary>
/// "Digita" no teclado virtual como um usuário de controle: só com direções e confirmar.
/// Troca de página/maiúsculas navegando até as teclas correspondentes.
/// </summary>
public static class KeyboardDriver
{
    public static void Type(Action<InputAction> send, Func<VirtualKeyboard> kb, string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var s = rune.ToString();
            var lower = s.ToLower(CultureInfo.CurrentCulture);
            var needsUpper = s != lower;
            if (s == " ")
            {
                Press(send, kb, k => k.Kind == KeyKind.Space);
                continue;
            }
            if (!EnsurePageWith(send, kb, lower)) throw new InvalidOperationException($"Tecla não encontrada: {s}");
            if (needsUpper && kb().Shift == ShiftState.Off) Press(send, kb, k => k.Kind == KeyKind.Shift);
            Press(send, kb, k => k.Kind == KeyKind.Character && k.Text == lower);
        }
    }

    /// <summary>Navega até a tecla e confirma. Se a tecla estiver em outra página (ex.: Limpar e idioma em "…"), troca de página antes.</summary>
    public static void Press(Action<InputAction> send, Func<VirtualKeyboard> kb, Func<VirtualKey, bool> target)
    {
        for (var attempt = 0; attempt < 3 && !kb().Rows.Any(r => r.Any(target)); attempt++)
            MoveToAndConfirm(send, kb, k => k.Kind == NextPage(kb().Page));
        MoveToAndConfirm(send, kb, target);
    }

    private static void MoveToAndConfirm(Action<InputAction> send, Func<VirtualKeyboard> kb, Func<VirtualKey, bool> target)
    {
        MoveTo(send, kb, target);
        send(InputAction.Confirm);
    }

    private static bool EnsurePageWith(Action<InputAction> send, Func<VirtualKeyboard> kb, string character)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (kb().Rows.Any(r => r.Any(k => k.Kind == KeyKind.Character && k.Text == character))) return true;
            var next = NextPage(kb().Page);
            MoveToAndConfirm(send, kb, k => k.Kind == next);
        }
        return false;
    }

    private static KeyKind NextPage(KeyboardPage page) => page switch
    {
        KeyboardPage.Letters => KeyKind.PageAccents,
        KeyboardPage.Accents => KeyKind.PageSymbols,
        _ => KeyKind.PageLetters,
    };

    private static void MoveTo(Action<InputAction> send, Func<VirtualKeyboard> kb, Func<VirtualKey, bool> target)
    {
        var rows = kb().Rows;
        var row = -1;
        var col = -1;
        for (var r = 0; r < rows.Count && row < 0; r++)
            for (var c = 0; c < rows[r].Count; c++)
                if (target(rows[r][c])) { row = r; col = c; break; }
        if (row < 0) throw new InvalidOperationException("Tecla alvo não está no layout atual.");
        var guard = 0;
        while (kb().Row != row && guard++ < 20) send(kb().Row < row ? InputAction.NavigateDown : InputAction.NavigateUp);
        while (kb().Column != col && guard++ < 40) send(kb().Column < col ? InputAction.NavigateRight : InputAction.NavigateLeft);
        if (!target(kb().FocusedKey)) throw new InvalidOperationException("Navegação não alcançou a tecla alvo.");
    }
}
