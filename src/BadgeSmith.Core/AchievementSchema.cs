namespace BadgeSmith.Core;

public sealed record AchievementDefinition(
    string ApiName,
    string Name,
    string Description,
    bool Hidden,
    bool Protected,
    string? Icon,
    string? IconGray);

public static class AchievementSchema
{
    private const int ProtectedPermission = 2;

    public static IReadOnlyList<AchievementDefinition> Read(byte[] data, string language = "english")
    {
        var root = BinaryKeyValues.Read(data);
        var app = root.Children.FirstOrDefault();
        var stats = app?.Child("stats");
        if (stats is null)
        {
            return [];
        }

        var achievements = new List<AchievementDefinition>();
        foreach (var stat in OrderedChildren(stats))
        {
            var type = stat.String("type") ?? stat.String("type_int");
            if (type is not ("ACHIEVEMENTS" or "GROUPACHIEVEMENTS" or "4" or "5"))
            {
                continue;
            }

            var bits = stat.Child("bits");
            if (bits is null)
            {
                continue;
            }

            foreach (var bit in OrderedChildren(bits))
            {
                var apiName = bit.String("name");
                if (string.IsNullOrEmpty(apiName))
                {
                    continue;
                }

                var display = bit.Child("display");
                achievements.Add(new AchievementDefinition(
                    apiName,
                    Localized(display?.Child("name"), language) ?? apiName,
                    Localized(display?.Child("desc"), language) ?? "",
                    display?.Number("hidden") == 1,
                    (bit.Number("permission") ?? 0) >= ProtectedPermission,
                    display?.String("icon"),
                    display?.String("icon_gray")));
            }
        }

        return achievements;
    }

    private static IEnumerable<KeyValuesNode> OrderedChildren(KeyValuesNode node) => node.Values
        .Where(v => v.Value is KeyValuesNode)
        .OrderBy(v => int.TryParse(v.Key, out var index) ? index : int.MaxValue)
        .Select(v => (KeyValuesNode)v.Value);

    private static string? Localized(KeyValuesNode? texts, string language) =>
        texts is null ? null : texts.String(language) ?? texts.String("english");
}
