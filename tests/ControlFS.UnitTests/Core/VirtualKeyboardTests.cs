using ControlFS.Core.Actions;
using ControlFS.Core.Text;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Core;

public class VirtualKeyboardTests
{
    [Fact]
    public void Types_accented_name_using_only_directions_and_confirm()
    {
        var kb = new VirtualKeyboard(TextFieldKind.FileName, "Nome", "");
        KeyboardDriver.Type(a => kb.Handle(a), () => kb, "Ação 1");
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.Done);
        Assert.Equal(KeyboardOutcome.Submitted, kb.Outcome);
        Assert.Equal("Ação 1", kb.Text);
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
        // Linha de letras "q" (coluna 0) -> descer até a barra de espaço e voltar.
        for (var i = 0; i < 4; i++) kb.Handle(InputAction.NavigateDown);
        Assert.Equal(KeyKind.PageSymbols, kb.FocusedKey.Kind);
        for (var i = 0; i < 3; i++) kb.Handle(InputAction.NavigateRight);
        Assert.Equal(".", kb.FocusedKey.Text);
        kb.Handle(InputAction.NavigateUp);
        Assert.Equal("b", kb.FocusedKey.Text); // coluna 5 na linha "⇧zxcvbnm-_"
    }

    [Fact]
    public void Switching_language_changes_layout()
    {
        var kb = new VirtualKeyboard(TextFieldKind.Generic, "t");
        Assert.Contains(kb.Rows[2], k => k.Text == "ç");
        KeyboardDriver.Press(a => kb.Handle(a), () => kb, k => k.Kind == KeyKind.SwitchLanguage);
        Assert.Equal(KeyboardLanguage.English, kb.Language);
        Assert.DoesNotContain(kb.Rows[2], k => k.Text == "ç");
    }
}
