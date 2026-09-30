using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>Uma alternativa de um valor com várias opções (#261): o valor, o texto e, opcionalmente, uma linha que explica.</summary>
public sealed record Choice<T>(T Value, string Label, string? Description = null, string? DisabledReason = null);

/// <summary>
/// Seletor de opções (#261): um padrão só para todo valor que tem várias alternativas escondidas (formato e compressão de
/// Compactar, tema, cor, ordenação, legendas… em Configurações). Quem monta um menu ou diálogo declara
/// <c>ChoiceRow(rótulo, atual, alternativas, aplicar)</c> (menu) ou <c>ChoiceOption(…)</c> (diálogo); ativar a linha abre um
/// menu com TODAS as alternativas, a atual marcada (ícone e texto), e escolher aplica e volta ao modal anterior, que fica como
/// estava (estado e foco), enquanto Voltar volta sem mudar nada. Alternar simples (ligado/desligado) e ações diretas continuam
/// de um toque: só vale um seletor quando é preciso ver as alternativas. Uma alternativa nova é uma <see cref="Choice{T}"/> a
/// mais na lista de quem declara a linha.
/// </summary>
public sealed partial class AppController
{
    /// <summary>
    /// Linha de menu que abre o seletor. O rótulo por extenso é "rótulo: atual"; num bloco de Configurações
    /// (<paramref name="placement"/> = Quick) o bloco mostra <paramref name="shortLabel"/> e o valor atual.
    /// <paramref name="keepOpen"/>: o menu continua aberto, com o valor novo, quando o seletor volta (Configurações).
    /// </summary>
    internal MenuItem ChoiceRow<T>(string label, T current, IReadOnlyList<Choice<T>> choices, Action<T> onPick, ActionIcon icon,
        string? section = null, string? detail = null, string? disabledReason = null, MenuPlacement placement = MenuPlacement.List,
        string? shortLabel = null, bool keepOpen = true, string? context = null)
    {
        var shown = CurrentLabel(current, choices);
        return new MenuItem($"{label}: {shown}", () => ShowChoicePicker(label, current, choices, onPick, icon, context), disabledReason, detail, icon, section,
            placement, shortLabel, keepOpen, Value: shown);
    }

    /// <summary>
    /// Opção de diálogo que abre o seletor (Compactar: Formato, Compressão). <paramref name="current"/> é lido quando o seletor abre e
    /// depois de aplicar, então o rótulo da opção acompanha o valor; os outros textos do diálogo se atualizam em <paramref name="onPick"/>.
    /// </summary>
    internal DialogOption ChoiceOption<T>(string label, Func<T> current, IReadOnlyList<Choice<T>> choices, Action<T> onPick, ActionIcon icon, string? context = null)
    {
        DialogOption? option = null;
        option = new DialogOption($"{label}: {CurrentLabel(current(), choices)}", DialogOptionKind.Primary, () => ShowChoicePicker(label, current(), choices, value =>
        {
            onPick(value);
            option!.Label = $"{label}: {CurrentLabel(current(), choices)}";
        }, icon, context), icon);
        return option;
    }

    /// <summary>O texto de <paramref name="current"/> na lista (ou o próprio valor, se a lista não o tiver).</summary>
    private static string CurrentLabel<T>(T current, IReadOnlyList<Choice<T>> choices) =>
        choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, current))?.Label ?? current?.ToString() ?? string.Empty;

    /// <summary>
    /// Abre o seletor sobre o modal atual, com o foco na alternativa atual. Escolher (mesmo a atual) fecha o seletor, aplica e
    /// atualiza o menu de baixo se ele for de ajustes; Voltar fecha sem aplicar. O modal de baixo nunca é refeito: seu foco e
    /// seu estado ficam como estavam.
    /// </summary>
    internal void ShowChoicePicker<T>(string label, T current, IReadOnlyList<Choice<T>> choices, Action<T> onPick, ActionIcon icon, string? context = null)
    {
        var index = Math.Max(0, choices.ToList().FindIndex(c => EqualityComparer<T>.Default.Equals(c.Value, current)));
        var items = choices.Select((choice, i) => new MenuItem(choice.Label, () => ApplyChoice(choice.Value, onPick),
            choice.DisabledReason, choice.Description, i == index ? ActionIcon.RadioOn : ActionIcon.RadioOff, Value: i == index ? "atual" : null)).ToList();
        PushModal(new MenuModal(label, items) { Icon = icon, Subtitle = context, PickerCurrent = index, FocusIndex = index });
    }

    private void ApplyChoice<T>(T value, Action<T> onPick)
    {
        onPick(value); // o seletor já foi fechado (Confirmar em menu fecha antes de executar)
        if (TopModal is MenuModal { Reload: not null } settings) settings.Refresh();
        RaiseChanged();
    }

    // ---------- Alternativas dos ajustes ----------

    private static IReadOnlyList<Choice<Core.Contracts.ViewMode>> ViewChoices { get; } =
    [
        new(Core.Contracts.ViewMode.List, "lista", "Linhas com nome, tipo, tamanho e data; painel de detalhes ao lado."),
        new(Core.Contracts.ViewMode.Grid, "grade", "Cartões com ícone grande, para ver de longe (também R3 ou Ctrl+G)."),
    ];

    private static IReadOnlyList<Choice<Core.Contracts.ListDensity>> DensityChoices { get; } =
    [
        new(Core.Contracts.ListDensity.Comfortable, "confortável", "Duas linhas por item, para TV."),
        new(Core.Contracts.ListDensity.Compact, "compacta", "Uma linha com tipo, tamanho e data; na grade, blocos menores."),
    ];

    private static IReadOnlyList<Choice<Core.Appearance.ThemeMode>> ThemeChoices { get; } =
    [
        new(Core.Appearance.ThemeMode.System, "automático", "Segue o modo de apps do Windows (Configurações → Personalização → Cores)."),
        new(Core.Appearance.ThemeMode.Dark, "escuro"),
        new(Core.Appearance.ThemeMode.Light, "claro"),
    ];

    private static IReadOnlyList<Choice<Core.Appearance.AccentColor>> AccentChoices { get; } =
        [.. Enum.GetValues<Core.Appearance.AccentColor>().Select(a => new Choice<Core.Appearance.AccentColor>(a, AccentName(a)))];

    private static IReadOnlyList<Choice<SortField>> SortChoices { get; } =
    [
        new(SortField.Name, "nome", "Ordem alfabética, com os números em ordem numérica."),
        new(SortField.Type, "tipo", "Agrupa por tipo de arquivo."),
        new(SortField.Size, "tamanho", "Do menor para o maior (ou o contrário, em Ordem)."),
        new(SortField.Modified, "data", "Pela data de modificação."),
    ];

    private static IReadOnlyList<Choice<ConfirmBackConvention>> ConventionChoices { get; } =
    [
        new(ConfirmBackConvention.SouthConfirms, "botão inferior", "Confirmar no botão de baixo (A no Xbox, Cruz no PlayStation) e voltar no da direita."),
        new(ConfirmBackConvention.EastConfirms, "botão direito", "Confirmar no botão da direita (B no Xbox, Círculo no PlayStation) e voltar no de baixo."),
    ];

    private static IReadOnlyList<Choice<ButtonLabelStyle>> LabelStyleChoices { get; } =
    [
        new(ButtonLabelStyle.Automatic, "automáticas", "Os símbolos seguem o controle em uso."),
        new(ButtonLabelStyle.Xbox, "Xbox", "A, B, X e Y coloridos."),
        new(ButtonLabelStyle.PlayStation, "PlayStation", "Cruz, Círculo, Quadrado e Triângulo."),
        new(ButtonLabelStyle.Nintendo, "Nintendo", "A, B, X e Y pela posição do botão."),
        new(ButtonLabelStyle.Generic, "genéricas", "Pontos, sem letras de nenhum fabricante."),
    ];

    private static IReadOnlyList<Choice<bool>> SmoothnessChoices { get; } =
    [
        new(true, "máxima", "Lê o controle ~125 vezes por segundo, o bastante para telas de 120 Hz."),
        new(false, "economia", "Lê o controle menos vezes: gasta menos bateria em portáteis."),
    ];

    private static IReadOnlyList<Choice<int>> SoundChoices { get; } =
    [
        new(0, "desligados", "Sem sons: o controle só se vê, não se ouve."),
        new(25, "baixo", "Toques discretos ao mover, confirmar, voltar e marcar."),
        new(50, "médio", "Toques discretos ao mover, confirmar, voltar e marcar."),
        new(75, "alto", "Toques discretos ao mover, confirmar, voltar e marcar."),
        new(100, "máximo", "O volume mais alto dos toques (ainda curtos e suaves)."),
    ];

    private void SetView(Core.Contracts.ViewMode view)
    {
        if (Settings.View == view) return;
        ToggleView(); // duas exibições: a mesma troca (preferência salva e mensagem)
    }

    private void SetDensity(Core.Contracts.ListDensity density)
    {
        if (Settings.Density == density) return;
        ToggleDensity();
    }

    private void SetTheme(Core.Appearance.ThemeMode mode)
    {
        if (Settings.Theme == mode) return;
        UpdateSettings(s => s with { Theme = mode });
        StatusMessage = $"Tema {ThemeName(mode)}.";
    }

    private void SetAccent(Core.Appearance.AccentColor accent)
    {
        if (Settings.Accent == accent) return;
        UpdateSettings(s => s with { Accent = accent });
        StatusMessage = $"Cor de destaque: {AccentName(accent)}.";
    }
}
