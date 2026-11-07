using System.Diagnostics;

namespace BadgeSmith.Core;

public sealed class AgentLauncher(string agentPath) : IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly List<Process> _running = [];

    public IReadOnlyCollection<uint> RunningAppIds { get; private set; } = [];

    public async Task<IReadOnlyList<uint>> StartAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken)
    {
        var started = new List<uint>();
        foreach (var appId in appIds)
        {
            var process = Process.Start(new ProcessStartInfo(agentPath)
            {
                ArgumentList = { appId.ToString(), "--parent-pid", Environment.ProcessId.ToString(), "--stdin-control" },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException($"Não foi possível iniciar o agente para {appId}.");

            if (await WaitReadyAsync(process, cancellationToken))
            {
                _running.Add(process);
                started.Add(appId);
            }
            else
            {
                await StopAsync(process);
            }
        }

        RunningAppIds = started;
        return started;
    }

    public async Task StopAllAsync()
    {
        await Task.WhenAll(_running.Select(StopAsync));
        _running.Clear();
        RunningAppIds = [];
    }

    public ValueTask DisposeAsync() => new(StopAllAsync());

    private static async Task<bool> WaitReadyAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadyTimeout);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith("READY ", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return false;
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("stop");
                await process.StandardInput.FlushAsync();
                using var timeout = new CancellationTokenSource(StopTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            process.Dispose();
        }
    }
}
