using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ControlFS.Application.Operations;

/// <summary>
/// Impressão de um item copiado (#22): tamanho e data de modificação de cada arquivo e a lista de itens de uma pasta.
/// Desfazer uma cópia só remove o que ainda tiver a mesma impressão. Null: não dá para garantir (links dentro da pasta,
/// itens demais, ou acesso negado), e então a cópia não é desfeita.
/// </summary>
public static class UndoFingerprint
{
    public const int MaxEntries = 10_000;

    public static string? Compute(string path)
    {
        try
        {
            if (IsLink(path)) return null;
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return string.Create(CultureInfo.InvariantCulture, $"F|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
            }
            if (!Directory.Exists(path)) return null;
            var lines = new List<string>();
            var stack = new Stack<string>([path]);
            while (stack.Count > 0)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(stack.Pop()))
                {
                    if (lines.Count >= MaxEntries || IsLink(child)) return null;
                    var relative = Path.GetRelativePath(path, child);
                    if (Directory.Exists(child))
                    {
                        lines.Add(relative + "|D");
                        stack.Push(child);
                    }
                    else
                    {
                        var file = new FileInfo(child);
                        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{relative}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"));
                    }
                }
            }
            lines.Sort(StringComparer.Ordinal);
            return "D|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsLink(string path) =>
        new FileInfo(path).LinkTarget is not null || (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
