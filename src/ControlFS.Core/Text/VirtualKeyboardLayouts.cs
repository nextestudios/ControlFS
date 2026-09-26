namespace ControlFS.Core.Text;

public enum KeyKind
{
    Character,
    Shift,
    Backspace,
    Clear,
    Space,
    CaretLeft,
    CaretRight,
    PageLetters,
    PageSymbols,
    PageAccents,
    SwitchLanguage,
    Reveal,
    Done,
    Cancel,
}

public enum KeyboardPage
{
    Letters,
    Symbols,
    Accents,
}

public enum KeyboardLanguage
{
    PortugueseBrazil,
    English,
}

/// <summary>Tecla com largura em colunas (grade de 10 colunas por linha).</summary>
public sealed record VirtualKey(KeyKind Kind, string Label, string? Text = null, int Span = 1, string? AccessibleName = null)
{
    public string Name => AccessibleName ?? Label;

    public static VirtualKey Character(string c) => new(KeyKind.Character, c, c);
}

public static class VirtualKeyboardLayouts
{
    public const int Columns = 10;

    public static IReadOnlyList<IReadOnlyList<VirtualKey>> Build(KeyboardPage page, KeyboardLanguage language, bool isPassword)
    {
        var rows = new List<IReadOnlyList<VirtualKey>>();
        switch (page)
        {
            case KeyboardPage.Letters:
                rows.Add(Chars("1234567890"));
                rows.Add(Chars("qwertyuiop"));
                rows.Add(language == KeyboardLanguage.PortugueseBrazil ? Chars("asdfghjklç") : Chars("asdfghjkl'"));
                rows.Add([new VirtualKey(KeyKind.Shift, "⇧", AccessibleName: "Maiúsculas"), .. Chars("zxcvbnm-_")]);
                break;
            case KeyboardPage.Symbols:
                rows.Add(Chars("1234567890"));
                rows.Add(Chars("!@#$%&()[]"));
                rows.Add(Chars("{}+=;,'~^`"));
                rows.Add(Chars("\\/:*?\"<>|°"));
                break;
            case KeyboardPage.Accents:
                rows.Add(Chars("áàâãäéèêëí"));
                rows.Add(Chars("ìîïóòôõöúù"));
                rows.Add(Chars("ûüçñýÿªº€£"));
                rows.Add([new VirtualKey(KeyKind.Shift, "⇧", AccessibleName: "Maiúsculas"), .. Chars("æœßøå«»¿¡")]);
                break;
        }

        var pageKeyA = page == KeyboardPage.Letters
            ? new VirtualKey(KeyKind.PageSymbols, "?123", AccessibleName: "Símbolos")
            : new VirtualKey(KeyKind.PageLetters, "ABC", AccessibleName: "Letras");
        var pageKeyB = page == KeyboardPage.Accents
            ? new VirtualKey(KeyKind.PageSymbols, "?123", AccessibleName: "Símbolos")
            : new VirtualKey(KeyKind.PageAccents, "áé", AccessibleName: "Acentos");
        rows.Add(
        [
            pageKeyA,
            pageKeyB,
            new VirtualKey(KeyKind.Space, "espaço", " ", 3, "Espaço"),
            VirtualKey.Character("."),
            new VirtualKey(KeyKind.CaretLeft, "◀", AccessibleName: "Mover cursor para a esquerda"),
            new VirtualKey(KeyKind.CaretRight, "▶", AccessibleName: "Mover cursor para a direita"),
            new VirtualKey(KeyKind.Backspace, "⌫", Span: 2, AccessibleName: "Apagar caractere"),
        ]);

        var language_ = new VirtualKey(KeyKind.SwitchLanguage, language == KeyboardLanguage.PortugueseBrazil ? "PT-BR" : "EN", AccessibleName: "Trocar idioma do teclado");
        rows.Add(isPassword
            ?
            [
                new VirtualKey(KeyKind.Clear, "Limpar", Span: 2),
                language_ with { Span = 2 },
                new VirtualKey(KeyKind.Reveal, "Mostrar", Span: 2, AccessibleName: "Mostrar senha temporariamente"),
                new VirtualKey(KeyKind.Cancel, "Cancelar", Span: 2),
                new VirtualKey(KeyKind.Done, "OK", Span: 2, AccessibleName: "Confirmar"),
            ]
            :
            [
                new VirtualKey(KeyKind.Clear, "Limpar", Span: 2),
                language_ with { Span = 2 },
                new VirtualKey(KeyKind.Cancel, "Cancelar", Span: 3),
                new VirtualKey(KeyKind.Done, "OK", Span: 3, AccessibleName: "Confirmar"),
            ]);
        return rows;
    }

    private static List<VirtualKey> Chars(string s)
    {
        var list = new List<VirtualKey>(s.Length);
        foreach (var rune in s.EnumerateRunes()) list.Add(VirtualKey.Character(rune.ToString()));
        return list;
    }
}
