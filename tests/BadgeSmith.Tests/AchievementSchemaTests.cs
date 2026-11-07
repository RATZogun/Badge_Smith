using System.Text;
using BadgeSmith.Core;

namespace BadgeSmith.Tests;

public class AchievementSchemaTests
{
    [Fact]
    public void ReadsAchievementsInOrderWithLocalizedTexts()
    {
        var schema = Node("979070",
            Node("stats",
                Node("1",
                    Str("type", "ACHIEVEMENTS"),
                    Node("bits",
                        Node("1", Achievement("ACH_SECOND", "Second", "Segunda", permission: 0, hidden: 1)),
                        Node("0", Achievement("ACH_FIRST", "First", null, permission: 2, hidden: 0)))),
                Node("2", Str("type", "INT"), Str("name", "DEATHS"))));

        var achievements = AchievementSchema.Read(Document(schema), "brazilian");

        Assert.Collection(
            achievements,
            a =>
            {
                Assert.Equal("ACH_FIRST", a.ApiName);
                Assert.Equal("First", a.Name);
                Assert.True(a.Protected);
                Assert.False(a.Hidden);
                Assert.Equal("ACH_FIRST.jpg", a.Icon);
            },
            a =>
            {
                Assert.Equal("ACH_SECOND", a.ApiName);
                Assert.Equal("Segunda", a.Name);
                Assert.Equal("Desc ACH_SECOND", a.Description);
                Assert.False(a.Protected);
                Assert.True(a.Hidden);
            });
    }

    [Fact]
    public void ReturnsEmptyWhenThereAreNoStats()
    {
        Assert.Empty(AchievementSchema.Read(Document(Node("1", Str("gamename", "x")))));
    }

    private static byte[] Achievement(string api, string english, string? brazilian, int permission, int hidden)
    {
        var names = brazilian is null ? Concat(Str("english", english)) : Concat(Str("english", english), Str("brazilian", brazilian));
        return Concat(
            Str("name", api),
            Node("display",
                Node("name", names),
                Node("desc", Str("english", $"Desc {api}")),
                Int("hidden", hidden),
                Str("icon", $"{api}.jpg"),
                Str("icon_gray", $"{api}_gray.jpg")),
            Int("permission", permission));
    }

    private static byte[] Document(byte[] root) => Concat(root, [8]);

    private static byte[] Node(string name, params byte[][] children) =>
        Concat([[0], Text(name), .. children, [8]]);

    private static byte[] Str(string name, string value) => Concat([1], Text(name), Text(value));

    private static byte[] Int(string name, int value) => Concat([2], Text(name), BitConverter.GetBytes(value));

    private static byte[] Text(string value) => Concat(Encoding.UTF8.GetBytes(value), [0]);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
