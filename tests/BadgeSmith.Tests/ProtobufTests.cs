using BadgeSmith.Steam;

namespace BadgeSmith.Tests;

public class ProtobufTests
{
    [Fact]
    public void ReadsBackWhatItWrites()
    {
        var bytes = new ProtoWriter()
            .Varint(1, 76561198000000000)
            .String(2, "Badge Smith ✓")
            .Fixed64(3, 0x0102030405060708)
            .Bool(4, true)
            .Message(5, new ProtoWriter().Varint(1, 3))
            .Message(5, new ProtoWriter().Varint(1, 4))
            .Bytes(6, [1, 2, 3])
            .ToArray();

        var message = ProtoMessage.Parse(bytes);

        Assert.Equal(76561198000000000UL, message.Varint(1));
        Assert.Equal("Badge Smith ✓", message.String(2));
        Assert.Equal(0x0102030405060708UL, message.Varint(3));
        Assert.True(message.Bool(4));
        Assert.Equal([3UL, 4UL], message.Messages(5).Select(m => m.Varint(1)));
        Assert.Equal([1, 2, 3], message.Bytes(6));
        Assert.Equal("", message.String(99));
    }

    [Fact]
    public void ReadsAFloatField()
    {
        byte[] bytes = [0x25, 0x00, 0x00, 0xA0, 0x40];

        Assert.Equal(5f, ProtoMessage.Parse(bytes).Float(4));
    }

    [Fact]
    public void RejectsATruncatedMessage()
    {
        var bytes = new ProtoWriter().String(1, "abc").ToArray()[..^1];

        Assert.Throws<FormatException>(() => ProtoMessage.Parse(bytes));
    }
}
