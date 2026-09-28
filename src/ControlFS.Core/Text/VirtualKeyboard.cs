using System.Globalization;
using ControlFS.Core.Actions;
using ControlFS.Core.Policies;

namespace ControlFS.Core.Text;

public enum TextFieldKind
{
    /// <summary>Um componente de nome no destino (regras do Windows).</summary>
    FileName,
    /// <summary>Caminho completo: separadores e ":" permitidos.</summary>
    Path,
    /// <summary>Mascarado, sem sugestões ou histórico, nunca persistido.</summary>
    Password,
    Generic,
}

public enum KeyboardOutcome
{
    None,
    Submitted,
    Cancelled,
}

public enum ShiftState
{
    Off,
    Once,
    Locked,
}

/// <summary>
/// Teclado virtual operável apenas com direções, confirmar e voltar. Modelo puro, sem UI.
/// Para senhas, o conteúdo vive em um buffer de caracteres zerado ao concluir/cancelar.
/// </summary>
public sealed class VirtualKeyboard
{
    private const string FileNameForbidden = "\\/:*?\"<>|";
    private char[] _buffer;
    private int _length;
    private readonly Func<string, string?>? _validator;
    private VirtualKey? _confirmedKey;
    private int? _anchor;

    public VirtualKeyboard(TextFieldKind kind, string title, string initialText = "", KeyboardLanguage language = KeyboardLanguage.PortugueseBrazil,
        Func<string, string?>? validator = null, int? maxLength = null, int? initialCaret = null, (int Start, int Length)? initialSelection = null)
    {
        Kind = kind;
        Title = title;
        Language = language;
        _validator = validator;
        MaxLength = maxLength ?? kind switch
        {
            TextFieldKind.FileName => WindowsNameRules.MaxComponentLength,
            TextFieldKind.Path => 4096,
            TextFieldKind.Password => 512,
            _ => 1024,
        };
        _buffer = new char[Math.Max(16, initialText.Length)];
        initialText.AsSpan().CopyTo(_buffer);
        _length = initialText.Length;
        Caret = Math.Clamp(initialCaret ?? _length, 0, _length);
        if (initialSelection is { } selection) Select(selection.Start, selection.Length);
        Rebuild();
        Row = FirstLetterRow();
    }

    /// <summary>
    /// Foco inicial: a primeira tecla de letra (o "q"), não a linha de números — quem abre o teclado quase sempre começa
    /// por uma letra (auditoria de UX, P2-11).
    /// </summary>
    private int FirstLetterRow()
    {
        for (var r = 0; r < Rows.Count; r++)
            if (Rows[r] is [{ Kind: KeyKind.Character, Text: [var c] }, ..] && char.IsLetter(c)) return r;
        return 0;
    }

    public TextFieldKind Kind { get; }
    public string Title { get; }
    public KeyboardLanguage Language { get; private set; }
    public KeyboardPage Page { get; private set; } = KeyboardPage.Letters;
    public ShiftState Shift { get; private set; }
    public int MaxLength { get; }
    public int Caret { get; private set; }
    public int Row { get; private set; }
    public int Column { get; private set; }
    public bool IsRevealed { get; private set; }
    public string? ErrorMessage { get; private set; }
    public KeyboardOutcome Outcome { get; private set; }
    public IReadOnlyList<IReadOnlyList<VirtualKey>> Rows { get; private set; } = [];
    public int Length => _length;

    /// <summary>Início do trecho selecionado (o cursor fica no fim dele). Sem seleção, igual a <see cref="Caret"/>.</summary>
    public int SelectionStart => _anchor is { } anchor ? Math.Min(anchor, Caret) : Caret;

    /// <summary>Tamanho do trecho selecionado; 0 quando não há seleção.</summary>
    public int SelectionLength => _anchor is { } anchor ? Math.Abs(Caret - anchor) : 0;

    public bool HasSelection => SelectionLength > 0;

    public VirtualKey FocusedKey => Rows[Row][Column];

    private Func<string, IReadOnlyList<string>>? _suggestionSource;
    private string? _suggestionsFor;
    private IReadOnlyList<string> _suggestions = [];

    /// <summary>
    /// Fonte de sugestões locais para o texto atual (null = sem sugestões). Campos de senha nunca usam sugestões:
    /// atribuir uma fonte a eles não tem efeito.
    /// </summary>
    public Func<string, IReadOnlyList<string>>? SuggestionSource
    {
        get => _suggestionSource;
        set
        {
            _suggestionSource = Kind == TextFieldKind.Password ? null : value;
            _suggestionsFor = null;
            ClampSuggestionFocus();
        }
    }

    /// <summary>Sugestões para o texto atual (sem repetir o próprio texto). Sempre vazia em campos de senha.</summary>
    public IReadOnlyList<string> Suggestions
    {
        get
        {
            if (_suggestionSource is null) return [];
            var text = Text;
            if (_suggestionsFor != text)
            {
                _suggestionsFor = text;
                _suggestions = [.. _suggestionSource(text).Where(s => !string.Equals(s, text, StringComparison.Ordinal)).Take(MaxSuggestions)];
            }
            return _suggestions;
        }
    }

    public const int MaxSuggestions = 5;

    /// <summary>Sugestão focada na faixa acima das teclas; null quando o foco está nas teclas.</summary>
    public int? SuggestionIndex { get; private set; }

    public string? FocusedSuggestion => SuggestionIndex is { } i && i < Suggestions.Count ? Suggestions[i] : null;

    /// <summary>Troca o texto inteiro pela sugestão focada e devolve o foco às teclas.</summary>
    public void ApplySuggestion()
    {
        if (FocusedSuggestion is not { } suggestion || Outcome != KeyboardOutcome.None) return;
        SelectAll();
        InsertText(suggestion);
        SuggestionIndex = null;
        Row = 0;
    }

    /// <summary>Foca uma sugestão (toque/clique na faixa).</summary>
    public void FocusSuggestion(int index)
    {
        if (index >= 0 && index < Suggestions.Count) SuggestionIndex = index;
    }

    private void ClampSuggestionFocus()
    {
        if (SuggestionIndex is { } i && Suggestions.Count == 0) SuggestionIndex = null;
        else if (SuggestionIndex is { } j) SuggestionIndex = Math.Min(j, Suggestions.Count - 1);
    }

    /// <summary>Texto para exibição. Senhas são mascaradas, exceto durante revelação explícita.</summary>
    public string DisplayText => Kind == TextFieldKind.Password && !IsRevealed ? new string('•', _length) : new string(_buffer, 0, _length);

    /// <summary>Texto real. Para senhas, prefira <see cref="TakeSecret"/>.</summary>
    public string Text => new(_buffer, 0, _length);

    public bool IsKeyEnabled(VirtualKey key) => DisabledReason(key) is null;

    public string? DisabledReason(VirtualKey key)
    {
        if (key.Kind != KeyKind.Character || key.Text is null) return null;
        if (Kind == TextFieldKind.FileName && key.Text.Length == 1 && FileNameForbidden.Contains(key.Text[0], StringComparison.Ordinal))
            return $"\"{key.Text}\" não é permitido em nomes de arquivo do Windows.";
        return null;
    }

    public string DisplayLabel(VirtualKey key) => key.Kind == KeyKind.Character && key.Text is not null && Shift != ShiftState.Off
        ? key.Label.ToUpper(CultureInfo.CurrentCulture)
        : key.Label;

    /// <summary>
    /// Ações de edição seguras para repetir enquanto o botão fica pressionado: apagar e mover o cursor, inclusive
    /// Confirmar mantido sobre ⌫/◀/▶ (desde que o foco não tenha mudado desde o toque). Concluir, Cancelar e
    /// caracteres nunca repetem.
    /// </summary>
    public bool IsRepeatable(InputAction action) => Outcome == KeyboardOutcome.None && action switch
    {
        InputAction.ToggleSelection or InputAction.PreviousRegion or InputAction.NextRegion => true,
        InputAction.Confirm => _confirmedKey is { Kind: KeyKind.Backspace or KeyKind.CaretLeft or KeyKind.CaretRight } key
            && ReferenceEquals(key, FocusedKey),
        _ => false,
    };

    /// <summary>Processa uma ação semântica. Retorna true se consumida.</summary>
    public bool Handle(InputAction action)
    {
        if (Outcome != KeyboardOutcome.None) return false;
        _confirmedKey = action == InputAction.Confirm && SuggestionIndex is null ? FocusedKey : null;
        ClampSuggestionFocus();
        if (SuggestionIndex is { } index)
        {
            // Faixa de sugestões: esquerda/direita escolhem, Sul usa, baixo volta à primeira linha e cima segue a volta até a
            // última (como antes da faixa existir); o resto age como nas teclas.
            switch (action)
            {
                case InputAction.NavigateLeft: SuggestionIndex = (index - 1 + Suggestions.Count) % Suggestions.Count; return true;
                case InputAction.NavigateRight: SuggestionIndex = (index + 1) % Suggestions.Count; return true;
                case InputAction.NavigateDown: SuggestionIndex = null; Row = 0; return true;
                case InputAction.NavigateUp: SuggestionIndex = null; Row = 0; MoveVertical(-1); return true; // continua a volta até a última linha
                case InputAction.Confirm: ApplySuggestion(); return true;
            }
        }
        else if (action == InputAction.NavigateUp && Row == 0 && Suggestions.Count > 0)
        {
            SuggestionIndex = 0;
            return true;
        }
        switch (action)
        {
            case InputAction.NavigateUp: MoveVertical(-1); return true;
            case InputAction.NavigateDown: MoveVertical(1); return true;
            case InputAction.NavigateLeft: MoveHorizontal(-1); return true;
            case InputAction.NavigateRight: MoveHorizontal(1); return true;
            case InputAction.Confirm: Press(FocusedKey); return true;
            case InputAction.Back: Cancel(); return true;
            case InputAction.ToggleSelection: Backspace(); return true;
            case InputAction.OpenContextMenu: ToggleShift(); return true;
            case InputAction.PreviousRegion: MoveCaret(-1); return true;
            case InputAction.NextRegion: MoveCaret(1); return true;
            case InputAction.PageUp: MoveCaretToStart(); return true;
            case InputAction.PageDown: MoveCaretToEnd(); return true;
            case InputAction.OpenAppMenu: Submit(); return true;
            case InputAction.Search: SetPage(Page == KeyboardPage.Letters ? KeyboardPage.Symbols : KeyboardPage.Letters); return true;
            default: return false;
        }
    }

    public void Press(VirtualKey key)
    {
        if (Outcome != KeyboardOutcome.None) return;
        var reason = DisabledReason(key);
        if (reason is not null)
        {
            ErrorMessage = reason;
            return;
        }
        switch (key.Kind)
        {
            case KeyKind.Character:
            case KeyKind.Space:
                var text = key.Text ?? string.Empty;
                if (Shift != ShiftState.Off) text = text.ToUpper(CultureInfo.CurrentCulture);
                InsertText(text);
                if (Shift == ShiftState.Once) Shift = ShiftState.Off;
                break;
            case KeyKind.Shift: ToggleShift(); break;
            case KeyKind.Backspace: Backspace(); break;
            case KeyKind.Clear: Clear(); break;
            case KeyKind.SelectAll: SelectAll(); break;
            case KeyKind.CaretLeft: MoveCaret(-1); break;
            case KeyKind.CaretRight: MoveCaret(1); break;
            case KeyKind.PageLetters: SetPage(KeyboardPage.Letters); break;
            case KeyKind.PageSymbols: SetPage(KeyboardPage.Symbols); break;
            case KeyKind.PageAccents: SetPage(KeyboardPage.Accents); break;
            case KeyKind.SwitchLanguage:
                Language = Language == KeyboardLanguage.PortugueseBrazil ? KeyboardLanguage.English : KeyboardLanguage.PortugueseBrazil;
                Rebuild();
                break;
            case KeyKind.Reveal: IsRevealed = !IsRevealed; break;
            case KeyKind.Done: Submit(); break;
            case KeyKind.Cancel: Cancel(); break;
        }
    }

    /// <summary>Entrada de texto (teclas virtuais ou teclado físico). Caracteres proibidos no tipo de campo são recusados com mensagem.</summary>
    public void InsertText(string text)
    {
        if (Outcome != KeyboardOutcome.None || text.Length == 0) return;
        foreach (var c in text)
        {
            if (char.IsControl(c)) continue;
            if (Kind == TextFieldKind.FileName && FileNameForbidden.Contains(c, StringComparison.Ordinal))
            {
                ErrorMessage = $"\"{c}\" não é permitido em nomes de arquivo do Windows.";
                return;
            }
        }
        if (_length - SelectionLength + text.Length > MaxLength)
        {
            ErrorMessage = $"Limite de {MaxLength} caracteres.";
            return;
        }
        DeleteSelection();
        EnsureCapacity(_length + text.Length);
        Array.Copy(_buffer, Caret, _buffer, Caret + text.Length, _length - Caret);
        text.AsSpan().CopyTo(_buffer.AsSpan(Caret));
        _length += text.Length;
        Caret += text.Length;
        ErrorMessage = null;
        if (Kind == TextFieldKind.Password) IsRevealed = false;
    }

    public void Backspace()
    {
        if (DeleteSelection())
        {
            ErrorMessage = null;
            return;
        }
        if (Caret == 0) return;
        // Remove um par substituto inteiro, se houver.
        var remove = Caret >= 2 && char.IsLowSurrogate(_buffer[Caret - 1]) && char.IsHighSurrogate(_buffer[Caret - 2]) ? 2 : 1;
        Array.Copy(_buffer, Caret, _buffer, Caret - remove, _length - Caret);
        _length -= remove;
        Caret -= remove;
        Array.Clear(_buffer, _length, remove);
        ErrorMessage = null;
    }

    public void Clear()
    {
        Array.Clear(_buffer);
        _length = 0;
        Caret = 0;
        _anchor = null;
        ErrorMessage = null;
    }

    /// <summary>Seleciona um trecho (o cursor vai para o fim dele). Digitar substitui o trecho; ⌫ o apaga; mover o cursor desfaz a seleção.</summary>
    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, _length);
        var end = Math.Clamp(start + Math.Max(0, length), start, _length);
        if (start > 0 && start < _length && char.IsLowSurrogate(_buffer[start])) start--;
        if (end < _length && char.IsLowSurrogate(_buffer[end])) end++;
        Caret = end;
        _anchor = end > start ? start : null;
    }

    /// <summary>Seleciona todo o texto. Não altera página nem maiúsculas.</summary>
    public void SelectAll() => Select(0, _length);

    public void MoveCaret(int delta)
    {
        if (HasSelection)
        {
            // Com seleção, ◀/▶ só a desfazem, deixando o cursor na borda correspondente (como em editores de texto).
            Caret = delta < 0 ? SelectionStart : SelectionStart + SelectionLength;
            _anchor = null;
            return;
        }
        var target = Math.Clamp(Caret + delta, 0, _length);
        if (delta < 0 && target > 0 && char.IsLowSurrogate(_buffer[target])) target--;
        if (delta > 0 && target < _length && char.IsLowSurrogate(_buffer[target])) target++;
        Caret = target;
    }

    /// <summary>Leva o cursor ao início do texto. Não altera texto, página nem maiúsculas.</summary>
    public void MoveCaretToStart()
    {
        _anchor = null;
        Caret = 0;
    }

    /// <summary>Leva o cursor ao fim do texto. Não altera texto, página nem maiúsculas.</summary>
    public void MoveCaretToEnd()
    {
        _anchor = null;
        Caret = _length;
    }

    public void Submit()
    {
        if (Outcome != KeyboardOutcome.None) return;
        var error = ValidateCurrent();
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }
        Outcome = KeyboardOutcome.Submitted;
        IsRevealed = false;
    }

    public void Cancel()
    {
        Outcome = KeyboardOutcome.Cancelled;
        IsRevealed = false;
        if (Kind == TextFieldKind.Password) Clear();
    }

    /// <summary>Entrega a senha e zera o buffer. Após a chamada, o teclado não guarda o segredo.</summary>
    public string TakeSecret()
    {
        var secret = new string(_buffer, 0, _length);
        Clear();
        return secret;
    }

    /// <summary>Foco direto em uma tecla (mouse/toque). O acionamento continua passando por <see cref="Handle"/>.</summary>
    public void FocusKey(int row, int column)
    {
        SuggestionIndex = null;
        Row = Math.Clamp(row, 0, Rows.Count - 1);
        Column = Math.Clamp(column, 0, Rows[Row].Count - 1);
    }

    /// <summary>
    /// Centro da tecla focada na grade (x em colunas de <see cref="VirtualKeyboardLayouts.Columns"/>, y em linhas), de
    /// onde o ponteiro do giroscópio (#77) parte.
    /// </summary>
    public (double X, double Y) FocusCenter
    {
        get
        {
            if (Rows.Count == 0) return (0, 0);
            var (start, end) = ColumnRange(Row, Column);
            return ((start + end) / 2.0, Row + 0.5);
        }
    }

    /// <summary>Tecla sob um ponto da grade (fora dela, a tecla da borda mais próxima; nunca dá a volta).</summary>
    public (int Row, int Column) KeyAt(double x, double y)
    {
        if (Rows.Count == 0) return (0, 0);
        var row = Math.Clamp((int)Math.Floor(y), 0, Rows.Count - 1);
        return (row, KeyAtColumn(row, Math.Max(0, (int)Math.Floor(x))));
    }

    public void SetExternalError(string message) => ErrorMessage = message;

    /// <summary>Permite nova tentativa após um erro externo (ex.: nome já existe).</summary>
    public void Reopen(string? error)
    {
        Outcome = KeyboardOutcome.None;
        ErrorMessage = error;
    }

    /// <summary>Apaga o trecho selecionado, se houver. Retorna true se algo foi apagado.</summary>
    private bool DeleteSelection()
    {
        var start = SelectionStart;
        var count = SelectionLength;
        _anchor = null;
        if (count == 0) return false;
        Array.Copy(_buffer, start + count, _buffer, start, _length - start - count);
        _length -= count;
        Array.Clear(_buffer, _length, count);
        Caret = start;
        return true;
    }

    private string? ValidateCurrent()
    {
        var text = Text;
        if (Kind == TextFieldKind.FileName)
        {
            var result = WindowsNameRules.ValidateComponent(text);
            if (!result.IsValid) return result.Message;
        }
        return _validator?.Invoke(text);
    }

    private void ToggleShift() => Shift = Shift switch
    {
        ShiftState.Off => ShiftState.Once,
        ShiftState.Once => ShiftState.Locked,
        _ => ShiftState.Off,
    };

    private void SetPage(KeyboardPage page)
    {
        Page = page;
        Rebuild();
    }

    private void Rebuild()
    {
        var (startColumn, _) = ColumnRange(Row, Column);
        Rows = VirtualKeyboardLayouts.Build(Page, Language, Kind == TextFieldKind.Password);
        Row = Math.Clamp(Row, 0, Rows.Count - 1);
        Column = KeyAtColumn(Row, startColumn);
    }

    private void MoveHorizontal(int delta)
    {
        var count = Rows[Row].Count;
        Column = ((Column + delta) % count + count) % count;
    }

    private void MoveVertical(int delta)
    {
        var (start, _) = ColumnRange(Row, Column);
        var count = Rows.Count;
        Row = ((Row + delta) % count + count) % count;
        Column = KeyAtColumn(Row, start);
    }

    private (int Start, int End) ColumnRange(int row, int column)
    {
        if (Rows.Count == 0) return (0, 1);
        var start = 0;
        for (var i = 0; i < column; i++) start += Rows[row][i].Span;
        return (start, start + Rows[row][column].Span);
    }

    private int KeyAtColumn(int row, int gridColumn)
    {
        var position = 0;
        var keys = Rows[row];
        for (var i = 0; i < keys.Count; i++)
        {
            position += keys[i].Span;
            if (gridColumn < position) return i;
        }
        return keys.Count - 1;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length) return;
        var bigger = new char[Math.Max(needed, _buffer.Length * 2)];
        Array.Copy(_buffer, bigger, _length);
        Array.Clear(_buffer);
        _buffer = bigger;
    }
}
