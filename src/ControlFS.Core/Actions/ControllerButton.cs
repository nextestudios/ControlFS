using ControlFS.Core.Input;

namespace ControlFS.Core.Actions;

/// <summary>
/// Controles que têm um desenho próprio nas legendas. Faces e direções seguem a POSIÇÃO física (como
/// <see cref="PhysicalControl"/>); o desenho de cada família é decidido na camada de apresentação.
/// </summary>
public enum ControllerButton
{
    FaceSouth,
    FaceEast,
    FaceWest,
    FaceNorth,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    Start,
    Select,
    DPad,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    DPadVertical,
    DPadHorizontal,
    LeftStick,
    RightStick,
    LeftStickClick,
    RightStickClick,
}

public static class ControllerButtons
{
    public static ControllerButton From(PhysicalControl control) => control switch
    {
        PhysicalControl.South => ControllerButton.FaceSouth,
        PhysicalControl.East => ControllerButton.FaceEast,
        PhysicalControl.West => ControllerButton.FaceWest,
        PhysicalControl.North => ControllerButton.FaceNorth,
        PhysicalControl.LeftShoulder => ControllerButton.LeftShoulder,
        PhysicalControl.RightShoulder => ControllerButton.RightShoulder,
        PhysicalControl.LeftTrigger => ControllerButton.LeftTrigger,
        PhysicalControl.RightTrigger => ControllerButton.RightTrigger,
        PhysicalControl.Start => ControllerButton.Start,
        PhysicalControl.Select => ControllerButton.Select,
        PhysicalControl.DPadUp => ControllerButton.DPadUp,
        PhysicalControl.DPadDown => ControllerButton.DPadDown,
        PhysicalControl.DPadLeft => ControllerButton.DPadLeft,
        PhysicalControl.DPadRight => ControllerButton.DPadRight,
        _ => ControllerButton.LeftStick, // StickUp/Down/Left/Right: o analógico esquerdo
    };

    /// <summary>Nome falado (leitor de tela) do botão na família, em pt-BR. Nunca depende do nome do dispositivo.</summary>
    public static string SpokenName(ControllerButton button, ControllerFamily family) => button switch
    {
        ControllerButton.FaceSouth or ControllerButton.FaceEast or ControllerButton.FaceWest or ControllerButton.FaceNorth => Face(button, family),
        ControllerButton.LeftShoulder => family switch { ControllerFamily.Xbox => "LB", ControllerFamily.Nintendo => "L", _ => "L1" },
        ControllerButton.RightShoulder => family switch { ControllerFamily.Xbox => "RB", ControllerFamily.Nintendo => "R", _ => "R1" },
        ControllerButton.LeftTrigger => family switch { ControllerFamily.Xbox => "LT", ControllerFamily.Nintendo => "ZL", _ => "L2" },
        ControllerButton.RightTrigger => family switch { ControllerFamily.Xbox => "RT", ControllerFamily.Nintendo => "ZR", _ => "R2" },
        ControllerButton.Start => family switch
        {
            ControllerFamily.Xbox => "Botão Menu",
            ControllerFamily.PlayStation => "Botão Options",
            ControllerFamily.Nintendo => "Botão mais",
            _ => "Botão Start",
        },
        ControllerButton.Select => family switch
        {
            ControllerFamily.Xbox => "Botão Exibir",
            ControllerFamily.PlayStation => "Botão Create",
            ControllerFamily.Nintendo => "Botão menos",
            _ => "Botão Select",
        },
        ControllerButton.DPad => "Direcional",
        ControllerButton.DPadUp => "Direcional para cima",
        ControllerButton.DPadDown => "Direcional para baixo",
        ControllerButton.DPadLeft => "Direcional para a esquerda",
        ControllerButton.DPadRight => "Direcional para a direita",
        ControllerButton.DPadVertical => "Direcional para cima ou para baixo",
        ControllerButton.DPadHorizontal => "Direcional para a esquerda ou para a direita",
        ControllerButton.LeftStick => "Analógico esquerdo",
        ControllerButton.RightStick => "Analógico direito",
        ControllerButton.LeftStickClick => family == ControllerFamily.Xbox ? "Pressionar analógico esquerdo" : "L3",
        ControllerButton.RightStickClick => family == ControllerFamily.Xbox ? "Pressionar analógico direito" : "R3",
        _ => button.ToString(),
    };

    private static string Face(ControllerButton button, ControllerFamily family) => (family, button) switch
    {
        (ControllerFamily.PlayStation, ControllerButton.FaceSouth) => "Botão cruz",
        (ControllerFamily.PlayStation, ControllerButton.FaceEast) => "Botão círculo",
        (ControllerFamily.PlayStation, ControllerButton.FaceWest) => "Botão quadrado",
        (ControllerFamily.PlayStation, ControllerButton.FaceNorth) => "Botão triângulo",
        (ControllerFamily.Xbox or ControllerFamily.Nintendo, _) => "Botão " + FaceLetter(button, family),
        (_, ControllerButton.FaceSouth) => "Botão inferior",
        (_, ControllerButton.FaceEast) => "Botão direito",
        (_, ControllerButton.FaceWest) => "Botão esquerdo",
        _ => "Botão superior",
    };

    /// <summary>Letra impressa por posição (Xbox e Nintendo). Nintendo tem A à direita e B embaixo.</summary>
    public static string FaceLetter(ControllerButton button, ControllerFamily family) => (family, button) switch
    {
        (ControllerFamily.Nintendo, ControllerButton.FaceSouth) => "B",
        (ControllerFamily.Nintendo, ControllerButton.FaceEast) => "A",
        (ControllerFamily.Nintendo, ControllerButton.FaceWest) => "Y",
        (ControllerFamily.Nintendo, ControllerButton.FaceNorth) => "X",
        (_, ControllerButton.FaceSouth) => "A",
        (_, ControllerButton.FaceEast) => "B",
        (_, ControllerButton.FaceWest) => "X",
        _ => "Y",
    };
}
