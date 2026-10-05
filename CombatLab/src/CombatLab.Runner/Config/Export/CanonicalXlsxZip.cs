using System.Buffers.Binary;

namespace CombatLab.Runner.Config.Export;

internal static class CanonicalXlsxZip
{
    /// <summary>
    /// ZipArchive writes the host OS into "version made by" independently of ExternalAttributes.
    /// Normalize generated non-ZIP64, comment-free packages to DOS/spec20 with no file permissions.
    /// Parts/CRC/local headers are unchanged. This operates only on our newly generated package.
    /// </summary>
    internal static void Normalize(byte[] bytes)
    {
        if (bytes.Length < 22) throw new InvalidDataException("Incomplete generated ZIP.");
        var end = bytes.Length - 22;
        if (Read32(bytes, end) != 0x06054b50 || Read16(bytes, end + 20) != 0 ||
            Read16(bytes, end + 4) != 0 || Read16(bytes, end + 6) != 0 || Read16(bytes, end + 8) != Read16(bytes, end + 10))
            throw new InvalidDataException("Unexpected ZIP end record.");
        var count = Read16(bytes, end + 10);
        var directorySize = Read32(bytes, end + 12);
        var offset = checked((int)Read32(bytes, end + 16));
        if (count == ushort.MaxValue || (long)offset + directorySize != end)
            throw new InvalidDataException("Unexpected ZIP64/directory layout.");
        for (var entry = 0; entry < count; entry++)
        {
            if (offset < 0 || offset > end - 46 || Read32(bytes, offset) != 0x02014b50)
                throw new InvalidDataException("Invalid central directory entry.");
            var next = checked(offset + 46 + Read16(bytes, offset + 28) + Read16(bytes, offset + 30) + Read16(bytes, offset + 32));
            if (next > end) throw new InvalidDataException("Central directory entry exceeds package.");
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 4, 2), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 38, 4), 0);
            offset = next;
        }
        if (offset != end) throw new InvalidDataException("Unexpected trailing directory bytes.");
    }
    private static ushort Read16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}
