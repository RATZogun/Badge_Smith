using BadgeSmith.Core;
using BadgeSmith.Steam;
using QRCoder;

const string CookieVariable = "BADGESMITH_STEAM_LOGIN_SECURE";

var command = args.FirstOrDefault();
if (command is not ("login" or "logout" or "scan" or "farm"))
{
    Console.Error.WriteLine("Uso: BadgeSmith.Cli login [--user <conta>] | logout | scan | farm [--agent <caminho>] [--hours <horas>]");
    return 2;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

using var auth = new SteamAuthApi();

try
{
    var vault = SessionVault.ForCurrentSystem();
    if (command == "login")
    {
        return await LoginAsync(vault);
    }

    if (command == "logout")
    {
        vault.Delete();
        Console.WriteLine("Sessão apagada do cofre do sistema.");
        return 0;
    }

    DotEnv.Load(Path.Combine(Environment.CurrentDirectory, ".env"));
    Func<CancellationToken, Task<SteamWebSession>> openSession;
    if (Environment.GetEnvironmentVariable(CookieVariable) is { Length: > 0 } cookie)
    {
        openSession = _ => Task.FromResult(new SteamWebSession(cookie));
    }
    else if (vault.Read() is { } refreshToken)
    {
        openSession = async ct => new SteamWebSession(await auth.GetLoginCookieAsync(refreshToken, ct));
    }
    else
    {
        Console.Error.WriteLine("Nenhuma sessão da Steam salva. Rode \"BadgeSmith.Cli login\" primeiro.");
        return 3;
    }

    var options = new FarmOptions
    {
        HoursBeforeDrops = Option("--hours") is { } hours && double.TryParse(hours, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 2,
    };

    using var scanner = new RenewingBadgeScanner(openSession);
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
catch (Exception error) when (error is SteamSessionExpiredException or SteamAuthException or SessionVaultException)
{
    Console.Error.WriteLine(error.Message);
    return 5;
}
catch (OperationCanceledException) when (cancel.IsCancellationRequested)
{
    return 1;
}

async Task<int> LoginAsync(ISessionVault vault)
{
    var login = new SteamLogin(auth);
    SteamLoginResult result;
    if (Option("--user") is { } accountName)
    {
        Console.Write("Senha: ");
        var password = ReadSecret();
        result = await login.WithCredentialsAsync(accountName, password, AskCodeAsync, WaitingConfirmation, cancel.Token);
    }
    else
    {
        Console.WriteLine("No app Steam do celular, abra o Steam Guard e escaneie o QR code (Ctrl+C para cancelar):");
        result = await login.WithQrAsync(ShowQrCode, cancel.Token);
    }

    var session = new SteamWebSession(await auth.GetLoginCookieAsync(result.RefreshToken, cancel.Token));
    vault.Save(result.RefreshToken);
    Console.WriteLine($"Logado como {result.AccountName} ({session.SteamId}). A sessão ficou guardada no cofre do sistema.");
    return 0;
}

static void ShowQrCode(string challengeUrl)
{
    using var data = QRCodeGenerator.GenerateQrCode(challengeUrl, QRCodeGenerator.ECCLevel.L);
    Console.WriteLine(new AsciiQRCode(data).GetGraphicSmall());
}

static Task<string> AskCodeAsync(SteamGuardType type, bool previousWasWrong, CancellationToken cancellationToken)
{
    if (previousWasWrong)
    {
        Console.WriteLine("Código incorreto.");
    }

    Console.Write(type == SteamGuardType.EmailCode
        ? "Código do Steam Guard enviado para o seu e-mail: "
        : "Código do Steam Guard do app no celular: ");
    return Task.FromResult(Console.ReadLine() ?? "");
}

static void WaitingConfirmation(SteamGuardType type) =>
    Console.WriteLine(type == SteamGuardType.EmailConfirmation
        ? "Confirme o login pelo link que a Steam enviou para o seu e-mail."
        : "Aprove o login no app Steam do celular.");

static string ReadSecret()
{
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? "";
    }

    var secret = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return secret.ToString();
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (secret.Length > 0)
            {
                secret.Length--;
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            secret.Append(key.KeyChar);
        }
    }
}

void Report(FarmEvent e)
{
    var now = DateTime.Now.ToString("HH:mm:ss");
    Console.WriteLine(e switch
    {
        FarmEvent.Scanned s => $"[{now}] {s.GamesWithDrops} jogo(s), {s.TotalDrops} carta(s) a dropar",
        FarmEvent.Farming f => $"[{now}] {f.Step.Mode}: {string.Join(", ", f.Step.Games.Where(g => f.Started.Contains(g.AppId)).Select(g => g.Name))} por {f.Step.Duration.TotalMinutes:0} min",
        FarmEvent.GameSkipped s => $"[{now}] Jogo {s.AppId} ignorado nesta sessão: {s.Reason}",
        FarmEvent.SteamUnavailable s => $"[{now}] Steam indisponível ({s.Reason}); farm pausado, nova tentativa em {s.RetryIn.TotalSeconds:0} s",
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
