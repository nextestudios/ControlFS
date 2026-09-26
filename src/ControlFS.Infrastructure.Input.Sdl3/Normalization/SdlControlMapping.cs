using ControlFS.Core.Actions;
using SDL;

namespace ControlFS.Infrastructure.Input.Sdl3.Normalization;

/// <summary>
/// Botões SDL3 (já por posição física) para controles do produto. Guide/Home não é mapeado:
/// pode pertencer ao sistema. Paddles, touchpad e MISC são ignorados nesta etapa.
/// </summary>
internal static class SdlControlMapping
{
    public static PhysicalControl? FromButton(SDL_GamepadButton button) => button switch
    {
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => PhysicalControl.South,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => PhysicalControl.East,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => PhysicalControl.West,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => PhysicalControl.North,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => PhysicalControl.DPadUp,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => PhysicalControl.DPadDown,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => PhysicalControl.DPadLeft,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => PhysicalControl.DPadRight,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => PhysicalControl.LeftShoulder,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => PhysicalControl.RightShoulder,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => PhysicalControl.Start,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => PhysicalControl.Select,
        _ => null,
    };
}
