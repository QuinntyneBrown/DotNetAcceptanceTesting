namespace Shared.Ccsds;

/// <summary>
/// Parses a CCSDS Space Packet (CCSDS 133.0-B-2) from a raw byte array.
/// All multi-byte fields are big-endian per the standard.
///
/// Primary Header layout (6 bytes):
///   Bytes 0-1: Version(3) | PacketType(1) | SecHdrFlag(1) | APID(11)
///   Bytes 2-3: SequenceFlags(2) | SequenceCount(14)
///   Bytes 4-5: DataLength(16) — value is (data field byte count) - 1
/// </summary>
public class CcsdsSpacePacket
{
    public const int PrimaryHeaderLength = 6;
    public const int MinimumPacketLength = 7;
    public const ushort IdleApid = 0x07FF;

    public byte VersionNumber { get; }
    public bool IsCommand { get; }
    public bool HasSecondaryHeader { get; }
    public ushort Apid { get; }
    public SequenceFlag SequenceFlags { get; }
    public ushort SequenceCount { get; }
    public ushort DataLengthField { get; }
    public int DataFieldLength => DataLengthField + 1;
    public int TotalPacketLength => PrimaryHeaderLength + DataFieldLength;
    public bool IsIdle => Apid == IdleApid;

    private readonly byte[] _raw;
    private readonly int _offset;

    private CcsdsSpacePacket(byte[] raw, int offset,
        byte version, bool isCommand, bool hasSecondaryHeader,
        ushort apid, SequenceFlag sequenceFlags, ushort sequenceCount,
        ushort dataLength)
    {
        _raw = raw;
        _offset = offset;
        VersionNumber = version;
        IsCommand = isCommand;
        HasSecondaryHeader = hasSecondaryHeader;
        Apid = apid;
        SequenceFlags = sequenceFlags;
        SequenceCount = sequenceCount;
        DataLengthField = dataLength;
    }

    public static CcsdsSpacePacket Parse(byte[] buffer, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length - offset < MinimumPacketLength)
            throw new ArgumentException(
                $"Buffer too short. Need at least {MinimumPacketLength} bytes, got {buffer.Length - offset}.");

        // Word 1: Packet Identification (bytes 0-1, big-endian)
        ushort word1 = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

        var version = (byte)((word1 >> 13) & 0x07);
        var isCommand = ((word1 >> 12) & 0x01) == 1;
        var hasSecondaryHeader = ((word1 >> 11) & 0x01) == 1;
        var apid = (ushort)(word1 & 0x07FF);

        // Word 2: Sequence Control (bytes 2-3, big-endian)
        ushort word2 = (ushort)((buffer[offset + 2] << 8) | buffer[offset + 3]);

        var sequenceFlags = (SequenceFlag)((word2 >> 14) & 0x03);
        var sequenceCount = (ushort)(word2 & 0x3FFF);

        // Word 3: Data Length (bytes 4-5, big-endian)
        ushort dataLength = (ushort)((buffer[offset + 4] << 8) | buffer[offset + 5]);

        int totalRequired = offset + PrimaryHeaderLength + dataLength + 1;
        if (buffer.Length < totalRequired)
            throw new ArgumentException(
                $"Buffer length {buffer.Length} is less than declared packet size {totalRequired} " +
                $"(header={PrimaryHeaderLength}, dataField={dataLength + 1}).");

        return new CcsdsSpacePacket(buffer, offset,
            version, isCommand, hasSecondaryHeader,
            apid, sequenceFlags, sequenceCount, dataLength);
    }

    public ReadOnlySpan<byte> GetDataField()
        => _raw.AsSpan(_offset + PrimaryHeaderLength, DataFieldLength);

    public byte[] GetDataFieldArray()
        => _raw.AsSpan(_offset + PrimaryHeaderLength, DataFieldLength).ToArray();

    public static void WritePrimaryHeader(Span<byte> destination,
        byte version, bool isCommand, bool hasSecondaryHeader,
        ushort apid, SequenceFlag sequenceFlags, ushort sequenceCount,
        ushort dataLengthField)
    {
        if (destination.Length < PrimaryHeaderLength)
            throw new ArgumentException($"Destination must be at least {PrimaryHeaderLength} bytes.");

        ushort word1 = (ushort)(
            ((version & 0x07) << 13) |
            ((isCommand ? 1 : 0) << 12) |
            ((hasSecondaryHeader ? 1 : 0) << 11) |
            (apid & 0x07FF));

        destination[0] = (byte)(word1 >> 8);
        destination[1] = (byte)(word1 & 0xFF);

        ushort word2 = (ushort)(
            (((byte)sequenceFlags & 0x03) << 14) |
            (sequenceCount & 0x3FFF));

        destination[2] = (byte)(word2 >> 8);
        destination[3] = (byte)(word2 & 0xFF);

        destination[4] = (byte)(dataLengthField >> 8);
        destination[5] = (byte)(dataLengthField & 0xFF);
    }

    public static byte[] BuildPacket(byte version, bool isCommand, bool hasSecondaryHeader,
        ushort apid, SequenceFlag sequenceFlags, ushort sequenceCount,
        byte[] dataField)
    {
        ArgumentNullException.ThrowIfNull(dataField);
        if (dataField.Length == 0)
            throw new ArgumentException("Data field must contain at least 1 byte.");

        var packet = new byte[PrimaryHeaderLength + dataField.Length];
        WritePrimaryHeader(packet,
            version, isCommand, hasSecondaryHeader,
            apid, sequenceFlags, sequenceCount,
            (ushort)(dataField.Length - 1));

        dataField.CopyTo(packet.AsSpan(PrimaryHeaderLength));
        return packet;
    }
}
