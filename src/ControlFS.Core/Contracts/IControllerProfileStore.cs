using ControlFS.Core.Input.Mapping;

namespace ControlFS.Core.Contracts;

/// <summary>Perfis salvos, carregados ao abrir (arquivos inválidos são ignorados e informados).</summary>
public sealed record ControllerProfilesLoadResult(IReadOnlyList<ControllerProfile> Profiles, IReadOnlyList<string> Problems);

/// <summary>
/// Perfis de joysticks sem mapeamento de gamepad, em JSON versionado (<see cref="ControllerProfileSerializer"/>).
/// Gravação atômica; substituir um perfil é decisão da aplicação, que confirma com o usuário antes.
/// </summary>
public interface IControllerProfileStore
{
    ControllerProfilesLoadResult Load();

    void Save(ControllerProfile profile);

    /// <summary>Lê um perfil de outro lugar (validação completa; nunca executa nada).</summary>
    ControllerProfile Import(string path);

    /// <summary>Grava uma cópia do perfil em <paramref name="directory"/> e devolve o caminho do arquivo criado.</summary>
    string Export(ControllerProfile profile, string directory);

    /// <summary>Perfis importáveis numa pasta (nomes de arquivo, sem abrir cada um).</summary>
    IReadOnlyList<string> ListImportable(string directory);
}

/// <summary>Joysticks crus conectados e seu estado instantâneo (publicado pela camada de entrada).</summary>
public interface IRawControllerSource
{
    IReadOnlyList<InputDeviceInfo> RawDevices { get; }

    RawJoystickState? GetState(string deviceKey);
}
