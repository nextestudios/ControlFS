using ControlFS.Core.Actions;
using ControlFS.Core.Text;

namespace ControlFS.Application.State;

/// <summary>Um modal cria um escopo exclusivo de entrada: somente o topo da pilha recebe ações.</summary>
public abstract class Modal(string title)
{
    public string Title { get; } = title;

    /// <summary>Confirmações sensíveis bloqueiam troca automática de dispositivo ativo.</summary>
    public virtual bool IsSensitive => false;
}

public sealed record MenuItem(string Label, Action? Execute, string? DisabledReason = null, string? Detail = null)
{
    public bool IsEnabled => Execute is not null && DisabledReason is null;
}

public sealed class MenuModal(string title, IReadOnlyList<MenuItem> items) : Modal(title)
{
    public IReadOnlyList<MenuItem> Items { get; } = items;
    public int FocusIndex { get; internal set; }
}

public sealed class KeyboardModal(VirtualKeyboard keyboard, Func<VirtualKeyboard, Task> onSubmit, Action? onCancel = null) : Modal(keyboard.Title)
{
    public VirtualKeyboard Keyboard { get; } = keyboard;
    internal Func<VirtualKeyboard, Task> OnSubmit { get; } = onSubmit;
    internal Action? OnCancel { get; } = onCancel;
    public bool IsBusy { get; internal set; }
}

public enum DialogOptionKind
{
    Primary,
    Safe,
    Danger,
    Toggle,
}

public sealed class DialogOption(string label, DialogOptionKind kind, Action execute)
{
    public string Label { get; internal set; } = label;
    public DialogOptionKind Kind { get; } = kind;
    internal Action Execute { get; } = execute;
    public bool IsChecked { get; internal set; }
}

/// <summary>
/// Diálogo com linhas de informação e opções. <see cref="BackOption"/> é a opção executada por "Voltar"
/// (sempre segura). Diálogos destrutivos iniciam o foco na opção segura.
/// </summary>
public sealed class DialogModal(string title, IReadOnlyList<(string Label, string Value)> lines, bool sensitive = false) : Modal(title)
{
    public IReadOnlyList<(string Label, string Value)> Lines { get; internal set; } = lines;
    public List<DialogOption> Options { get; } = [];
    public int FocusIndex { get; internal set; }
    public string? Message { get; internal set; }
    internal DialogOption? BackOption { get; set; }
    public override bool IsSensitive => sensitive;
}

/// <summary>Tela "Sobre": logo, versão, licença e origem do código. Fecha com Confirmar ou Voltar.</summary>
public sealed class AboutModal(string version, IReadOnlyList<(string Label, string Value)> lines) : Modal("Sobre o ControlFS")
{
    public string Version { get; } = version;
    public IReadOnlyList<(string Label, string Value)> Lines { get; } = lines;
}

public sealed record Hint(InputAction Action, string Label);
