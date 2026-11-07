using System.Runtime.InteropServices;

namespace BadgeSmith.Agent;

internal sealed unsafe class SteamUserStats(IntPtr stats, SteamCallbacks callbacks)
{
    private const int UserStatsReceived = 1101;
    private const int UserStatsStored = 1102;
    private const int ResultOk = 1;
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(15);

    private readonly IntPtr* _vtable = *(IntPtr**)stats;

    public bool Load(uint appId)
    {
        if (((delegate* unmanaged<IntPtr, byte>)_vtable[0])(stats) == 0)
        {
            return false;
        }

        var received = callbacks.WaitFor(UserStatsReceived, data => BitConverter.ToUInt64(data, 0) == appId, CallbackTimeout);
        return received is not null && BitConverter.ToInt32(received, 8) == ResultOk;
    }

    public (bool Achieved, uint UnlockTime)? Get(string apiName)
    {
        using var name = new NativeString(apiName);
        byte achieved = 0;
        uint unlockTime = 0;
        var ok = ((delegate* unmanaged<IntPtr, byte*, byte*, uint*, byte>)_vtable[9])(stats, name.Pointer, &achieved, &unlockTime);
        return ok == 0 ? null : (achieved != 0, unlockTime);
    }

    public bool Set(string apiName, bool achieved)
    {
        using var name = new NativeString(apiName);
        var index = achieved ? 7 : 8;
        return ((delegate* unmanaged<IntPtr, byte*, byte>)_vtable[index])(stats, name.Pointer) != 0;
    }

    public bool Store(uint appId)
    {
        if (((delegate* unmanaged<IntPtr, byte>)_vtable[10])(stats) == 0)
        {
            return false;
        }

        var stored = callbacks.WaitFor(UserStatsStored, data => BitConverter.ToUInt64(data, 0) == appId, CallbackTimeout);
        return stored is not null && BitConverter.ToInt32(stored, 8) == ResultOk;
    }

    private readonly struct NativeString : IDisposable
    {
        private readonly IntPtr _pointer;

        public NativeString(string value) => _pointer = Marshal.StringToCoTaskMemUTF8(value);

        public byte* Pointer => (byte*)_pointer;

        public void Dispose() => Marshal.FreeCoTaskMem(_pointer);
    }
}
