using System.Diagnostics;
using System.Text.Json;

namespace BadgeSmith.Core;

public sealed class AgentAchievementClient(string agentPath) : IAchievementAgent
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AchievementState>> ListAsync(uint appId, string language, CancellationToken cancellationToken)
    {
        var lines = await RunAsync([appId.ToString(), "achievements", "list", "--language", language], cancellationToken);
        return lines.Select(line => JsonSerializer.Deserialize<AchievementState>(line, Json)!).ToList();
    }

    public async Task<IReadOnlyList<AchievementChange>> ChangeAsync(uint appId, IReadOnlyList<string> apiNames, bool unlock, CancellationToken cancellationToken)
    {
        var lines = await RunAsync([appId.ToString(), "achievements", unlock ? "unlock" : "lock", .. apiNames], cancellationToken, acceptStoreFailure: true);
        return lines.Select(line => JsonSerializer.Deserialize<AchievementChange>(line, Json)!).ToList();
    }

    private async Task<IReadOnlyList<string>> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken, bool acceptStoreFailure = false)
    {
        var startInfo = new ProcessStartInfo(agentPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        Process? started;
        try
        {
            started = Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new AchievementAgentException(AgentExitCode.AgentNotStarted, $"Não foi possível iniciar o agente em {agentPath} ({error.Message}).");
        }

        using var process = started
            ?? throw new AchievementAgentException(AgentExitCode.AgentNotStarted, $"Não foi possível iniciar o agente em {agentPath}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var code = (AgentExitCode)process.ExitCode;
            if (code != AgentExitCode.Stopped && !(acceptStoreFailure && code == AgentExitCode.StoreFailed))
            {
                var message = (await errors).Trim();
                throw new AchievementAgentException(code, message.Length > 0 ? message : $"O agente terminou com o código {process.ExitCode}.");
            }

            return (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new AchievementAgentException(AgentExitCode.StatsUnavailable, $"O agente não respondeu em {Timeout.TotalSeconds:0} s.");
        }
    }
}
