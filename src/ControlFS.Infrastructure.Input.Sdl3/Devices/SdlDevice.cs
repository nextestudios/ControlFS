using ControlFS.Core.Contracts;
using ControlFS.Core.Input;

namespace ControlFS.Infrastructure.Input.Sdl3.Devices;

internal sealed unsafe class SdlDevice(InputDeviceInfo info, SDL.SDL_Gamepad* gamepad, SDL.SDL_Joystick* joystick, InputSettings settings)
{
    public InputDeviceInfo Info { get; } = info;
    public SDL.SDL_Gamepad* Gamepad { get; } = gamepad;
    public SDL.SDL_Joystick* Joystick { get; } = joystick;
    public StickNormalizer Stick { get; } = new(settings);
    public double StickX { get; set; }
    public double StickY { get; set; }
    public double RightX { get; set; }
    public double RightY { get; set; }
    public bool LeftTriggerDown { get; set; }
    /// <summary>Giroscópio ligado neste gamepad (mira experimental, #77).</summary>
    public bool GyroOn { get; set; }
    public bool RightTriggerDown { get; set; }
}
