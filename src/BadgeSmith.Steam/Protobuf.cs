using System.Buffers.Binary;
using System.Text;

namespace BadgeSmith.Steam;

public sealed class ProtoWriter
{
    private readonly MemoryStream _buffer = new();

    public ProtoWriter Varint(int field, ulong value)
    {
        WriteTag(field, 0);
        WriteVarint(value);
        return this;
    }

    public ProtoWriter Bool(int field, bool value) => Varint(field, value ? 1UL : 0UL);

    public ProtoWriter Fixed64(int field, ulong value)
    {
        WriteTag(field, 1);
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _buffer.Write(bytes);
        return this;
    }

    public ProtoWriter String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));

    public ProtoWriter Message(int field, ProtoWriter message) => Bytes(field, message.ToArray());

    public ProtoWriter Bytes(int field, byte[] value)
    {
        WriteTag(field, 2);
        WriteVarint((ulong)value.Length);
        _buffer.Write(value);
        return this;
    }

    public byte[] ToArray() => _buffer.ToArray();

    private void WriteTag(int field, int wireType) => WriteVarint(((ulong)field << 3) | (uint)wireType);

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _buffer.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        _buffer.WriteByte((byte)value);
    }
}

public sealed class ProtoMessage
{
    private readonly Dictionary<int, List<object>> _fields = [];

    private ProtoMessage()
    {
    }

    public static ProtoMessage Parse(ReadOnlySpan<byte> data)
    {
        var message = new ProtoMessage();
        var position = 0;
        while (position < data.Length)
        {
            var tag = ReadVarint(data, ref position);
            var field = (int)(tag >> 3);
            object value = (tag & 7) switch
            {
                0 => ReadVarint(data, ref position),
                1 => ReadFixed(data, ref position, 8),
                2 => ReadLengthDelimited(data, ref position),
                5 => ReadFixed(data, ref position, 4),
                var wireType => throw new FormatException($"Tipo de campo protobuf não suportado: {wireType}."),
            };

            if (!message._fields.TryGetValue(field, out var values))
            {
                message._fields[field] = values = [];
            }

            values.Add(value);
        }

        return message;
    }

    public ulong Varint(int field) => First(field) is ulong value ? value : 0;

    public bool Bool(int field) => Varint(field) != 0;

    public float Float(int field) => First(field) is ulong bits ? BitConverter.Int32BitsToSingle((int)bits) : 0;

    public byte[] Bytes(int field) => First(field) as byte[] ?? [];

    public string String(int field) => Encoding.UTF8.GetString(Bytes(field));

    public IEnumerable<ProtoMessage> Messages(int field) =>
        _fields.TryGetValue(field, out var values) ? values.OfType<byte[]>().Select(v => Parse(v)) : [];

    private object? First(int field) => _fields.TryGetValue(field, out var values) ? values[0] : null;

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= data.Length)
            {
                throw new FormatException("Mensagem protobuf truncada.");
            }

            var b = data[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
            {
                return value;
            }
        }

        throw new FormatException("Varint protobuf longo demais.");
    }

    private static ulong ReadFixed(ReadOnlySpan<byte> data, ref int position, int size)
    {
        if (position + size > data.Length)
        {
            throw new FormatException("Mensagem protobuf truncada.");
        }

        ulong value = 0;
        for (var i = size - 1; i >= 0; i--)
        {
            value = (value << 8) | data[position + i];
        }

        position += size;
        return value;
    }

    private static byte[] ReadLengthDelimited(ReadOnlySpan<byte> data, ref int position)
    {
        var length = ReadVarint(data, ref position);
        if (length > (ulong)(data.Length - position))
        {
            throw new FormatException("Mensagem protobuf truncada.");
        }

        var value = data.Slice(position, (int)length).ToArray();
        position += (int)length;
        return value;
    }
}
