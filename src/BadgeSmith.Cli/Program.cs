using BadgeSmith.Core;
using BadgeSmith.Steam;
using QRCoder;

const string CookieVariable = "BADGESMITH_STEAM_LOGIN_SECURE";

var command = args.FirstOrDefault();
if (command is not ("login" or "logout" or "scan" or "farm"))
{
    Console.Error.WriteLine("Uso: BadgeSmith.Cli login [--agent <caminho>] | logout | scan | farm [--agent <caminho>] [--hours <horas>]");
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
        return await LogoutAsync(vault);
    }

    DotEnv.Load(Path.Combine(Environment.CurrentDirectory, ".env"));
    Func<CancellationToken, Task<SteamWebSession>> openSession;
    ulong? steamId;
    if (Environment.GetEnvironmentVariable(CookieVariable) is { Length: > 0 } cookie)
    {
        Console.Error.WriteLine($"Usando o cookie de {CookieVariable} (desenvolvimento) no lugar da sessão salva.");
        var fixedSession = new SteamWebSession(cookie);
        openSession = _ => Task.FromResult(fixedSession);
        steamId = fixedSession.SteamId;
    }
    else if (vault.Read() is { } stored && SteamWebSession.IsCookie(stored))
    {
        var storedSession = new SteamWebSession(stored);
        openSession = _ => Task.FromResult(storedSession);
        steamId = storedSession.SteamId;
    }
    else if (vault.Read() is { } refreshToken)
    {
        openSession = async ct => new SteamWebSession(await auth.GetLoginCookieAsync(refreshToken, ct));
        steamId = SteamAuthApi.SteamIdFromToken(refreshToken);
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

    var agentPath = Option("--agent") ?? AgentLocator.DefaultPath();
    if (!File.Exists(agentPath))
    {
        Console.Error.WriteLine($"Agente não encontrado em {agentPath}. Compile o BadgeSmith.Agent ou use --agent.");
        return 4;
    }

    await using var launcher = new AgentLauncher(agentPath, steamId);
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
catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
{
    Console.Error.WriteLine($"Não foi possível falar com a Steam ({error.Message}). Confira a conexão e tente de novo.");
    return 6;
}

async Task<int> LoginAsync(ISessionVault vault)
{
    var result = await new SteamLogin(auth).WithQrAsync(ShowQrCode, cancel.Token);

    var session = new SteamWebSession(await auth.GetLoginCookieAsync(result.RefreshToken, cancel.Token));
    var previous = vault.Read();
    vault.Save(result.RefreshToken);
    if (previous is not null && previous != result.RefreshToken && !SteamWebSession.IsCookie(previous))
    {
        await TryRevokeAsync(previous);
    }

    Console.WriteLine($"Logado como {result.AccountName} ({session.SteamId}). A sessão ficou guardada no cofre do sistema.");

    var agentPath = Option("--agent") ?? AgentLocator.DefaultPath();
    if (File.Exists(agentPath))
    {
        await using var launcher = new AgentLauncher(agentPath, session.SteamId);
        if (await launcher.CheckAccountAsync(cancel.Token) is { } mismatch)
        {
            Console.Error.WriteLine($"Atenção: {mismatch.Replace("ERRO ", "")} Entre na Steam com a conta {result.AccountName} antes de farmar.");
        }
    }

    return 0;
}

async Task<int> LogoutAsync(ISessionVault vault)
{
    var stored = vault.Read();
    vault.Delete();
    if (stored is not null && !SteamWebSession.IsCookie(stored))
    {
        await TryRevokeAsync(stored);
    }

    Console.WriteLine("Sessão encerrada e apagada do cofre do sistema.");
    return 0;
}

async Task TryRevokeAsync(string refreshToken)
{
    try
    {
        await auth.RevokeAsync(refreshToken, cancel.Token);
    }
    catch (SteamAuthException)
    {
    }
    catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
    {
        Console.Error.WriteLine($"Não foi possível encerrar a sessão na Steam ({error.Message}); ela foi apagada só deste computador.");
    }
}

static void ShowQrCode(string challengeUrl)
{
    if (!Console.IsOutputRedirected)
    {
        Console.Clear();
    }

    Console.WriteLine("No app Steam do celular, abra o Steam Guard e escaneie o QR code (Ctrl+C para cancelar):");
    using var data = QRCodeGenerator.GenerateQrCode(challengeUrl, QRCodeGenerator.ECCLevel.L);
    Console.WriteLine(new AsciiQRCode(data).GetGraphicSmall());
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
