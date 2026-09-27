namespace ControlFS.Core.Contracts;

/// <summary>
/// O que a camada de entrada sabe dos controles conectados, para a tela "Teste de controles": todos os dispositivos
/// (gamepads e joysticks crus), o ativo e a versão do backend.
/// </summary>
public interface IControllerDiagnostics
{
    IReadOnlyCollection<InputDeviceInfo> Devices { get; }

    /// <summary>Dispositivo que comanda a UI agora (null: nenhum).</summary>
    string? ActiveDeviceKey { get; }

    /// <summary>Backend e versão (ex.: "SDL 3.2.10") ou o motivo de estar indisponível.</summary>
    string BackendDescription { get; }
}
