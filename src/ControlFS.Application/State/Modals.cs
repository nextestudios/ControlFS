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

/// <summary>
/// Assistente de mapeamento de um joystick sem perfil. Os eventos crus do próprio joystick conduzem os passos; o
/// teclado (ou outro controle) pula, refaz e cancela. No teste, o rascunho já comanda as opções abaixo.
/// </summary>
public sealed class MappingWizardModal(Core.Contracts.InputDeviceInfo device, Core.Input.Mapping.ControllerMappingWizard wizard) : Modal("Configurar controle")
{
    public static IReadOnlyList<string> ReviewOptions { get; } = ["Salvar perfil", "Refazer um passo…", "Cancelar sem salvar"];

    public Core.Contracts.InputDeviceInfo Device { get; } = device;
    public Core.Input.Mapping.ControllerMappingWizard Wizard { get; } = wizard;
    public int ReviewFocus { get; internal set; }

    /// <summary>Outro controle não assume no meio do mapeamento.</summary>
    public override bool IsSensitive => true;
}

/// <summary>Um dispositivo na tela de teste: número estável na sessão e se ainda está conectado.</summary>
public sealed record ControllerTestDevice(int Number, Core.Contracts.InputDeviceInfo Info, bool IsConnected, bool IsActive, string? Profile);

/// <summary>
/// Uma pressão registrada no teste: o controle físico (gamepad ou perfil) e/ou a entrada crua, e a ação semântica que
/// produziu (null: nenhuma — joystick sem perfil, outro controle ativo ou botão sem função).
/// </summary>
public sealed record ControllerTestLine(int Device, PhysicalControl? Control, Core.Input.Mapping.RawInputEvent? Raw, InputAction? Action, Core.Input.ControllerFamily Family);

/// <summary>
/// Tela "Teste de controles" (#78): lista os controles conectados e mostra, ao vivo, cada botão/eixo e a ação que ele
/// produziu. Não executa nada: no controle, segurar Confirmar copia o relatório e segurar Voltar sai; no teclado, Enter e Esc.
/// </summary>
public sealed class ControllerTestModal() : Modal("Teste de controles")
{
    public const int MaxLines = 300;

    internal Dictionary<string, (int Number, Core.Contracts.InputDeviceInfo Info)> Seen { get; } = new(StringComparer.Ordinal);
    internal Dictionary<(string Device, int Axis), int> AxisBuckets { get; } = [];
    public List<ControllerTestLine> Lines { get; } = [];
    public string? Notice { get; internal set; }

    /// <summary>Pressão em curso vinda da camada de entrada (preenchida antes de o roteador emitir a ação).</summary>
    internal PendingTestInput? Pending { get; set; }

    /// <summary>Confirmar/Voltar mantidos: ao completar o tempo, copiam o relatório ou saem.</summary>
    internal (string Device, PhysicalControl Control, InputAction Action, TimeSpan Since)? Hold { get; set; }
}

internal sealed record PendingTestInput(string DeviceKey, int Device, PhysicalControl Control, Core.Input.Mapping.RawInputEvent? Raw, Core.Input.ControllerFamily Family)
{
    public bool Handled { get; set; }
}
