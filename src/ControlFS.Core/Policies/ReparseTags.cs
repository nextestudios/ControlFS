namespace ControlFS.Core.Policies;

/// <summary>
/// Classificação das marcas de ponto de nova análise (reparse tags) de pastas. Só as pastas de arquivos na nuvem
/// (OneDrive e outros provedores do Cloud Files, "arquivos sob demanda") são pastas comuns para percorrer; junções,
/// links simbólicos, pontos de montagem e qualquer outra marca continuam recusados.
/// </summary>
public static class ReparseTags
{
    public const uint MountPoint = 0xA0000003; // junções e pontos de montagem
    public const uint SymbolicLink = 0xA000000C;
    public const uint Cloud = 0x9000001A;

    /// <summary>IO_REPARSE_TAG_CLOUD e CLOUD_1…CLOUD_F (0x9000_101A…0x9000_F01A): variam só nos bits 12–15.</summary>
    private const uint CloudMask = 0xFFFF0FFF;

    public static bool IsCloudFiles(uint tag) => (tag & CloudMask) == Cloud;

    /// <summary>Uma pasta com esta marca pode ser percorrida como pasta comum (só enumerar nomes, nunca hidratar).</summary>
    public static bool IsTraversableFolder(uint tag) => IsCloudFiles(tag);
}
