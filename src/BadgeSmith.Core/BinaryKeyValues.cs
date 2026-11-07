using System.Text;

namespace BadgeSmith.Core;

public sealed class KeyValuesNode
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, object>> Values => _values;

    public KeyValuesNode? Child(string name) => _values.GetValueOrDefault(name) as KeyValuesNode;

    public IEnumerable<KeyValuesNode> Children => _values.Values.OfType<KeyValuesNode>();

    public string? String(string name) => _values.GetValueOrDefault(name) switch
    {
        string s => s,
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    public long? Number(string name) => _values.GetValueOrDefault(name) switch
    {
        int i => i,
        long l => l,
        ulong u => (long)u,
        string s when long.TryParse(s, out var parsed) => parsed,
        _ => null,
    };

    internal void Set(string name, object value) => _values[name] = value;
}

public static class BinaryKeyValues
{
    private enum Type : byte
    {
        Node = 0,
        String = 1,
        Int32 = 2,
        Float = 3,
        Pointer = 4,
        WideString = 5,
        Color = 6,
        UInt64 = 7,
        End = 8,
        Int64 = 10,
    }

    public static KeyValuesNode Read(byte[] data)
    {
        var position = 0;
        return ReadNode(data, ref position);
    }

    private static KeyValuesNode ReadNode(byte[] data, ref int position)
    {
        var node = new KeyValuesNode();
        while (true)
        {
            var type = (Type)data[position++];
            if (type == Type.End)
            {
                return node;
            }

            var name = ReadString(data, ref position);
            object value = type switch
            {
                Type.Node => ReadNode(data, ref position),
                Type.String => ReadString(data, ref position),
                Type.Int32 or Type.Pointer or Type.Color => ReadInt32(data, ref position),
                Type.Float => ReadFloat(data, ref position),
                Type.UInt64 => (ulong)ReadInt64(data, ref position),
                Type.Int64 => ReadInt64(data, ref position),
                _ => throw new InvalidDataException($"Tipo {(byte)type} não suportado no KeyValues binário."),
            };
            node.Set(name, value);
        }
    }

    private static string ReadString(byte[] data, ref int position)
    {
        var end = Array.IndexOf(data, (byte)0, position);
        if (end < 0)
        {
            throw new InvalidDataException("Texto sem terminador no KeyValues binário.");
        }

        var value = Encoding.UTF8.GetString(data, position, end - position);
        position = end + 1;
        return value;
    }

    private static int ReadInt32(byte[] data, ref int position)
    {
        var value = BitConverter.ToInt32(data, position);
        position += 4;
        return value;
    }

    private static float ReadFloat(byte[] data, ref int position)
    {
        var value = BitConverter.ToSingle(data, position);
        position += 4;
        return value;
    }

    private static long ReadInt64(byte[] data, ref int position)
    {
        var value = BitConverter.ToInt64(data, position);
        position += 8;
        return value;
    }
}
