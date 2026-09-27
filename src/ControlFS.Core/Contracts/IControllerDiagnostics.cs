namespace ControlFS.Core.Contracts;

/// <summary>
/// O que a camada de entrada sabe dos controles conectados, para a tela "Teste de controles": todos os dispositivos
/// (gamepads e joysticks crus), o ativo e a versão do backend. Também troca o controle ativo (Menu → Controle ativo).
/// </summary>
public interface IControllerDiagnostics
{
    IReadOnlyCollection<InputDeviceInfo> Devices { get; }

    /// <summary>Dispositivo que comanda a UI agora (null: nenhum).</summary>
    string? ActiveDeviceKey { get; }

    /// <summary>Backend e versão (ex.: "SDL 3.2.10") ou o motivo de estar indisponível.</summary>
    string BackendDescription { get; }

    /// <summary>O ativo foi escolhido no menu e nenhum outro controle assume (false: automático).</summary>
    bool IsActiveDeviceLocked { get; }

    /// <summary>Fixa o controle ativo (só ele comanda a UI); null volta ao automático.</summary>
    void SelectActiveDevice(string? deviceKey);
}
