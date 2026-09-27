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

/// <summary>Tecla com largura em colunas (grade de <see cref="VirtualKeyboardLayouts.Columns"/> colunas por linha).</summary>
public sealed record VirtualKey(KeyKind Kind, string Label, string? Text = null, int Span = 1, string? AccessibleName = null)
{
    public string Name => AccessibleName ?? Label;

    /// <summary>Teclas de função (tudo que não insere um caractere) são desenhadas com outro tom.</summary>
    public bool IsFunction => Kind is not (KeyKind.Character or KeyKind.Space);

    public static VirtualKey Character(string c) => new(KeyKind.Character, c, c);
}

/// <summary>
/// Layout no modelo estrutural de console (campo em cima, quatro linhas de caracteres, linha de funções e
/// linha inferior com cursor, "mais" e Concluir). Identidade visual própria: só a estrutura é inspirada.
/// </summary>
public static class VirtualKeyboardLayouts
{
    public const int Columns = 11;

    public static IReadOnlyList<IReadOnlyList<VirtualKey>> Build(KeyboardPage page, KeyboardLanguage language, bool isPassword)
    {
        var rows = new List<IReadOnlyList<VirtualKey>>();
        switch (page)
        {
            case KeyboardPage.Letters:
                rows.Add(Chars("1234567890@"));
                rows.Add(Chars("qwertyuiop#"));
                rows.Add(language == KeyboardLanguage.PortugueseBrazil ? Chars("asdfghjklç-") : Chars("asdfghjkl'-"));
                rows.Add(Chars("zxcvbnm,._!"));
                break;
            case KeyboardPage.Symbols:
                rows.Add(Chars("1234567890°"));
                rows.Add(Chars("!@#$%&*()-+"));
                rows.Add(Chars("[]{}=;,'~^`"));
                rows.Add(Chars("\\/:?\"<>|._§"));
                break;
            case KeyboardPage.Accents:
                rows.Add(Chars("áàâãäéèêëíì"));
                rows.Add(Chars("îïóòôõöúùûü"));
                rows.Add(Chars("çñýÿªº€£¿¡ß"));
                rows.Add(
                [
                    .. Chars("æœøå«»"),
                    new VirtualKey(KeyKind.Clear, "Limpar", Span: 2, AccessibleName: "Limpar texto"),
                    new VirtualKey(KeyKind.SwitchLanguage, language == KeyboardLanguage.PortugueseBrazil ? "PT-BR → EN" : "EN → PT-BR", Span: 3,
                        AccessibleName: "Trocar idioma do teclado"),
                ]);
                break;
        }

        rows.Add(
        [
            new VirtualKey(KeyKind.Shift, "⇧", Span: 2, AccessibleName: "Maiúsculas"),
            new VirtualKey(KeyKind.PageLetters, "ABC", Span: 2, AccessibleName: "Letras"),
            new VirtualKey(KeyKind.PageSymbols, "@#:", Span: 2, AccessibleName: "Símbolos"),
            new VirtualKey(KeyKind.Space, "espaço", " ", 3, "Espaço"),
            new VirtualKey(KeyKind.Backspace, "⌫", Span: 2, AccessibleName: "Apagar caractere"),
        ]);

        var left = new VirtualKey(KeyKind.CaretLeft, "◀", AccessibleName: "Mover cursor para a esquerda");
        var right = new VirtualKey(KeyKind.CaretRight, "▶", AccessibleName: "Mover cursor para a direita");
        var more = new VirtualKey(KeyKind.PageAccents, "…", Span: 2, AccessibleName: "Mais: acentos, idioma e limpar");
        var cancel = new VirtualKey(KeyKind.Cancel, "Cancelar", Span: 2);
        rows.Add(isPassword
            ? [left, right, more, new VirtualKey(KeyKind.Reveal, "Mostrar", Span: 2, AccessibleName: "Mostrar senha temporariamente"), cancel,
                new VirtualKey(KeyKind.Done, "Concluir", Span: 3)]
            : [left, right, more, cancel, new VirtualKey(KeyKind.Done, "Concluir", Span: 5)]);
        return rows;
    }

    /// <summary>Página que a tecla abre (para destacar a página atual na linha de funções).</summary>
    public static KeyboardPage? PageOf(VirtualKey key) => key.Kind switch
    {
        KeyKind.PageLetters => KeyboardPage.Letters,
        KeyKind.PageSymbols => KeyboardPage.Symbols,
        KeyKind.PageAccents => KeyboardPage.Accents,
        _ => null,
    };

    private static List<VirtualKey> Chars(string s)
    {
        var list = new List<VirtualKey>(s.Length);
        foreach (var rune in s.EnumerateRunes()) list.Add(VirtualKey.Character(rune.ToString()));
        return list;
    }
}
