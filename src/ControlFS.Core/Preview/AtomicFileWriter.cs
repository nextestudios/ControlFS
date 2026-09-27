namespace ControlFS.Core.Preview;

/// <summary>
/// Regrava um arquivo sem nunca deixá-lo pela metade (#62): o conteúdo novo vai para um temporário na mesma pasta
/// (gravado até o disco), que então substitui o original numa troca só (<see cref="File.Replace(string, string, string?, bool)"/>,
/// que preserva atributos e permissões do original). O original vira a cópia de segurança indicada.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>Nome do temporário ao lado do arquivo (oculto pelo ponto; registrado para limpeza após queda).</summary>
    public static string TempPathFor(string path) =>
        Path.Join(Path.GetDirectoryName(path), $".controlfs-edit-{Guid.NewGuid():N}.part");

    /// <summary>Cópia de segurança do original ao lado dele (sobrescrita a cada salvamento).</summary>
    public static string BackupPathFor(string path) => path + ".controlfs.bak";

    /// <exception cref="IOException">Disco cheio, arquivo em uso, etc. O original fica intacto.</exception>
    /// <exception cref="UnauthorizedAccessException">Arquivo somente leitura ou sem permissão. O original fica intacto.</exception>
    public static void Replace(string path, string tempPath, ReadOnlySpan<byte> content, string backupPath)
    {
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fica para a limpeza de sobras da próxima inicialização (o temporário foi registrado).
            }
        }
    }
}
