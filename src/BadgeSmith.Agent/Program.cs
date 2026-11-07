using System.Diagnostics;
using System.Runtime.InteropServices;
using BadgeSmith.Agent;
using BadgeSmith.Core;

if (args.Length < 1 || !uint.TryParse(args[0], out var appId) || appId == 0)
{
    Console.Error.WriteLine("Uso: BadgeSmith.Agent <appId> [--seconds N] [--parent-pid PID] [--stdin-control]");
    return (int)AgentExitCode.InvalidArguments;
}

int? seconds = null;
int? parentPid = null;
var stdinControl = false;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s) && s > 0:
            seconds = s;
            i++;
            break;
        case "--parent-pid" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
            parentPid = p;
            i++;
            break;
        case "--stdin-control":
            stdinControl = true;
            break;
    }
}

var libraryPath = SteamClientLibrary.FindPath();
if (libraryPath is null)
{
    Console.Error.WriteLine("ERRO steamclient não encontrado: a Steam está instalada?");
    return (int)AgentExitCode.SteamNotInstalled;
}

NativeEnvironment.Set("SteamAppId", appId.ToString());
NativeEnvironment.Set("SteamGameId", appId.ToString());

SteamClient opened;
try
{
    opened = SteamClient.Open(libraryPath);
}
catch (InvalidOperationException error)
{
    Console.Error.WriteLine($"ERRO {error.Message}");
    return (int)AgentExitCode.UnsupportedSteam;
}
catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
{
    Console.Error.WriteLine($"ERRO o steamclient em {libraryPath} não pôde ser carregado: a instalação da Steam está íntegra?");
    return (int)AgentExitCode.SteamNotInstalled;
}

using var client = opened;

if (!client.Connect())
{
    Console.Error.WriteLine("ERRO não foi possível conectar ao usuário da Steam: a Steam está aberta e logada?");
    return (int)AgentExitCode.SteamNotRunning;
}

switch (client.IsLoggedOn())
{
    case null:
        Console.Error.WriteLine($"ERRO versão da interface SteamUser não suportada por esta Steam ({client.Version}).");
        return (int)AgentExitCode.UnsupportedSteam;
    case false:
        Console.Error.WriteLine("ERRO a Steam não está logada.");
        return (int)AgentExitCode.SteamNotLoggedOn;
}

var steamAppId = client.GetAppId();
if (steamAppId is null)
{
    Console.Error.WriteLine($"ERRO versão da interface SteamUtils não suportada por esta Steam ({client.Version}).");
    return (int)AgentExitCode.UnsupportedSteam;
}

if (steamAppId != appId)
{
    Console.Error.WriteLine($"ERRO a Steam registrou o appId {steamAppId} em vez de {appId}.");
    return (int)AgentExitCode.WrongAppId;
}

using var cancel = seconds is { } limit ? new CancellationTokenSource(TimeSpan.FromSeconds(limit)) : new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancel.Cancel();
});

if (parentPid is { } pid)
{
    _ = Task.Run(async () =>
    {
        while (!cancel.IsCancellationRequested)
        {
            if (!IsRunning(pid))
            {
                cancel.Cancel();
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    });
}

if (stdinControl)
{
    _ = Task.Run(() =>
    {
        while (Console.In.ReadLine() is { } line)
        {
            if (line.Trim() == "stop")
            {
                break;
            }
        }

        cancel.Cancel();
    });
}

Console.WriteLine($"READY {appId}");

try
{
    await Task.Delay(Timeout.Infinite, cancel.Token);
}
catch (OperationCanceledException)
{
}

Console.WriteLine($"STOPPED {appId}");
return (int)AgentExitCode.Stopped;

static bool IsRunning(int pid)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        return !process.HasExited;
    }
    catch (ArgumentException)
    {
        return false;
    }
}
