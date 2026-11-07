using System.Runtime.InteropServices;

namespace BadgeSmith.Agent;

internal sealed unsafe class SteamClient : IDisposable
{
    private static readonly string[] ClientVersions = ["SteamClient021", "SteamClient020", "SteamClient019", "SteamClient018", "SteamClient017"];
    private static readonly string[] UserVersions = ["SteamUser023", "SteamUser022", "SteamUser021", "SteamUser020"];
    private static readonly string[] UtilsVersions = ["SteamUtils010"];
    private static readonly string[] UserStatsVersions = ["STEAMUSERSTATS_INTERFACE_VERSION012"];

    private readonly IntPtr _library;
    private readonly IntPtr _client;
    private readonly IntPtr* _clientVtable;
    private int _pipe;
    private int _user;

    public string Version { get; }

    private SteamClient(IntPtr library, IntPtr client, string version)
    {
        _library = library;
        _client = client;
        _clientVtable = *(IntPtr**)client;
        Version = version;
    }

    public static SteamClient Open(string libraryPath)
    {
        var library = NativeLibrary.Load(libraryPath);
        var createInterface = (delegate* unmanaged<byte*, int*, IntPtr>)NativeLibrary.GetExport(library, "CreateInterface");

        foreach (var version in ClientVersions)
        {
            var name = Marshal.StringToHGlobalAnsi(version);
            try
            {
                int returnCode = 0;
                var client = createInterface((byte*)name, &returnCode);
                if (client != IntPtr.Zero)
                {
                    return new SteamClient(library, client, version);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        NativeLibrary.Free(library);
        throw new InvalidOperationException("Nenhuma versão conhecida da interface SteamClient foi encontrada.");
    }

    public bool Connect()
    {
        _pipe = ((delegate* unmanaged<IntPtr, int>)_clientVtable[0])(_client);
        if (_pipe == 0)
        {
            return false;
        }

        _user = ((delegate* unmanaged<IntPtr, int, int>)_clientVtable[2])(_client, _pipe);
        return _user != 0;
    }

    public bool? IsLoggedOn()
    {
        var user = GetInterface(UserVersions, name =>
            ((delegate* unmanaged<IntPtr, int, int, byte*, IntPtr>)_clientVtable[5])(_client, _user, _pipe, (byte*)name));
        if (user == IntPtr.Zero)
        {
            return null;
        }

        var userVtable = *(IntPtr**)user;
        return ((delegate* unmanaged<IntPtr, byte>)userVtable[1])(user) != 0;
    }

    public uint? GetAppId()
    {
        var utils = GetInterface(UtilsVersions, name =>
            ((delegate* unmanaged<IntPtr, int, byte*, IntPtr>)_clientVtable[9])(_client, _pipe, (byte*)name));
        if (utils == IntPtr.Zero)
        {
            return null;
        }

        var utilsVtable = *(IntPtr**)utils;
        return ((delegate* unmanaged<IntPtr, uint>)utilsVtable[9])(utils);
    }

    public SteamUserStats? GetUserStats()
    {
        var stats = GetInterface(UserStatsVersions, name =>
            ((delegate* unmanaged<IntPtr, int, int, byte*, IntPtr>)_clientVtable[13])(_client, _user, _pipe, (byte*)name));
        return stats == IntPtr.Zero ? null : new SteamUserStats(stats, new SteamCallbacks(_library, _pipe));
    }

    private static IntPtr GetInterface(string[] versions, Func<IntPtr, IntPtr> get)
    {
        foreach (var version in versions)
        {
            var name = Marshal.StringToHGlobalAnsi(version);
            try
            {
                var found = get(name);
                if (found != IntPtr.Zero)
                {
                    return found;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_user != 0)
        {
            ((delegate* unmanaged<IntPtr, int, int, void>)_clientVtable[4])(_client, _pipe, _user);
            _user = 0;
        }

        if (_pipe != 0)
        {
            ((delegate* unmanaged<IntPtr, int, byte>)_clientVtable[1])(_client, _pipe);
            _pipe = 0;
        }

        NativeLibrary.Free(_library);
    }
}
