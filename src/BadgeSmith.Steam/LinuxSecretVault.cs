using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BadgeSmith.Steam;

[SupportedOSPlatform("linux")]
public sealed partial class LinuxSecretVault : ISessionVault
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string SchemaName = "io.github.ratzogun.BadgeSmith";
    private const string AttributeName = "kind";
    private const string AttributeValue = "steam-refresh-token";
    private const string Label = "Badge Smith: sessão da Steam";
    private const int SchemaSize = 1024;
    private const int FirstAttributeOffset = 16;

    public string? Read() => WithAttributes((schema, attributes) =>
    {
        var secret = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, out var error);
        ThrowIfError(error, "ler a sessão do cofre");
        if (secret == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(secret);
        }
        finally
        {
            secret_password_free(secret);
        }
    });

    public void Save(string refreshToken) => WithAttributes((schema, attributes) =>
    {
        secret_password_storev_sync(schema, attributes, null, Label, refreshToken, IntPtr.Zero, out var error);
        ThrowIfError(error, "gravar a sessão no cofre");
        return true;
    });

    public void Delete() => WithAttributes((schema, attributes) =>
    {
        secret_password_clearv_sync(schema, attributes, IntPtr.Zero, out var error);
        ThrowIfError(error, "apagar a sessão do cofre");
        return true;
    });

    private static T WithAttributes<T>(Func<IntPtr, IntPtr, T> action)
    {
        var schemaName = Marshal.StringToCoTaskMemUTF8(SchemaName);
        var attributeName = Marshal.StringToCoTaskMemUTF8(AttributeName);
        var attributeValue = Marshal.StringToCoTaskMemUTF8(AttributeValue);
        var schema = Marshal.AllocHGlobal(SchemaSize);
        var attributes = IntPtr.Zero;
        try
        {
            unsafe
            {
                new Span<byte>((void*)schema, SchemaSize).Clear();
            }

            Marshal.WriteIntPtr(schema, 0, schemaName);
            Marshal.WriteIntPtr(schema, FirstAttributeOffset, attributeName);

            var glib = NativeLibrary.Load(GLib);
            attributes = g_hash_table_new(NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
            g_hash_table_insert(attributes, attributeName, attributeValue);
            return action(schema, attributes);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new SessionVaultException($"Não foi possível usar o cofre do sistema: instale a libsecret ({error.Message}).");
        }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                g_hash_table_unref(attributes);
            }

            Marshal.FreeHGlobal(schema);
            Marshal.FreeCoTaskMem(schemaName);
            Marshal.FreeCoTaskMem(attributeName);
            Marshal.FreeCoTaskMem(attributeValue);
        }
    }

    private static void ThrowIfError(IntPtr error, string action)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
        g_error_free(error);
        throw new SessionVaultException($"Não foi possível {action}: {message}");
    }

    [LibraryImport(LibSecret)]
    private static partial IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool secret_password_storev_sync(IntPtr schema, IntPtr attributes, string? collection, string label, string password, IntPtr cancellable, out IntPtr error);

    [LibraryImport(LibSecret)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [LibraryImport(LibSecret)]
    private static partial void secret_password_free(IntPtr password);

    [LibraryImport(GLib)]
    private static partial IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);

    [LibraryImport(GLib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [LibraryImport(GLib)]
    private static partial void g_hash_table_unref(IntPtr table);

    [LibraryImport(GLib)]
    private static partial void g_error_free(IntPtr error);
}
