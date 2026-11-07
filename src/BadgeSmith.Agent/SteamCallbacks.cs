using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BadgeSmith.Agent;

internal sealed unsafe class SteamCallbacks(IntPtr library, int pipe)
{
    private readonly delegate* unmanaged<int, byte*, byte> _getCallback =
        (delegate* unmanaged<int, byte*, byte>)NativeLibrary.GetExport(library, "Steam_BGetCallback");

    private readonly delegate* unmanaged<int, byte> _freeLastCallback =
        (delegate* unmanaged<int, byte>)NativeLibrary.GetExport(library, "Steam_FreeLastCallback");

    public byte[]? WaitFor(int callbackId, Func<byte[], bool> accept, TimeSpan timeout)
    {
        var message = stackalloc byte[32];
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (_getCallback(pipe, message) == 0)
            {
                Thread.Sleep(50);
                continue;
            }

            try
            {
                var id = *(int*)(message + 4);
                var parameter = *(IntPtr*)(message + 8);
                var size = *(int*)(message + 16);
                if (id == callbackId && parameter != IntPtr.Zero && size > 0)
                {
                    var data = new byte[size];
                    Marshal.Copy(parameter, data, 0, size);
                    if (accept(data))
                    {
                        return data;
                    }
                }
            }
            finally
            {
                _freeLastCallback(pipe);
            }
        }

        return null;
    }
}
