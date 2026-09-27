using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Ícones do Shell do Windows (SHGetFileInfo + listas de imagens do sistema). Tudo roda numa thread STA dedicada: a thread
/// de UI nunca espera. Tipos comuns são pedidos por extensão com SHGFI_USEFILEATTRIBUTES (sem tocar no disco); só
/// unidades, pastas especiais e .exe/.ico consultam o item real. Atalhos (.lnk, .url de jogos da Steam) nunca passam
/// pelo Shell: o ícone que declaram é lido por <see cref="ShortcutFiles"/>, validado (só caminho local em unidade fixa) e
/// extraído do arquivo local. Fora do Windows devolve sempre <c>null</c>.
/// </summary>
public sealed partial class ShellIconProvider : IIconProvider, IDisposable
{
    private readonly BlockingCollection<Work> _queue = [];
    private readonly Thread? _worker;

    private sealed record Work(IconRequest Request, int SizePx, TaskCompletionSource<IconImage?> Result, CancellationToken Cancellation);

    public ShellIconProvider()
    {
        if (!OperatingSystem.IsWindows()) return;
        _worker = new Thread(Run) { IsBackground = true, Name = "ControlFS shell icons" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public Task<IconImage?> GetIconAsync(IconRequest request, int sizePx, CancellationToken cancellationToken)
    {
        if (_worker is null || sizePx <= 0) return Task.FromResult<IconImage?>(null);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<IconImage?>(cancellationToken);
        var result = new TaskCompletionSource<IconImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(new Work(request, sizePx, result, cancellationToken), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            result.TrySetResult(null); // já descartado
        }
        return result.Task;
    }

    public void Dispose() => _queue.CompleteAdding();

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            // Linha que saiu da tela antes da vez dela: não gasta tempo com o Shell.
            if (work.Cancellation.IsCancellationRequested)
            {
                work.Result.TrySetCanceled(work.Cancellation);
                continue;
            }
            IconImage? image = null;
            try
            {
                if (OperatingSystem.IsWindows()) image = Extract(work.Request, work.SizePx);
            }
            catch (Exception ex) when (ex is COMException or ExternalException or ArgumentException or OutOfMemoryException or IOException or UnauthorizedAccessException)
            {
                image = null;
            }
            work.Result.TrySetResult(image);
        }
    }

    // ---------- Win32 ----------

    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiSysIconIndex = 0x4000;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint ShgfiPidl = 0x8;
    private const int ShilLarge = 0;      // 32 px
    private const int ShilSmall = 1;      // 16 px
    private const int ShilExtraLarge = 2; // 48 px
    private const int ShilJumbo = 4;      // 256 px
    private const int IldTransparent = 0x1;
    private static readonly Guid IidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ShFileInfo
    {
        public nint HIcon;
        public int IIcon;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int FIcon;
        public int XHotspot;
        public int YHotspot;
        public nint HbmMask;
        public nint HbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe IconImage? Extract(IconRequest request, int sizePx)
    {
        if (request.Kind == IconSourceKind.Shortcut) return ExtractShortcut(request.Value, sizePx);
        var (name, attributes, flags) = request.Kind switch
        {
            IconSourceKind.Folder => ("pasta", FileAttributeDirectory, ShgfiSysIconIndex | ShgfiUseFileAttributes),
            IconSourceKind.Extension => ("arquivo" + request.Value, FileAttributeNormal, ShgfiSysIconIndex | ShgfiUseFileAttributes),
            _ => (request.Value, 0u, ShgfiSysIconIndex),
        };
        ShFileInfo info = default;
        if (request.Kind == IconSourceKind.Path && name.StartsWith("::", StringComparison.Ordinal))
        {
            // Pasta virtual do Shell (ex.: Lixeira): o ícone vem do PIDL, que reflete o estado atual (vazia/cheia).
            if (SHParseDisplayName(name, 0, out var pidl, 0, out _) != 0 || pidl == 0) return null;
            try
            {
                if (SHGetFileInfoW((char*)pidl, 0, &info, (uint)sizeof(ShFileInfo), ShgfiSysIconIndex | ShgfiPidl) == 0) return null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
        else if (SHGetFileInfoW(name, attributes, &info, (uint)sizeof(ShFileInfo), flags) == 0) return null;

        var list = sizePx <= 16 ? ShilSmall : sizePx <= 32 ? ShilLarge : sizePx <= 48 ? ShilExtraLarge : ShilJumbo;
        var image = FromImageList(list, info.IIcon);
        // Tipos sem arte de 256 px vêm pequenos no canto da imagem "jumbo": nesse caso, usa a de 48 px.
        if (list == ShilJumbo && image is not null && ContentExtent(image) <= 48) image = FromImageList(ShilExtraLarge, info.IIcon) ?? image;
        return image;
    }

    /// <summary>
    /// Ícone de um atalho. .url (jogo da Steam): <c>IconFile</c>/<c>IconIndex</c> do atalho e, se o arquivo não existir,
    /// o mesmo ícone na instalação local da Steam. .lnk: o ícone declarado; sem ele, o do destino (programa .exe/.ico
    /// pelo próprio arquivo, pasta pela pasta genérica, documento só pela extensão — o Shell nunca lê o destino).
    /// Nada resolvido: <c>null</c> (o chamador mostra o símbolo de reserva).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IconImage? ExtractShortcut(string path, int sizePx)
    {
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            if (ShortcutFiles.ReadInternetShortcut(path) is not { IsSteamGame: true } shortcut) return null;
            return (ShortcutFiles.ResolveLocal(shortcut.IconFile, iconFile: true) is { } file ? ExtractFromFile(file, shortcut.IconIndex, sizePx) : null)
                ?? (ShortcutFiles.FindSteamGameIcon(shortcut) is { } steam ? ExtractFromFile(steam, 0, sizePx) : null);
        }
        if (ShortcutFiles.ReadShellLink(path) is not { } link) return null;
        if (link.IconLocation is not null)
            return ShortcutFiles.ResolveLocal(link.IconLocation, iconFile: true) is { } icon ? ExtractFromFile(icon, link.IconIndex, sizePx) : null;
        if (ShortcutFiles.ResolveLocal(link.TargetPath, iconFile: false) is not { } target) return null;
        if (Directory.Exists(target)) return Extract(new IconRequest("folder", IconSourceKind.Folder, string.Empty), sizePx);
        var extension = Path.GetExtension(target).ToLowerInvariant();
        return extension is ".exe" or ".ico"
            ? ExtractFromFile(target, 0, sizePx)
            : Extract(new IconRequest("ext:" + extension, IconSourceKind.Extension, extension), sizePx);
    }

    /// <summary>Ícone número <paramref name="index"/> (negativo: id do recurso) de um .ico/.exe/.dll local, no tamanho pedido.</summary>
    [SupportedOSPlatform("windows")]
    private static unsafe IconImage? ExtractFromFile(string file, int index, int sizePx)
    {
        var size = (uint)Math.Clamp(sizePx, 16, 256);
        nint large = 0, small = 0;
        try
        {
            if (SHDefExtractIconW(file, index, 0, &large, &small, size | (16u << 16)) != 0 || large == 0) return null;
            return FromIcon(large);
        }
        finally
        {
            if (large != 0) _ = DestroyIcon(large);
            if (small != 0) _ = DestroyIcon(small);
        }
    }

    [SupportedOSPlatform("windows")]
    private static unsafe IconImage? FromImageList(int list, int index)
    {
        var iid = IidImageList;
        if (SHGetImageList(list, &iid, out var imageList) != 0 || imageList == 0) return null;
        try
        {
            // IImageList::GetIcon é o 8º método depois de IUnknown (slot 10 da vtable).
            var vtable = *(nint**)imageList;
            var getIcon = (delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)vtable[10];
            nint icon;
            if (getIcon(imageList, index, IldTransparent, &icon) != 0 || icon == 0) return null;
            try
            {
                return FromIcon(icon);
            }
            finally
            {
                _ = DestroyIcon(icon);
            }
        }
        finally
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)imageList)[2];
            _ = release(imageList);
        }
    }

    [SupportedOSPlatform("windows")]
    private static unsafe IconImage? FromIcon(nint icon)
    {
        if (!GetIconInfo(icon, out var info)) return null;
        try
        {
            if (info.HbmColor == 0) return null; // ícone monocromático: fica o símbolo
            Bitmap bitmap;
            if (GetObjectW(info.HbmColor, sizeof(Bitmap), &bitmap) == 0 || bitmap.Width <= 0 || bitmap.Height <= 0) return null;
            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = ReadBits(info.HbmColor, width, height);
            if (pixels is null) return null;

            var hasAlpha = false;
            for (var i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
            if (!hasAlpha)
            {
                // Ícones antigos sem canal alfa: a máscara diz o que é transparente (branco) e o que é opaco (preto).
                var mask = info.HbmMask != 0 ? ReadBits(info.HbmMask, width, height) : null;
                for (var i = 0; i < pixels.Length; i += 4)
                    pixels[i + 3] = mask is not null && mask[i] != 0 ? (byte)0 : (byte)255;
            }
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                if (a == 255) continue;
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }
            return new IconImage(width, height, pixels);
        }
        finally
        {
            if (info.HbmColor != 0) _ = DeleteObject(info.HbmColor);
            if (info.HbmMask != 0) _ = DeleteObject(info.HbmMask);
        }
    }

    [SupportedOSPlatform("windows")]
    private static unsafe byte[]? ReadBits(nint hbitmap, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // de cima para baixo
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var dc = GetDC(0);
        try
        {
            fixed (byte* bits = pixels)
                return GetDIBits(dc, hbitmap, 0, (uint)height, bits, &header, 0) == height ? pixels : null;
        }
        finally
        {
            _ = ReleaseDC(0, dc);
        }
    }

    /// <summary>Maior coordenada (x ou y) com pixel visível: mede o tamanho real do desenho dentro da imagem.</summary>
    private static int ContentExtent(IconImage image)
    {
        var span = image.Pixels.Span;
        var extent = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (span[((y * image.Width) + x) * 4 + 3] != 0) extent = Math.Max(extent, Math.Max(x, y) + 1);
        return extent;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static unsafe partial nint SHGetFileInfoW(string pszPath, uint dwFileAttributes, ShFileInfo* psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial nint SHGetFileInfoW(char* pidl, uint dwFileAttributes, ShFileInfo* psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags, nint* phiconLarge, nint* phiconSmall, uint nIconSize);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial int SHParseDisplayName(string name, nint bindingContext, out nint pidl, uint sfgaoIn, out uint sfgaoOut);

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int SHGetImageList(int iImageList, Guid* riid, out nint ppvObj);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool GetIconInfo(nint hIcon, out IconInfo piconinfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int GetObjectW(nint h, int c, void* pv);

    [LibraryImport("gdi32.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, void* lpvBits, BitmapInfoHeader* lpbmi, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool DeleteObject(nint ho);
}
