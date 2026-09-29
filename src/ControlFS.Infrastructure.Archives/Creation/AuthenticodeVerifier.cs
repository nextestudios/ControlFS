using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ControlFS.Infrastructure.Archives.Creation;

/// <summary>Confere a assinatura Authenticode de um executável (cadeia válida, sem rede) e quem o assinou.</summary>
internal static partial class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [LibraryImport("wintrust.dll", SetLastError = false)]
    private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    /// <summary>
    /// True quando a assinatura é válida para o Windows e o assinante (CN) contém <paramref name="expectedSigner"/>.
    /// Não consulta a rede (revogação não é verificada: o arquivo fica em pasta que só administradores gravam).
    /// </summary>
    public static bool IsSignedBy(string path, string expectedSigner)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (!IsTrusted(path)) return false;
#pragma warning disable SYSLIB0057 // lê só o assinante embutido; a validade da cadeia já veio do WinVerifyTrust
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false).Contains(expectedSigner, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool IsTrusted(string path)
    {
        var pathPtr = Marshal.StringToCoTaskMemUni(path);
        var filePtr = IntPtr.Zero;
        try
        {
            var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = pathPtr };
            filePtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2,           // WTD_UI_NONE
                fdwRevocationChecks = 0,  // WTD_REVOKE_NONE
                dwUnionChoice = 1,        // WTD_CHOICE_FILE
                pFile = filePtr,
                dwProvFlags = 0x1000 | 0x10, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE: sem rede
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        finally
        {
            if (filePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(filePtr);
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
