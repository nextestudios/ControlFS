using ControlFS.Core.Actions;
using ControlFS.Core.Input;
using ControlFS.Core.Text;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Core;

public class VirtualKeyboardTests
{
    [Fact]
    public void Types_accented_name_using_only_directions_and_confirm()
    {
        var kb = new VirtualKeyboard(TextFieldKind.FileName, "Nome", "");
        KeyboardDriver.Type(a => kb.Handle(a), () => kb, "Relatório ação 2026");
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.Done);
        Assert.Equal(KeyboardOutcome.Submitted, kb.Outcome);
        Assert.Equal("Relatório ação 2026", kb.Text);
    }

    [Fact]
    public void Shift_once_then_lock()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t");
        kb.Handle(InputAction.OpenContextMenu);
        Assert.Equal(ShiftState.Once, kb.Shift);
        kb.Press(VirtualKey.Character("a"));
        kb.Press(VirtualKey.Character("b"));
        Assert.Equal("Ab", kb.Text);
        kb.Handle(InputAction.OpenContextMenu);
        kb.Handle(InputAction.OpenContextMenu);
        Assert.Equal(ShiftState.Locked, kb.Shift);
        kb.Press(VirtualKey.Character("c"));
        kb.Press(VirtualKey.Character("ç"));
        Assert.Equal("AbCÇ", kb.Text);
    }

    [Fact]
    public void Caret_insert_and_backspace_in_the_middle()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t", "arquivo.txt", initialCaret: 7);
        kb.InsertText("_v2");
        Assert.Equal("arquivo_v2.txt", kb.Text);
        kb.Handle(InputAction.PreviousRegion);
        kb.Handle(InputAction.ToggleSelection);
        Assert.Equal("arquivo_2.txt", kb.Text);
        Assert.Equal(8, kb.Caret);
    }

    [Fact]
    public void Holding_backspace_deletes_continuously_and_stops_on_release()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t", new string('a', 200));
        var router = new InputRouter(new ActionMap(ConfirmBackConvention.SouthConfirms), InputSettings.Default, a => kb.Handle(a))
        {
            RepeatPolicy = a => a.IsRepeatable() || kb.IsRepeatable(a),
        };
        static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

        router.OnControl("p1", PhysicalControl.West, true, Ms(0));
        for (var t = 0; t <= 2000; t += 5) router.Tick(Ms(t));
        router.OnControl("p1", PhysicalControl.West, false, Ms(2001));
        var afterHold = kb.Length;
        Assert.True(afterHold < 200 - 20, $"restaram {afterHold}");
        for (var t = 2005; t <= 4000; t += 5) router.Tick(Ms(t));
        Assert.Equal(afterHold, kb.Length); // parou ao soltar

        // Sul mantido sobre ⌫ também repete; sobre um caractere, digita uma vez só.
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.Backspace);
        var afterTap = kb.Length;
        router.OnControl("p1", PhysicalControl.South, true, Ms(5000));
        for (var t = 5000; t <= 6000; t += 5) router.Tick(Ms(t));
        router.OnControl("p1", PhysicalControl.South, false, Ms(6001));
        Assert.True(kb.Length < afterTap - 5, $"restaram {kb.Length}");

        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.Character && k.Text == "q");
        var beforeLetter = kb.Length;
        router.OnControl("p1", PhysicalControl.South, true, Ms(7000));
        for (var t = 7000; t <= 9000; t += 5) router.Tick(Ms(t));
        Assert.Equal(beforeLetter + 1, kb.Length);
    }

    [Fact]
    public void Jumps_to_start_and_end_and_types_at_the_caret_without_touching_page_or_shift()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t", "ControlFS" + new string('x', 200));
        kb.Handle(InputAction.OpenContextMenu); // Maiúsculas uma vez
        kb.Handle(InputAction.Search); // página de símbolos
        kb.Handle(InputAction.PageUp); // LT
        Assert.Equal(0, kb.Caret);
        kb.Handle(InputAction.PageDown); // RT
        Assert.Equal(kb.Length, kb.Caret);
        kb.Handle(InputAction.PageUp);
        for (var i = 0; i < 7; i++) kb.Handle(InputAction.NextRegion);
        Assert.Equal(ShiftState.Once, kb.Shift);
        Assert.Equal(KeyboardPage.Symbols, kb.Page);
        Assert.Equal(209, kb.Length);

        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.Character && k.Text == "-");
        Assert.StartsWith("Control-FS", kb.Text);
        Assert.Equal(8, kb.Caret);
    }

    [Fact]
    public void Backspace_removes_whole_surrogate_pair()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t", "a😀");
        kb.Backspace();
        Assert.Equal("a", kb.Text);
    }

    [Fact]
    public void File_name_field_disables_forbidden_keys_but_path_field_allows_them()
    {
        var name = new VirtualKeyboard(TextFieldKind.FileName, "n");
        var path = new VirtualKeyboard(TextFieldKind.Path, "p");
        var slash = VirtualKey.Character("\\");
        Assert.False(name.IsKeyEnabled(slash));
        name.Press(slash);
        Assert.Equal("", name.Text);
        Assert.NotNull(name.ErrorMessage);
        Assert.True(path.IsKeyEnabled(slash));
        path.InsertText("C:\\Users\\x");
        Assert.Equal("C:\\Users\\x", path.Text);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nome.")]
    [InlineData("")]
    public void Submit_validates_windows_name_rules(string text)
    {
        var kb = new VirtualKeyboard(TextFieldKind.FileName, "n", text);
        kb.Submit();
        Assert.Equal(KeyboardOutcome.None, kb.Outcome);
        Assert.NotNull(kb.ErrorMessage);
    }

    [Fact]
    public void Password_is_masked_revealed_explicitly_and_wiped_after_take()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Password, "Senha");
        kb.InsertText("s3nh@");
        Assert.Equal("•••••", kb.DisplayText);
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.Reveal);
        Assert.Equal("s3nh@", kb.DisplayText);
        kb.InsertText("!");
        Assert.Equal("••••••", kb.DisplayText); // revelação termina ao digitar
        Assert.Equal("s3nh@!", kb.TakeSecret());
        Assert.Equal(0, kb.Length);
    }

    [Fact]
    public void Cancel_discards_without_applying()
    {
        var kb = new VirtualKeyboard(TextFieldKind.FileName, "n", "original");
        kb.InsertText("XYZ");
        kb.Handle(InputAction.Back);
        Assert.Equal(KeyboardOutcome.Cancelled, kb.Outcome);
    }

    [Fact]
    public void Vertical_navigation_keeps_column_across_wide_keys()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t");
        // Coluna 7 ("8") desce pelas letras até a barra de espaço (colunas 6-8) e segue para Concluir (colunas 6-10).
        for (var i = 0; i < 7; i++) kb.Handle(InputAction.NavigateRight);
        for (var i = 0; i < 4; i++) kb.Handle(InputAction.NavigateDown);
        Assert.Equal(KeyKind.Space, kb.FocusedKey.Kind);
        kb.Handle(InputAction.NavigateDown);
        Assert.Equal(KeyKind.Done, kb.FocusedKey.Kind);
        kb.Handle(InputAction.NavigateUp);
        kb.Handle(InputAction.NavigateUp);
        Assert.Equal("m", kb.FocusedKey.Text); // coluna 6, início da barra de espaço, na linha "zxcvbnm,._!"
    }

    [Fact]
    public void Every_page_fills_the_grid_and_keeps_the_function_and_bottom_rows()
    {
        foreach (var page in Enum.GetValues<KeyboardPage>())
        foreach (var password in new[] { false, true })
        {
            var rows = VirtualKeyboardLayouts.Build(page, KeyboardLanguage.PortugueseBrazil, password);
            Assert.Equal(6, rows.Count);
            Assert.All(rows, r => Assert.Equal(VirtualKeyboardLayouts.Columns, r.Sum(k => k.Span)));
            Assert.Equal([KeyKind.Shift, KeyKind.PageLetters, KeyKind.PageSymbols, KeyKind.Space, KeyKind.Backspace], rows[4].Select(k => k.Kind));
            Assert.Equal(KeyKind.Done, rows[5][^1].Kind);
            Assert.Equal(password, rows[5].Any(k => k.Kind == KeyKind.Reveal));
        }
    }

    [Fact]
    public void Switching_language_changes_layout()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t");
        Assert.Contains(kb.Rows[2], k => k.Text == "ç");
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.SwitchLanguage); // fica na página "…"
        Assert.Equal(KeyboardLanguage.English, kb.Language);
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.PageLetters);
        Assert.DoesNotContain(kb.Rows[2], k => k.Text == "ç");
    }
}
