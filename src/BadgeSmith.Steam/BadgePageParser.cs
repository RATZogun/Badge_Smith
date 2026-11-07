using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using BadgeSmith.Core;

namespace BadgeSmith.Steam;

public sealed record BadgePage(IReadOnlyList<Badge> Badges, int PageCount, bool IsSignedIn);

public static partial class BadgePageParser
{
    public static BadgePage Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var isSignedIn = document.QuerySelector(".badges_sheet, .badge_row, .profile_xp_block") is not null;

        var badges = new List<Badge>();
        foreach (var row in document.QuerySelectorAll("div.badge_row"))
        {
            var href = row.QuerySelector("a.badge_row_overlay")?.GetAttribute("href") ?? "";
            var appMatch = GameCardsLink().Match(href);
            if (!appMatch.Success || href.Contains("border=1", StringComparison.Ordinal))
            {
                continue;
            }

            var dropsText = row.QuerySelector(".progress_info_bold")?.TextContent ?? "";
            var dropsMatch = DropsRemaining().Match(dropsText);
            if (!dropsMatch.Success)
            {
                continue;
            }

            var title = row.QuerySelector(".badge_title")?.ChildNodes.FirstOrDefault()?.TextContent.Trim() ?? "";
            var hoursText = row.QuerySelector(".badge_title_stats_playtime")?.TextContent ?? "";
            var hoursMatch = HoursOnRecord().Match(hoursText);
            var hours = hoursMatch.Success
                ? double.Parse(hoursMatch.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture)
                : 0;

            badges.Add(new Badge(
                uint.Parse(appMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                title,
                int.Parse(dropsMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                hours));
        }

        var pageCount = document.QuerySelectorAll(".pagelink")
            .Select(a => int.TryParse(a.TextContent.Trim(), out var n) ? n : 1)
            .DefaultIfEmpty(1)
            .Max();

        return new BadgePage(badges, pageCount, isSignedIn);
    }

    [GeneratedRegex(@"/gamecards/(\d+)")]
    private static partial Regex GameCardsLink();

    [GeneratedRegex(@"(\d+) card drops? remaining")]
    private static partial Regex DropsRemaining();

    [GeneratedRegex(@"([\d,]+(?:\.\d+)?) hrs on record")]
    private static partial Regex HoursOnRecord();
}
