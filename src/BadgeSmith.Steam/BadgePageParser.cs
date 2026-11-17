using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public sealed record BadgePage(
    IReadOnlyList<Badge> Badges,
    IReadOnlyList<Badge> WithoutDropInfo,
    int PageCount,
    ulong? SignedInSteamId,
    bool? AccountLimited = null);

public static partial class BadgePageParser
{
    public static BadgePage Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);

        var badges = new List<Badge>();
        var withoutDropInfo = new List<Badge>();
        foreach (var row in document.QuerySelectorAll("div.badge_row"))
        {
            var href = row.QuerySelector("a.badge_row_overlay")?.GetAttribute("href") ?? "";
            var appMatch = GameCardsLink().Match(href);
            if (!appMatch.Success || href.Contains("border=1", StringComparison.Ordinal))
            {
                continue;
            }

            var appId = uint.Parse(appMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var title = row.QuerySelector(".badge_title")?.ChildNodes.FirstOrDefault()?.TextContent.Trim() ?? "";
            var hoursText = row.QuerySelector(".badge_title_stats_playtime")?.TextContent;
            var hours = ParseHours(hoursText ?? "");

            var dropsText = row.QuerySelector(".progress_info_bold")?.TextContent;
            var drops = ParseRemainingDrops(dropsText ?? "");
            if (drops is { } remaining)
            {
                badges.Add(new Badge(appId, title, remaining, hours));
            }
            else if (dropsText is null && hoursText is not null)
            {
                withoutDropInfo.Add(new Badge(appId, title, 0, hours));
            }
        }

        var pageCount = document.QuerySelectorAll(".pagelink")
            .Select(a => int.TryParse(a.TextContent.Trim(), out var n) ? n : 1)
            .DefaultIfEmpty(1)
            .Max();

        return new BadgePage(badges, withoutDropInfo, pageCount, ParseSignedInSteamId(html), ParseAccountLimited(document.QuerySelector("[data-userinfo]")?.GetAttribute("data-userinfo")));
    }

    public static int? ParseRemainingDrops(string text)
    {
        if (NoDropsRemaining().IsMatch(text))
        {
            return 0;
        }

        var match = DropsRemaining().Match(text);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    public static bool? ParseAccountLimited(string? userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(userInfo);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("is_limited", out var limited)
                && limited.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? limited.GetBoolean()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ulong? ParseSignedInSteamId(string html)
    {
        var match = SignedInSteamId().Match(html);
        return match.Success ? ulong.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static double ParseHours(string text)
    {
        var match = HoursOnRecord().Match(text);
        return match.Success ? double.Parse(match.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : 0;
    }

    [GeneratedRegex(@"/gamecards/(\d+)")]
    private static partial Regex GameCardsLink();

    [GeneratedRegex(@"(\d+) card drops? remaining")]
    private static partial Regex DropsRemaining();

    [GeneratedRegex(@"No card drops remaining")]
    private static partial Regex NoDropsRemaining();

    [GeneratedRegex(@"([\d,]+(?:\.\d+)?) hrs on record")]
    private static partial Regex HoursOnRecord();

    [GeneratedRegex(@"g_steamID\s*=\s*""(\d+)""")]
    private static partial Regex SignedInSteamId();
}
