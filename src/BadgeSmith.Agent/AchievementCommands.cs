using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BadgeSmith.Core;

namespace BadgeSmith.Agent;

internal static class AchievementCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static int Run(SteamClient client, uint appId, string action, IReadOnlyList<string> apiNames, string language)
    {
        if (action is "unlock" or "lock" && apiNames.Count == 0)
        {
            Console.Error.WriteLine($"ERRO informe ao menos uma conquista para {action}.");
            return (int)AgentExitCode.InvalidArguments;
        }

        var stats = client.GetUserStats();
        if (stats is null)
        {
            Console.Error.WriteLine($"ERRO versão da interface SteamUserStats não suportada por esta Steam ({client.Version}).");
            return (int)AgentExitCode.UnsupportedSteam;
        }

        if (!stats.Load(appId))
        {
            Console.Error.WriteLine($"ERRO a Steam não entregou as conquistas do jogo {appId}.");
            return (int)AgentExitCode.StatsUnavailable;
        }

        var root = SteamInstall.FindRoot();
        var schemaPath = root is null ? null : SteamInstall.SchemaPath(root, appId);
        if (schemaPath is null)
        {
            Console.Error.WriteLine($"ERRO o schema de conquistas do jogo {appId} não está no cache da Steam.");
            return (int)AgentExitCode.SchemaUnavailable;
        }

        var schema = AchievementSchema.Read(File.ReadAllBytes(schemaPath), language);
        return action == "list" ? List(stats, schema) : Change(stats, appId, schema, apiNames, action == "unlock");
    }

    private static int List(SteamUserStats stats, IReadOnlyList<AchievementDefinition> schema)
    {
        foreach (var definition in schema)
        {
            var state = stats.Get(definition.ApiName);
            Write(new
            {
                api = definition.ApiName,
                definition.Name,
                definition.Description,
                definition.Hidden,
                definition.Protected,
                achieved = state?.Achieved,
                unlockTime = state is { Achieved: true, UnlockTime: > 0 } s ? DateTimeOffset.FromUnixTimeSeconds(s.UnlockTime) : (DateTimeOffset?)null,
            });
        }

        return (int)AgentExitCode.Stopped;
    }

    private static int Change(SteamUserStats stats, uint appId, IReadOnlyList<AchievementDefinition> schema, IReadOnlyList<string> apiNames, bool unlock)
    {
        var byName = schema.ToDictionary(a => a.ApiName, StringComparer.Ordinal);
        var results = new List<(string Api, string? Error)>();
        foreach (var apiName in apiNames)
        {
            string? error = null;
            if (!byName.TryGetValue(apiName, out var definition))
            {
                error = "conquista não existe neste jogo";
            }
            else if (definition.Protected)
            {
                error = "conquista protegida: só o jogo pode alterá-la";
            }
            else if (!stats.Set(apiName, unlock))
            {
                error = "a Steam recusou a alteração";
            }

            results.Add((apiName, error));
        }

        var stored = !results.Any(r => r.Error is null) || stats.Store(appId);
        foreach (var (api, error) in results)
        {
            var finalError = error ?? (stored ? null : "a Steam não confirmou a gravação");
            Write(new { api, ok = finalError is null, error = finalError });
        }

        if (!stored)
        {
            Console.Error.WriteLine("ERRO a Steam não confirmou a gravação das conquistas.");
            return (int)AgentExitCode.StoreFailed;
        }

        return (int)AgentExitCode.Stopped;
    }

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
}
