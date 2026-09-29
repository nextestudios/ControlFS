using ControlFS.Core.Actions;

namespace ControlFS.Application.State;

/// <summary>Passos das boas-vindas (#231), nesta ordem.</summary>
public enum OnboardingStep
{
    Welcome,
    Controls,
    Basics,
    Privacy,
    Tutorial,
}

/// <summary>
/// Uma opção de um passo das boas-vindas: avançar, um ajuste com o valor atual (<see cref="Value"/>; Confirmar troca na
/// hora) ou a escolha final (tutorial ou não).
/// </summary>
public sealed record OnboardingOption(string Label, Action Execute, ActionIcon Icon = ActionIcon.None, string? Value = null, string? Detail = null)
{
    /// <summary>Um ajuste (mostra o valor e muda a cada Confirmar), não um botão de avançar.</summary>
    public bool IsSetting => Value is not null;
}

/// <summary>
/// Boas-vindas em tela cheia (#231), guiadas pelo controle: direcional move o foco entre as opções do passo, Confirmar
/// escolhe, Voltar (ou L1) volta um passo, R1 avança e Menu (Start) pula tudo. Os ajustes valem na hora, pelas mesmas
/// preferências de Configurações. Nunca bloqueia: pular e concluir sempre fecham, e a tela pode desistir dela (erro ao
/// desenhar) chamando <see cref="AppController.SkipOnboarding"/>.
/// </summary>
public sealed class OnboardingModal : Modal
{
    internal OnboardingModal(bool replay) : base("Boas-vindas ao ControlFS")
    {
        Icon = ActionIcon.Help;
        IsReplay = replay;
    }

    public override ModalSize Size => ModalSize.Fill;

    public static IReadOnlyList<OnboardingStep> Steps { get; } = Enum.GetValues<OnboardingStep>();

    /// <summary>Aberto de novo pelo Menu ou por Configurações (não é a primeira execução).</summary>
    public bool IsReplay { get; }

    public int StepIndex { get; internal set; }
    public OnboardingStep Step => Steps[StepIndex];
    public int StepCount => Steps.Count;
    public bool IsLastStep => StepIndex == StepCount - 1;

    public string StepTitle { get; internal set; } = string.Empty;
    public string StepBody { get; internal set; } = string.Empty;

    public IReadOnlyList<OnboardingOption> Options { get; internal set; } = [];
    public int FocusIndex { get; internal set; }
    public OnboardingOption? FocusedOption => Options.Count == 0 ? null : Options[Math.Clamp(FocusIndex, 0, Options.Count - 1)];

    /// <summary>Passo "Como o controle funciona": ações e o que fazem (a tela desenha o botão do controle em uso).</summary>
    public IReadOnlyList<Hint> ControlLegend { get; internal set; } = [];
}
