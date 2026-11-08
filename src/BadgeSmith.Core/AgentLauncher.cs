using System.Diagnostics;

namespace BadgeSmith.Core;

public sealed class AgentLauncher(string agentPath, ulong? steamId = null) : IAgentLauncher, IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private const uint ProbeAppId = 480;

    private readonly List<Process> _running = [];

    public IReadOnlyCollection<uint> RunningAppIds { get; private set; } = [];

    public async Task<AgentStartResult> StartAsync(IEnumerable<uint> appIds, CancellationToken cancellationToken)
    {
        var started = new List<uint>();
        var failed = new Dictionary<uint, string>();
        var steamLoggedIn = false;
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
            if (steamId is { } id)
            {
                process.StartInfo.ArgumentList.Add("--steam-id");
                process.StartInfo.ArgumentList.Add(id.ToString());
            }

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
            var outcome = await WaitReadyAsync(process, cancellationToken);
            if (outcome == ReadyOutcome.Ready)
            {
                _running.Add(process);
                started.Add(appId);
                continue;
            }

            var exited = outcome == ReadyOutcome.Exited;
            int? exitCode = null;
            if (exited)
            {
                using var exitTimeout = new CancellationTokenSource(StopTimeout);
                try
                {
                    await process.WaitForExitAsync(exitTimeout.Token);
                    exitCode = process.ExitCode;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await StopAsync(process);
            string reason;
            lock (errors)
            {
                reason = errors.Count > 0
                    ? string.Join(" ", errors)
                    : exited ? "o agente encerrou sem responder" : $"o agente não respondeu em {ReadyTimeout.TotalSeconds:0} s";
            }

            if (exitCode is { } code && AgentExitCodes.IsSteamUnavailable(code))
            {
                RunningAppIds = started;
                return new AgentStartResult(started, failed, reason);
            }

            if (exitCode == (int)AgentExitCode.GameNotAvailable && started.Count == 0 && !steamLoggedIn)
            {
                var (probeCode, probeError) = await ProbeSteamAsync(cancellationToken);
                steamLoggedIn = probeCode == (int)AgentExitCode.Stopped;
                if (!steamLoggedIn)
                {
                    RunningAppIds = started;
                    return new AgentStartResult(
                        started,
                        failed,
                        probeCode == (int)AgentExitCode.WrongAccount ? probeError : "a Steam está aberta, mas sem usuário logado");
                }
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

    private async Task<(int? ExitCode, string Error)> ProbeSteamAsync(CancellationToken cancellationToken)
    {
        using var probe = new Process
        {
            StartInfo = new ProcessStartInfo(agentPath)
            {
                ArgumentList = { ProbeAppId.ToString(), "--seconds", "1", "--parent-pid", Environment.ProcessId.ToString() },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        if (steamId is { } id)
        {
            probe.StartInfo.ArgumentList.Add("--steam-id");
            probe.StartInfo.ArgumentList.Add(id.ToString());
        }

        try
        {
            probe.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            var output = probe.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = probe.StandardError.ReadToEndAsync(timeout.Token);
            await probe.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, errors);
            return (probe.ExitCode, (await errors).Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            probe.Kill(entireProcessTree: true);
            return (null, "");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (null, "");
        }
    }

    private enum ReadyOutcome
    {
        Ready,
        Exited,
        TimedOut,
    }

    private static async Task<ReadyOutcome> WaitReadyAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadyTimeout);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith("READY ", StringComparison.Ordinal))
                {
                    return ReadyOutcome.Ready;
                }
            }

            return ReadyOutcome.Exited;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReadyOutcome.TimedOut;
        }
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
