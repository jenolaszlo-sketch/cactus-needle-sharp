using System.Buffers.Binary;

namespace CactusNeedleSharp;

internal static class NeedleModelFormat
{
    private const uint Needle3Tag = 0x05e12a84;
    private const uint Needle2Tag = 0x05e12a83;
    private const int HeaderSize = 196;
    private const int RecordSize = 44;

    internal static bool Read(string path)
    {
        using var model = File.OpenRead(path);
        return Read(model, path);
    }

    internal static bool Read(Stream model, string path)
    {
        NeedleArtifactException Invalid(string detail) => new($"Invalid Needle 3 archive '{path}': {detail}.");
        Span<byte> header = stackalloc byte[HeaderSize];
        if (model.Length < sizeof(uint)) throw Invalid("missing archive tag");
        model.ReadExactly(header[..sizeof(uint)]);
        var tag = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (tag == Needle2Tag) throw Invalid("Needle 2 weights require the legacy runtime");
        if (tag != Needle3Tag) throw Invalid($"unknown archive tag 0x{tag:x8}");
        if (model.Length < HeaderSize) throw Invalid("incomplete archive header");
        model.ReadExactly(header[sizeof(uint)..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var codebook = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var directory = HeaderSize + codebook * 4L;
        var dataStart = directory + count * RecordSize;
        if (count == 0 || count > int.MaxValue / RecordSize || dataStart > model.Length)
            throw Invalid("invalid tensor directory");

        Span<byte> record = stackalloc byte[RecordSize];
        model.Position = directory;
        ulong previousEnd = (ulong)dataStart;
        for (var index = 0; index < count; index++)
        {
            model.ReadExactly(record);
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(record[20..]);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(record[28..]);
            if (record[0] is < 1 or > 4 || record[1] > 4 ||
                offset < previousEnd || offset > (ulong)model.Length || length > (ulong)model.Length - offset)
                throw Invalid($"tensor {index} is malformed or truncated");
            previousEnd = offset + length;
        }
        if (record[0] != 4 || previousEnd != (ulong)model.Length)
            throw Invalid("missing or incomplete tokenizer payload");

        Span<byte> codes = stackalloc byte[6];
        for (var heads = 1; heads <= 3; heads++)
        {
            var index = (long)count - 2 - 6 * heads;
            if (index < 0) break;
            model.Position = directory + index * RecordSize;
            model.ReadExactly(record);
            if (record[0] != 1 || record[1] != 1 ||
                BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) != heads ||
                BinaryPrimitives.ReadUInt64LittleEndian(record[28..]) != 2UL * (ulong)heads)
                continue;
            model.Position = (long)BinaryPrimitives.ReadUInt64LittleEndian(record[20..]);
            model.ReadExactly(codes[..(heads * 2)]);
            for (var head = 0; head < heads; head++)
                if ((float)BinaryPrimitives.ReadHalfLittleEndian(codes[(head * 2)..]) == 2f) return true;
            return false;
        }
        return false;
    }
}
