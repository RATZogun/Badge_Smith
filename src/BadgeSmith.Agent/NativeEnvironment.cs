using System.Runtime.InteropServices;

namespace BadgeSmith.Agent;

internal static partial class NativeEnvironment
{
    public static void Set(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (!OperatingSystem.IsWindows() && SetEnv(name, value, 1) != 0)
        {
            throw new InvalidOperationException($"setenv falhou para {name}.");
        }
    }

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnv(string name, string value, int overwrite);
}
