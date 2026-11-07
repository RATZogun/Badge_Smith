using System.Diagnostics;

namespace BadgeSmith.Core;

public sealed class AgentLauncher(string agentPath) : IAgentLauncher, IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly List<Process> _running = [];

    public IReadOnlyCollection<uint> RunningAppIds { get; private set; } = [];

    public async Task<AgentStartResult> StartAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken)
    {
        var started = new List<uint>();
        var failed = new Dictionary<uint, string>();
        foreach (var appId in appIds)
        {
            var errors = new List<string>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(agentPath)
                {
                    ArgumentList = { appId.ToString(), "--parent-pid", Environment.ProcessId.ToString(), "--stdin-control" },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (errors)
                    {
                        errors.Add(e.Data.Trim());
                    }
                }
            };

            try
            {
                process.Start();
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                process.Dispose();
                failed[appId] = $"o agente não pôde ser iniciado ({error.Message})";
                continue;
            }

            process.BeginErrorReadLine();
            if (await WaitReadyAsync(process, cancellationToken))
            {
                _running.Add(process);
                started.Add(appId);
                continue;
            }

            var exited = process.HasExited;
            if (exited)
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await StopAsync(process);
            string reason;
            lock (errors)
            {
                reason = errors.Count > 0
                    ? string.Join(" ", errors)
                    : exited ? "o agente encerrou sem responder" : $"o agente não respondeu em {ReadyTimeout.TotalSeconds:0} s";
            }

            failed[appId] = reason;
        }

        RunningAppIds = started;
        return new AgentStartResult(started, failed);
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
