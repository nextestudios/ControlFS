namespace ControlFS.Core.Contracts;

/// <summary>
/// Montar e desmontar imagens de disco (ISO, IMG, VHD, VHDX) com o suporte nativo do Windows (#73, #74): nada de drivers de
/// terceiros nem linha de comando. Toda chamada vem de uma ação explícita do usuário; métodos síncronos rodam fora da
/// thread de UI.
/// </summary>
public interface IDiskImageService
{
    /// <summary>Monta a imagem (ou encontra a montagem existente) e devolve a raiz da unidade nova (ex.: "E:\").</summary>
    /// <exception cref="FileOperationException">Com o motivo legível (formato, permissão, arquivo em uso…).</exception>
    string Mount(string imagePath, CancellationToken cancellationToken);

    /// <summary>Arquivo de imagem por trás da unidade, se ela for uma imagem montada (por qualquer programa); null se não for.</summary>
    string? ImageBehind(string driveRoot);

    /// <summary>Desmonta a imagem montada na unidade. A unidade some da lista.</summary>
    /// <exception cref="FileOperationException"/>
    void Unmount(string driveRoot);
}

/// <summary>Extensões que o Windows monta nativamente.</summary>
public static class DiskImageFormats
{
    public static bool IsMountable(string path) => Path.GetExtension(path).ToLowerInvariant() is ".iso" or ".img" or ".vhd" or ".vhdx";

    /// <summary>Imagens de disco óptico (somente leitura); VHD/VHDX são discos rígidos virtuais.</summary>
    public static bool IsOptical(string path) => Path.GetExtension(path).ToLowerInvariant() is ".iso" or ".img";
}
