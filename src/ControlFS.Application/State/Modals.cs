using ControlFS.Core.Actions;
using ControlFS.Core.Text;

namespace ControlFS.Application.State;

/// <summary>Um modal cria um escopo exclusivo de entrada: somente o topo da pilha recebe ações.</summary>
public abstract class Modal(string title)
{
    public string Title { get; } = title;

    /// <summary>Linha de contexto sob o título (ex.: tipo do item cujas ações o menu mostra). Opcional.</summary>
    public string? Subtitle { get; internal set; }

    /// <summary>Ícone do cabeçalho (o item, o tom de um aviso ou erro). <see cref="ActionIcon.None"/>: só o título.</summary>
    public virtual ActionIcon Icon { get; internal set; }

    /// <summary>Confirmações sensíveis bloqueiam troca automática de dispositivo ativo.</summary>
    public virtual bool IsSensitive => false;
}

/// <summary>Onde uma opção aparece no menu (#193): na grade de ações rápidas do topo ou na lista abaixo dela.</summary>
public enum MenuPlacement
{
    List,

    /// <summary>Ação frequente: bloco com ícone e rótulo curto na grade do topo (como o menu de contexto do Windows 11).</summary>
    Quick,
}

/// <summary>
/// Opção de menu. <paramref name="Icon"/> diz o que a ação faz (a tela desenha o símbolo ao lado do texto);
/// <paramref name="Section"/> agrupa opções: quando muda de um item para o seguinte, a tela desenha um separador com o
/// título do grupo (vazio: só a linha). <paramref name="Placement"/> põe a opção na grade de ações rápidas, com
/// <paramref name="ShortLabel"/> sob o ícone (o Narrador e o foco sempre usam <paramref name="Label"/> por extenso).
/// <paramref name="KeepOpen"/>: a opção muda um ajuste e o menu continua aberto, com os textos atualizados.
/// </summary>
public sealed record MenuItem(string Label, Action? Execute, string? DisabledReason = null, string? Detail = null,
    ActionIcon Icon = ActionIcon.None, string? Section = null, MenuPlacement Placement = MenuPlacement.List, string? ShortLabel = null,
    bool KeepOpen = false)
{
    public bool IsEnabled => Execute is not null && DisabledReason is null;

    /// <summary>Apaga arquivos ou listas: desenhado em vermelho com alerta e nunca é o foco inicial.</summary>
    public bool IsDestructive => ActionIcons.IsDestructive(Icon);

    public bool IsQuick => Placement == MenuPlacement.Quick;

    /// <summary>Texto sob o ícone na grade: o curto escolhido ou o rótulo sem as reticências.</summary>
    public string TileLabel => ShortLabel ?? Label.TrimEnd('…');
}

/// <summary>
/// Menu com ações rápidas (grade no topo, <see cref="QuickCount"/> primeiros itens de <see cref="Items"/>) e a lista das
/// demais. A ordem de quem montou o menu vale dentro de cada parte; o foco inicial é o primeiro item que ele passou (ou
/// <see cref="FocusOn"/>), nunca uma ação perigosa (AppController.SafeInitialFocus).
/// </summary>
public sealed class MenuModal : Modal
{
    /// <summary>Máximo de blocos por linha da grade.</summary>
    public const int MaxQuickColumns = 4;

    public MenuModal(string title, IReadOnlyList<MenuItem> items) : base(title)
    {
        Icon = ActionIcon.Menu;
        Arrange(items);
        FocusIndex = items.Count == 0 ? 0 : IndexOf(items[0]);
    }

    public IReadOnlyList<MenuItem> Items { get; private set; } = [];
    public int FocusIndex { get; internal set; }

    /// <summary>Quantos itens (do início de <see cref="Items"/>) são blocos da grade de ações rápidas.</summary>
    public int QuickCount { get; private set; }

    /// <summary>
    /// Blocos por linha: até 4 numa linha só; mais que isso, duas (ou mais) linhas equilibradas de no máximo 4 (os rótulos
    /// curtos cabem inteiros, legíveis de longe).
    /// </summary>
    public int QuickColumns => QuickCount <= MaxQuickColumns ? QuickCount : Math.Min(MaxQuickColumns, (QuickCount + 1) / 2);

    public int QuickRows => QuickCount == 0 ? 0 : (QuickCount + QuickColumns - 1) / QuickColumns;

    public bool IsQuick(int index) => index >= 0 && index < QuickCount;

    /// <summary>Coluna da grade de onde o foco saiu para a lista: Cima volta a ela (navegação previsível).</summary>
    internal int GridColumn { get; set; }

    /// <summary>Remonta as opções depois de uma opção <see cref="MenuItem.KeepOpen"/> (ex.: Configurações).</summary>
    internal Func<IReadOnlyList<MenuItem>>? Reload { get; init; }

    /// <summary>Foco inicial numa opção específica (ex.: "Extrair para" num compactado).</summary>
    internal MenuItem? FocusOn
    {
        init
        {
            if (value is not null && IndexOf(value) is var index and >= 0) FocusIndex = index;
        }
    }

    internal void Refresh()
    {
        if (Reload is null) return;
        var label = FocusIndex >= 0 && FocusIndex < Items.Count ? Items[FocusIndex].Label : null;
        var previous = FocusIndex;
        Arrange(Reload());
        // O mesmo ajuste continua em foco mesmo com o texto novo (ex.: "Ordem: crescente" → "decrescente").
        var same = Items.ToList().FindIndex(i => i.Label == label);
        FocusIndex = same >= 0 ? same : Math.Clamp(previous, 0, Math.Max(0, Items.Count - 1));
    }

    private void Arrange(IReadOnlyList<MenuItem> items)
    {
        // Blocos perigosos (Excluir) sempre no fim da grade, longe do ponto de entrada.
        Items = [.. items.Where(i => i.IsQuick && !i.IsDestructive), .. items.Where(i => i.IsQuick && i.IsDestructive), .. items.Where(i => !i.IsQuick)];
        QuickCount = items.Count(i => i.IsQuick);
    }

    private int IndexOf(MenuItem item)
    {
        for (var i = 0; i < Items.Count; i++)
            if (ReferenceEquals(Items[i], item)) return i;
        return -1;
    }
}

public sealed class KeyboardModal(VirtualKeyboard keyboard, Func<VirtualKeyboard, Task> onSubmit, Action? onCancel = null) : Modal(keyboard.Title)
{
    public VirtualKeyboard Keyboard { get; } = keyboard;
    public override ActionIcon Icon { get; internal set; } = keyboard.Kind == TextFieldKind.Password ? ActionIcon.Password : ActionIcon.Keyboard;
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

public sealed class DialogOption(string label, DialogOptionKind kind, Action execute, ActionIcon icon = ActionIcon.None)
{
    public string Label { get; internal set; } = label;
    public DialogOptionKind Kind { get; } = kind;
    internal Action Execute { get; } = execute;
    public bool IsChecked { get; internal set; }

    /// <summary>Opção perigosa (confirma algo que apaga, substitui ou executa): nunca é o foco inicial.</summary>
    public bool IsDestructive => Kind == DialogOptionKind.Danger || ActionIcons.IsDestructive(Icon);

    /// <summary>
    /// Símbolo da opção: o escolhido por quem montou o diálogo ou, sem escolha, o do tipo (segura = fechar, principal =
    /// aceitar, perigosa = alerta). Opções de alternar mostram a caixa de marcação na tela.
    /// </summary>
    public ActionIcon Icon { get; } = icon != ActionIcon.None ? icon : kind switch
    {
        DialogOptionKind.Safe => ActionIcon.Close,
        DialogOptionKind.Danger => ActionIcon.Warning,
        DialogOptionKind.Toggle => ActionIcon.Settings,
        _ => ActionIcon.Accept,
    };
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

    /// <summary>Andamento (0–1) de uma operação mostrada no diálogo; null: sem barra.</summary>
    public double? Progress { get; internal set; }
    internal DialogOption? BackOption { get; set; }
    public override bool IsSensitive => sensitive;

    private ActionIcon _icon;

    /// <summary>Tom do diálogo: o escolhido por quem o montou ou, com uma opção perigosa, o alerta.</summary>
    public override ActionIcon Icon
    {
        get => _icon != ActionIcon.None ? _icon : Options.Any(o => o.Kind == DialogOptionKind.Danger) ? ActionIcon.Warning : ActionIcon.None;
        internal set => _icon = value;
    }
}

/// <summary>Tela "Sobre": logo, versão, licença e origem do código. Fecha com Confirmar ou Voltar.</summary>
public sealed class AboutModal(string version, IReadOnlyList<(string Label, string Value)> lines) : Modal("Sobre o ControlFS")
{
    public override ActionIcon Icon { get; internal set; } = ActionIcon.About;

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
    public override ActionIcon Icon { get; internal set; } = ActionIcon.ControllerSetup;
    public Core.Input.Mapping.ControllerMappingWizard Wizard { get; } = wizard;
    public int ReviewFocus { get; internal set; }

    /// <summary>Outro controle não assume no meio do mapeamento.</summary>
    public override bool IsSensitive => true;
}

/// <summary>Um dispositivo na tela de teste: número estável na sessão e se ainda está conectado.</summary>
public sealed record ControllerTestDevice(int Number, Core.Contracts.InputDeviceInfo Info, bool IsConnected, bool IsActive, string? Profile,
    Core.Input.VirtualSource Source = Core.Input.VirtualSource.None);

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

    public override ActionIcon Icon { get; internal set; } = ActionIcon.ControllerTest;

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

/// <summary>
/// Visualização de imagens da pasta (#57). A decodificação acontece fora da thread de UI e só depois de conferir tamanho e
/// resolução; nada é executado. Zoom e posição são lógicos: a tela só desenha.
/// </summary>
public sealed class ImagePreviewModal : Modal
{
    /// <summary>Níveis de zoom sobre a imagem ajustada à tela (1 = inteira na tela).</summary>
    public static IReadOnlyList<double> ZoomLevels { get; } = [1, 1.5, 2, 3, 4, 6, 8];

    internal ImagePreviewModal(PaneState pane, IReadOnlyList<Core.Models.FileEntry> images, int index) : base("Visualizar imagem")
    {
        Icon = ActionIcon.Image;
        Pane = pane;
        Images = images;
        Index = index;
    }

    internal PaneState Pane { get; }

    /// <summary>Imagens da pasta, na ordem da lista.</summary>
    public IReadOnlyList<Core.Models.FileEntry> Images { get; }
    public int Index { get; internal set; }
    public Core.Models.FileEntry Current => Images[Index];

    public bool IsLoading { get; internal set; }
    public Core.Contracts.PreviewImage? Image { get; internal set; }
    public Core.Preview.ImageHeaderInfo? Info { get; internal set; }
    public string? Error { get; internal set; }

    public int ZoomIndex { get; private set; }
    public double Zoom => ZoomLevels[ZoomIndex];

    /// <summary>Centro da área visível, em frações da imagem (0–1). Sempre dentro da imagem para o zoom atual.</summary>
    public double CenterX { get; private set; } = 0.5;
    public double CenterY { get; private set; } = 0.5;

    internal int Generation { get; set; }
    internal CancellationTokenSource? Loading { get; set; }

    /// <summary>
    /// Legendas recolhidas depois de um tempo sem entrada (#171): a tela as esmaece e deixa só Fechar em destaque; qualquer
    /// entrada as mostra de novo (e ainda faz o que o botão faz).
    /// </summary>
    public bool HintsFaded { get; internal set; }

    internal TimeSpan LastInput { get; set; }

    internal void ChangeZoom(int delta)
    {
        ZoomIndex = Math.Clamp(ZoomIndex + delta, 0, ZoomLevels.Count - 1);
        Pan(0, 0);
    }

    /// <summary>Move a área visível em passos de 1/4 da tela; nunca sai da imagem.</summary>
    internal void Pan(int dx, int dy)
    {
        var half = 0.5 / Zoom;
        CenterX = Math.Clamp(CenterX + dx * 0.25 / Zoom, half, 1 - half);
        CenterY = Math.Clamp(CenterY + dy * 0.25 / Zoom, half, 1 - half);
    }

    internal void ResetView()
    {
        ZoomIndex = 0;
        CenterX = CenterY = 0.5;
    }
}

/// <summary>
/// Visualização de texto (#58), somente leitura. O documento já vem limitado em bytes e linhas; a tela desenha só as linhas
/// visíveis a partir de <see cref="Top"/> e <see cref="Column"/>.
/// </summary>
public sealed class TextPreviewModal : Modal
{
    /// <summary>Colunas deslocadas por Esquerda/Direita (linhas longas não quebram).</summary>
    public const int ColumnStep = 16;

    internal TextPreviewModal(PaneState pane, Core.Models.FileEntry entry) : base("Visualizar texto")
    {
        Icon = ActionIcon.Text;
        Pane = pane;
        Entry = entry;
    }

    internal PaneState Pane { get; }
    public Core.Models.FileEntry Entry { get; }
    public bool IsLoading { get; internal set; } = true;
    public Core.Preview.TextDocument? Document { get; internal set; }
    public string? Error { get; internal set; }

    /// <summary>Primeira linha visível (0-based).</summary>
    public int Top { get; private set; }

    /// <summary>Primeira coluna visível.</summary>
    public int Column { get; private set; }

    /// <summary>Fonte de largura fixa (padrão) ou proporcional.</summary>
    public bool Monospace { get; internal set; } = true;

    /// <summary>Linhas que cabem na tela; informado pela tela (usado para paginar e limitar a rolagem).</summary>
    public int PageLines { get; internal set; } = 20;

    private int LineCount => Document?.Lines.Count ?? 0;

    internal void ScrollTo(int top) => Top = Math.Clamp(top, 0, Math.Max(0, LineCount - PageLines));

    internal void ScrollBy(int lines) => ScrollTo(Top + lines);

    internal void ShiftColumns(int delta)
    {
        var longest = 0;
        if (Document is { } document)
            for (var i = Top; i < Math.Min(LineCount, Top + PageLines); i++) longest = Math.Max(longest, document.Lines[i].Length);
        Column = Math.Clamp(Column + delta, 0, Math.Max(0, longest - ColumnStep));
    }
}
