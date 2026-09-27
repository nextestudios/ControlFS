using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace ControlFS.Infrastructure.Windows.DiskImages;

/// <summary>
/// Montagem nativa de imagens pela API de discos virtuais do Windows (virtdisk.dll), a mesma do "Montar" do Explorador e
/// do Mount-DiskImage: sem drivers de terceiros e sem linha de comando (o caminho nunca passa por um shell). ISO/IMG são
/// montadas somente leitura; VHD/VHDX como o Windows faz (o Windows exige administrador para elas). A montagem é
/// permanente até desmontar ou reiniciar, como no Explorador. Desmontar funciona também para imagens montadas por outros
/// programas: o arquivo por trás da unidade vem de GetStorageDependencyInformation.
/// </summary>
public sealed partial class VirtualDiskService : IDiskImageService
{
    /// <summary>Tempo máximo esperando o Windows dar uma letra à unidade nova.</summary>
    private static readonly TimeSpan LetterTimeout = TimeSpan.FromSeconds(15);

    public string Mount(string imagePath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new FileOperationException(OperationErrorKind.UnsupportedFormat, "Montar imagens só funciona no Windows.");
        var path = Path.GetFullPath(imagePath);
        if (!DiskImageFormats.IsMountable(path)) throw new FileOperationException(OperationErrorKind.UnsupportedFormat, "O Windows monta apenas imagens .iso, .img, .vhd e .vhdx.");
        if (!File.Exists(path)) throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "A imagem não existe mais.");
        var optical = DiskImageFormats.IsOptical(path);
        using var disk = Open(path, optical, forDetach: false);
        // Já montada (pelo ControlFS, pelo Explorador…): só encontra a unidade.
        var physical = PhysicalPath(disk);
        if (physical is null)
        {
            var parameters = new AttachParameters { Version = 1 };
            var flags = AttachPermanentLifetime | (optical ? AttachReadOnly : 0u);
            var rc = AttachVirtualDisk(disk, IntPtr.Zero, flags, 0, ref parameters, IntPtr.Zero);
            if (rc != 0) throw Explain(rc, "montar");
            physical = PhysicalPath(disk) ?? throw new FileOperationException(OperationErrorKind.Unknown, "A imagem foi montada, mas o Windows não informou o disco criado.");
        }
        var device = DeviceNumber(physical);
        var deadline = DateTime.UtcNow + LetterTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (device is { } number && DriveOf(number) is { } root) return root;
            if (DateTime.UtcNow > deadline) break;
            Thread.Sleep(200);
            device ??= DeviceNumber(physical);
        }
        // Sem letra (ex.: VHD sem partição reconhecível): não deixa um disco invisível montado.
        _ = DetachVirtualDisk(disk, 0, 0);
        throw new FileOperationException(OperationErrorKind.UnsupportedFormat,
            "O Windows montou a imagem, mas não encontrou um volume para mostrar (disco sem partição ou sistema de arquivos desconhecido). A imagem foi desmontada.");
    }

    public string? ImageBehind(string driveRoot)
    {
        if (!OperatingSystem.IsWindows() || VolumeDevice(driveRoot) is not { } volume) return null;
        using var handle = CreateFileW(volume, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        return BackingFile(handle);
    }

    public void Unmount(string driveRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new FileOperationException(OperationErrorKind.UnsupportedFormat, "Desmontar imagens só funciona no Windows.");
        var image = ImageBehind(driveRoot) ?? throw new FileOperationException(OperationErrorKind.UnsupportedFormat, "Esta unidade não é uma imagem montada.");
        var optical = DiskImageFormats.IsOptical(image) || !Path.GetExtension(image).StartsWith(".vhd", StringComparison.OrdinalIgnoreCase);
        uint rc;
        try
        {
            using var disk = Open(image, optical, forDetach: true);
            rc = DetachVirtualDisk(disk, 0, 0);
        }
        catch (FileOperationException)
        {
            rc = ErrorInvalidParameter;
        }
        if (rc != 0)
        {
            // Algumas versões do Windows só desmontam ISO com o disco aberto no modo "versão 2".
            using var disk = Open(image, optical, forDetach: false);
            rc = DetachVirtualDisk(disk, 0, 0);
        }
        if (rc != 0) throw Explain(rc, "desmontar");
    }

    // ---------------- Abrir e caminhos ----------------

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle Open(string path, bool optical, bool forDetach)
    {
        var type = new VirtualStorageType
        {
            DeviceId = optical ? StorageTypeIso : Path.GetExtension(path).Equals(".vhdx", StringComparison.OrdinalIgnoreCase) ? StorageTypeVhdx : StorageTypeVhd,
            VendorId = VendorMicrosoft,
        };
        uint rc;
        SafeFileHandle handle;
        if (forDetach)
        {
            var v1 = new OpenParameters { Version = 1, First = 0 };
            rc = OpenVirtualDisk(ref type, path, AccessDetach | AccessGetInfo, 0, ref v1, out handle);
        }
        else
        {
            var v2 = new OpenParameters { Version = 2, ReadOnly = optical ? 1 : 0 };
            rc = OpenVirtualDisk(ref type, path, 0, 0, ref v2, out handle);
        }
        if (rc != 0)
        {
            handle.Dispose();
            throw Explain(rc, forDetach ? "desmontar" : "montar");
        }
        return handle;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe string? PhysicalPath(SafeFileHandle disk)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024 * sizeof(char);
        return GetVirtualDiskPhysicalPath(disk, ref size, buffer) == 0 ? new string(buffer) : null;
    }

    private static string? VolumeDevice(string driveRoot)
    {
        var root = Path.GetPathRoot(driveRoot);
        if (root is not { Length: >= 2 } || root[1] != ':' || !char.IsAsciiLetter(root[0])) return null;
        return $@"\\.\{char.ToUpperInvariant(root[0])}:";
    }

    [SupportedOSPlatform("windows")]
    private static (uint Type, uint Number)? DeviceNumber(string device)
    {
        using var handle = CreateFileW(device, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        if (!DeviceIoControl(handle, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0, out var number, (uint)Marshal.SizeOf<StorageDeviceNumber>(), out _, IntPtr.Zero)) return null;
        return (number.DeviceType, number.DeviceNumber);
    }

    /// <summary>A unidade (letra) cujo volume está no dispositivo <paramref name="device"/>.</summary>
    [SupportedOSPlatform("windows")]
    private static string? DriveOf((uint Type, uint Number) device)
    {
        var mask = GetLogicalDrives();
        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            var letter = (char)('A' + i);
            if (DeviceNumber($@"\\.\{letter}:") == device) return $@"{letter}:\";
        }
        return null;
    }

    /// <summary>
    /// Arquivo de imagem por trás do volume: a entrada direta (nível 1) de GetStorageDependencyInformation, com o volume
    /// hospedeiro convertido para a letra quando houver. Qualquer erro (disco comum, sem acesso): null.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static unsafe string? BackingFile(SafeFileHandle volume)
    {
        var size = 4096u;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[size];
            fixed (byte* data = buffer)
            {
                *(int*)data = 2; // STORAGE_DEPENDENCY_INFO_VERSION_2
                var rc = GetStorageDependencyInformation(volume, DependencyHostVolumes | DependencyDiskHandle, size, data, out var used);
                if (rc == ErrorInsufficientBuffer && used > size)
                {
                    size = used;
                    continue;
                }
                if (rc != 0) return null;
                var count = *(uint*)(data + 4);
                for (var i = 0; i < count; i++)
                {
                    var entry = data + 8 + (i * sizeof(DependencyEntry));
                    var info = *(DependencyEntry*)entry;
                    if (info.AncestorLevel != 1 || info.HostVolumeName == IntPtr.Zero || info.DependentVolumeRelativePath == IntPtr.Zero) continue;
                    var host = Marshal.PtrToStringUni(info.HostVolumeName);
                    var relative = Marshal.PtrToStringUni(info.DependentVolumeRelativePath);
                    if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(relative)) continue;
                    return Path.Join(FriendlyVolume(host), relative.TrimStart('\\'));
                }
                return null;
            }
        }
        return null;
    }

    /// <summary>"\\?\Volume{…}\" → "C:\" (quando o volume tem letra).</summary>
    [SupportedOSPlatform("windows")]
    private static unsafe string FriendlyVolume(string volume)
    {
        var name = volume.EndsWith('\\') ? volume : volume + "\\";
        var buffer = stackalloc char[1024];
        if (GetVolumePathNamesForVolumeNameW(name, buffer, 1024, out _) && buffer[0] != '\0') return new string(buffer);
        return name;
    }

    private static FileOperationException Explain(uint code, string verb) => code switch
    {
        ErrorFileNotFound or ErrorPathNotFound => new(OperationErrorKind.DestinationUnavailable, "A imagem não existe mais."),
        ErrorAccessDenied or ErrorPrivilegeNotHeld => new(OperationErrorKind.AccessDenied,
            $"O Windows negou permissão para {verb} esta imagem. Discos rígidos virtuais (VHD/VHDX) exigem o ControlFS aberto como administrador."),
        ErrorSharingViolation => new(OperationErrorKind.AccessDenied, $"Outro programa está usando a imagem; feche-o e tente {verb} de novo."),
        ErrorFileCorrupt or ErrorDiskCorrupt => new(OperationErrorKind.Corrupt, "A imagem está danificada."),
        ErrorFileSystemLimitation or ErrorSparseNotAllowed => new(OperationErrorKind.UnsupportedFormat,
            "O Windows não monta esta imagem porque ela está compactada ou é um arquivo esparso; copie a imagem para outra pasta e tente de novo."),
        ErrorNotSupported or ErrorVirtdiskNotVirtualDisk or ErrorVirtdiskProviderNotFound or ErrorInvalidParameter => new(OperationErrorKind.UnsupportedFormat,
            "O Windows não reconheceu este arquivo como uma imagem de disco."),
        ErrorBusy or ErrorDeviceInUse => new(OperationErrorKind.AccessDenied, "A unidade está em uso; feche os arquivos abertos nela e tente de novo."),
        _ => new(OperationErrorKind.Unknown, $"O Windows não conseguiu {verb} a imagem (código {code})."),
    };

    // ---------------- Win32 ----------------

    private const uint StorageTypeIso = 1, StorageTypeVhd = 2, StorageTypeVhdx = 3;
    private static readonly Guid VendorMicrosoft = new("EC984AEC-A0F9-47e9-901F-71415A66345B");
    private const uint AccessGetInfo = 0x00080000, AccessDetach = 0x00040000;
    private const uint AttachReadOnly = 0x1, AttachPermanentLifetime = 0x4;
    private const uint DependencyHostVolumes = 0x1, DependencyDiskHandle = 0x2;
    private const uint FileShareReadWrite = 0x3, OpenExisting = 3;
    private const uint IoctlStorageGetDeviceNumber = 0x2D1080;

    private const uint ErrorFileNotFound = 2, ErrorPathNotFound = 3, ErrorAccessDenied = 5, ErrorSharingViolation = 32, ErrorNotSupported = 50,
        ErrorInvalidParameter = 87, ErrorInsufficientBuffer = 122, ErrorBusy = 170, ErrorFileSystemLimitation = 665, ErrorPrivilegeNotHeld = 1314,
        ErrorFileCorrupt = 1392, ErrorDiskCorrupt = 1393, ErrorDeviceInUse = 2404, ErrorSparseNotAllowed = 0xC03A001A,
        ErrorVirtdiskProviderNotFound = 0xC03A0014, ErrorVirtdiskNotVirtualDisk = 0xC03A0015;

    [StructLayout(LayoutKind.Sequential)]
    private struct VirtualStorageType
    {
        public uint DeviceId;
        public Guid VendorId;
    }

    /// <summary>OPEN_VIRTUAL_DISK_PARAMETERS (versões 1 e 2; o tamanho cobre a versão 3).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenParameters
    {
        public int Version;
        public int First; // Version1.RWDepth ou Version2.GetInfoOnly
        public int ReadOnly; // Version2.ReadOnly
        public Guid ResiliencyGuid;
        public Guid SnapshotId;
    }

    /// <summary>ATTACH_VIRTUAL_DISK_PARAMETERS: a união começa alinhada em 8 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AttachParameters
    {
        public int Version;
        public int Padding;
        public ulong Reserved;
        public ulong RestrictedLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceNumber
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    /// <summary>STORAGE_DEPENDENCY_INFO_TYPE_2.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DependencyEntry
    {
        public uint DependencyTypeFlags;
        public uint ProviderSpecificFlags;
        public VirtualStorageType VirtualStorageType;
        public uint AncestorLevel;
        public IntPtr DependencyDeviceName;
        public IntPtr HostVolumeName;
        public IntPtr DependentVolumeName;
        public IntPtr DependentVolumeRelativePath;
    }

    [LibraryImport("virtdisk.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial uint OpenVirtualDisk(ref VirtualStorageType type, string path, uint accessMask, uint flags, ref OpenParameters parameters, out SafeFileHandle handle);

    [LibraryImport("virtdisk.dll")]
    [SupportedOSPlatform("windows")]
    private static partial uint AttachVirtualDisk(SafeFileHandle disk, IntPtr securityDescriptor, uint flags, uint providerFlags, ref AttachParameters parameters, IntPtr overlapped);

    [LibraryImport("virtdisk.dll")]
    [SupportedOSPlatform("windows")]
    private static partial uint DetachVirtualDisk(SafeFileHandle disk, uint flags, uint providerFlags);

    [LibraryImport("virtdisk.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial uint GetVirtualDiskPhysicalPath(SafeFileHandle disk, ref uint sizeInBytes, char* path);

    [LibraryImport("virtdisk.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial uint GetStorageDependencyInformation(SafeFileHandle handle, uint flags, uint infoSize, byte* info, out uint sizeUsed);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, uint inputSize, out StorageDeviceNumber output, uint outputSize, out uint returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial uint GetLogicalDrives();

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNamesForVolumeNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static unsafe partial bool GetVolumePathNamesForVolumeNameW(string volume, char* names, uint length, out uint returned);
}
