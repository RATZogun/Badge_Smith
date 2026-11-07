using BadgeSmith.Core;
using BadgeSmith.Steam;

const string CookieVariable = "BADGESMITH_STEAM_LOGIN_SECURE";

var command = args.FirstOrDefault();
if (command is not ("scan" or "farm"))
{
    Console.Error.WriteLine("Uso: BadgeSmith.Cli scan | farm [--agent <caminho>] [--hours <horas>]");
    return 2;
}

DotEnv.Load(Path.Combine(Environment.CurrentDirectory, ".env"));
var cookie = Environment.GetEnvironmentVariable(CookieVariable);
if (string.IsNullOrWhiteSpace(cookie))
{
    Console.Error.WriteLine($"Defina {CookieVariable} no ambiente ou no arquivo .env (veja o .env.example).");
    return 3;
}

var options = new FarmOptions
{
    HoursBeforeDrops = Option("--hours") is { } hours && double.TryParse(hours, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 2,
};

var session = new SteamWebSession(cookie);
using var scanner = new BadgeScanner(session);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    if (command == "scan")
    {
        var badges = await scanner.ScanAsync(cancel.Token);
        var pending = badges.Where(b => b.RemainingDrops > 0).OrderByDescending(b => b.HoursPlayed).ToList();
        foreach (var b in pending)
        {
            Console.WriteLine($"{b.AppId,10}  {b.RemainingDrops,2} carta(s)  {b.HoursPlayed,7:0.0} h  {b.Name}");
        }

        Console.WriteLine($"{pending.Count} jogo(s) com {pending.Sum(b => b.RemainingDrops)} carta(s) a dropar.");
        var step = FarmPlanner.Next(pending, options);
        Console.WriteLine(step is null
            ? "Nada para farmar."
            : $"Próximo passo: {step.Mode} com {step.Games.Count} jogo(s) por {step.Duration.TotalMinutes:0} min.");
        return 0;
    }

    var agentPath = Option("--agent") ?? DefaultAgentPath();
    if (!File.Exists(agentPath))
    {
        Console.Error.WriteLine($"Agente não encontrado em {agentPath}. Compile o BadgeSmith.Agent ou use --agent.");
        return 4;
    }

    await using var launcher = new AgentLauncher(agentPath);
    var runner = new FarmRunner(scanner.ScanAsync, launcher, options);
    await runner.RunAsync(Report, cancel.Token);
    return 0;
}
catch (SteamSessionExpiredException error)
{
    Console.Error.WriteLine(error.Message);
    return 5;
}

void Report(FarmEvent e)
{
    var now = DateTime.Now.ToString("HH:mm:ss");
    Console.WriteLine(e switch
    {
        FarmEvent.Scanned s => $"[{now}] {s.GamesWithDrops} jogo(s), {s.TotalDrops} carta(s) a dropar",
        FarmEvent.Farming f => $"[{now}] {f.Step.Mode}: {string.Join(", ", f.Step.Games.Where(g => f.Started.Contains(g.AppId)).Select(g => g.Name))} por {f.Step.Duration.TotalMinutes:0} min",
        FarmEvent.GameSkipped s => $"[{now}] Jogo {s.AppId} ignorado nesta sessão: {s.Reason}",
        FarmEvent.ScanFailed s => $"[{now}] Falha ao ler as badges ({s.Error.Message}); nova tentativa em {s.RetryIn.TotalSeconds:0} s",
        FarmEvent.Finished { Skipped.Count: 0 } => $"[{now}] Todas as cartas foram dropadas.",
        FarmEvent.Finished f => $"[{now}] Fim: {f.Skipped.Count} jogo(s) ignorado(s) por falha do agente ainda têm cartas.",
        _ => e.ToString(),
    });
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string DefaultAgentPath()
{
    var name = OperatingSystem.IsWindows() ? "BadgeSmith.Agent.exe" : "BadgeSmith.Agent";
    var sibling = Path.Combine(AppContext.BaseDirectory, name);
    if (File.Exists(sibling))
    {
        return sibling;
    }

    var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BadgeSmith.Agent", "bin", configuration, "net10.0", name));
}
