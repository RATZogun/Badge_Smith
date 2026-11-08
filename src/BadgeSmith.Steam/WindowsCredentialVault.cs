using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BadgeSmith.Steam;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialVault : ISessionVault
{
    private const string Target = "BadgeSmith/steam-refresh-token";
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;
    private const int NotFound = 1168;

    public string? Read()
    {
        if (!CredReadW(Target, GenericCredential, 0, out var credential))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == NotFound ? null : throw Failure("ler a sessão do cofre", error);
        }

        try
        {
            var stored = Marshal.PtrToStructure<Credential>(credential);
            var blob = new byte[stored.CredentialBlobSize];
            Marshal.Copy(stored.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            CredFree(credential);
        }
    }

    public void Save(string refreshToken)
    {
        var blob = Encoding.UTF8.GetBytes(refreshToken);
        var target = Marshal.StringToHGlobalUni(Target);
        var data = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, data, blob.Length);
            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = blob.Length,
                CredentialBlob = data,
                Persist = PersistLocalMachine,
            };
            if (!CredWriteW(credential, 0))
            {
                throw Failure("gravar a sessão no cofre", Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(target);
        }
    }

    public void Delete()
    {
        if (!CredDeleteW(Target, GenericCredential, 0) && Marshal.GetLastPInvokeError() is var error and not NotFound)
        {
            throw Failure("apagar a sessão do cofre", error);
        }
    }

    private static SessionVaultException Failure(string action, int error) =>
        new($"Não foi possível {action} (erro {error} do Gerenciador de Credenciais).");

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(in Credential credential, int flags);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, int type, int flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);
}
