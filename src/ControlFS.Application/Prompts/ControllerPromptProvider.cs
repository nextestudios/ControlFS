using ControlFS.Core.Actions;
using ControlFS.Core.Input;

namespace ControlFS.Application.Prompts;

/// <summary>
/// Legenda pronta para desenhar: o glifo do controle (<see cref="Button"/> + <see cref="Family"/>) ou, sem controle
/// ativo, a tecla do teclado (<see cref="Key"/>). <see cref="Key"/> também serve de texto alternativo do glifo.
/// </summary>
public sealed record ControllerPrompt(InputAction Action, string Label, ControllerButton? Button, ControllerFamily? Family, string Key, string AccessibilityText)
{
    public bool IsKeyboard => Button is null;
}

/// <summary>Transforma uma ação semântica na legenda do dispositivo em uso. Só o visual muda: o comportamento segue a posição física.</summary>
public interface IControllerPromptProvider
{
    ControllerPrompt For(InputAction action, string label);
}

/// <summary>
/// Legendas conforme a família ativa (<paramref name="family"/>: null = sem controle ativo, legendas de teclado), a
/// convenção confirmar/voltar e o contexto de digitação (no teclado virtual, Enter confirma o texto).
/// Nunca olha o nome do dispositivo.
/// </summary>
public sealed class ControllerPromptProvider(Func<ControllerFamily?> family, Func<ConfirmBackConvention> convention, Func<bool> typing) : IControllerPromptProvider
{
    private ActionMap? _map;

    public ControllerPrompt For(InputAction action, string label)
    {
        if (family() is { } active && Map().ControlFor(action) is { } control)
        {
            var button = ControllerButtons.From(control);
            return new(action, label, button, active, ButtonGlyphs.For(control, active), $"{ControllerButtons.SpokenName(button, active)}: {label}");
        }
        var key = ButtonGlyphs.KeyboardFor(action, typing());
        return new(action, label, null, null, key, $"{key}: {label}");
    }

    private ActionMap Map()
    {
        var current = convention();
        if (_map is null || _map.Convention != current) _map = new ActionMap(current);
        return _map;
    }
}
