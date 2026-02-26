using Shared.Ccsds;

namespace Shared.Tests.Ccsds;

public class CcsdsSpacePacketTests
{
    [Fact]
    public void Parse_telemetry_packet_extracts_correct_header_fields()
    {
        // APID=208 (0x0D0), Telemetry, SecHdr=true, Unsegmented, SeqCount=25, DataLen=4
        // Word1: version=0, type=0(TM), secHdr=1, APID=0x0D0
        //   = (0 << 13) | (0 << 12) | (1 << 11) | 0x0D0 = 0x08D0
        // Word2: seqFlags=3(unseg), seqCount=25
        //   = (3 << 14) | 25 = 0xC019
        // Word3: dataLen=4 (5 bytes of data)
        //   = 0x0004
        byte[] raw = [0x08, 0xD0, 0xC0, 0x19, 0x00, 0x04, 0x01, 0x02, 0x03, 0x04, 0x05];

        var packet = CcsdsSpacePacket.Parse(raw);

        Assert.Equal(0, packet.VersionNumber);
        Assert.False(packet.IsCommand);
        Assert.True(packet.HasSecondaryHeader);
        Assert.Equal(208, packet.Apid);
        Assert.Equal(SequenceFlag.Unsegmented, packet.SequenceFlags);
        Assert.Equal(25, packet.SequenceCount);
        Assert.Equal(4, packet.DataLengthField);
        Assert.Equal(5, packet.DataFieldLength);
        Assert.Equal(11, packet.TotalPacketLength);
    }

    [Fact]
    public void Parse_telecommand_packet_sets_IsCommand_true()
    {
        // Word1: version=0, type=1(TC), secHdr=0, APID=0x001
        //   = (0 << 13) | (1 << 12) | (0 << 11) | 0x001 = 0x1001
        byte[] raw = [0x10, 0x01, 0xC0, 0x00, 0x00, 0x00, 0xFF];

        var packet = CcsdsSpacePacket.Parse(raw);

        Assert.True(packet.IsCommand);
        Assert.False(packet.HasSecondaryHeader);
        Assert.Equal(1, packet.Apid);
    }

    [Fact]
    public void Parse_idle_packet_sets_IsIdle_true()
    {
        // APID = 0x7FF (idle)
        // Word1: (0 << 13) | (0 << 12) | (0 << 11) | 0x7FF = 0x07FF
        byte[] raw = [0x07, 0xFF, 0xC0, 0x00, 0x00, 0x00, 0x00];

        var packet = CcsdsSpacePacket.Parse(raw);

        Assert.True(packet.IsIdle);
        Assert.Equal(CcsdsSpacePacket.IdleApid, packet.Apid);
    }

    [Fact]
    public void Parse_extracts_data_field_correctly()
    {
        byte[] dataField = [0xDE, 0xAD, 0xBE, 0xEF];
        var raw = CcsdsSpacePacket.BuildPacket(0, false, false, 100,
            SequenceFlag.Unsegmented, 0, dataField);

        var packet = CcsdsSpacePacket.Parse(raw);
        var extracted = packet.GetDataFieldArray();

        Assert.Equal(dataField, extracted);
    }

    [Fact]
    public void Parse_all_sequence_flags()
    {
        foreach (var flag in Enum.GetValues<SequenceFlag>())
        {
            var raw = CcsdsSpacePacket.BuildPacket(0, false, false, 1, flag, 0, [0x00]);
            var packet = CcsdsSpacePacket.Parse(raw);
            Assert.Equal(flag, packet.SequenceFlags);
        }
    }

    [Fact]
    public void Parse_maximum_APID()
    {
        // APID 2046 = 0x7FE (max non-idle)
        var raw = CcsdsSpacePacket.BuildPacket(0, false, false, 2046,
            SequenceFlag.Unsegmented, 0, [0x00]);

        var packet = CcsdsSpacePacket.Parse(raw);

        Assert.Equal(2046, packet.Apid);
        Assert.False(packet.IsIdle);
    }

    [Fact]
    public void Parse_maximum_sequence_count()
    {
        // Max = 0x3FFF = 16383
        var raw = CcsdsSpacePacket.BuildPacket(0, false, false, 1,
            SequenceFlag.Unsegmented, 16383, [0x00]);

        var packet = CcsdsSpacePacket.Parse(raw);

        Assert.Equal(16383, packet.SequenceCount);
    }

    [Fact]
    public void Parse_throws_on_buffer_too_short()
    {
        byte[] tooShort = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05]; // 6 bytes, need 7 minimum

        Assert.Throws<ArgumentException>(() => CcsdsSpacePacket.Parse(tooShort));
    }

    [Fact]
    public void Parse_throws_when_declared_length_exceeds_buffer()
    {
        // Declare 10 bytes of data (dataLength=9) but only provide 1
        byte[] raw = [0x00, 0x01, 0xC0, 0x00, 0x00, 0x09, 0xFF];

        Assert.Throws<ArgumentException>(() => CcsdsSpacePacket.Parse(raw));
    }

    [Fact]
    public void Parse_throws_on_null_buffer()
    {
        Assert.Throws<ArgumentNullException>(() => CcsdsSpacePacket.Parse(null!));
    }

    [Fact]
    public void BuildPacket_and_Parse_roundtrip()
    {
        byte[] dataField = [0x01, 0x02, 0x03, 0x04, 0x05];

        var built = CcsdsSpacePacket.BuildPacket(
            version: 0,
            isCommand: true,
            hasSecondaryHeader: true,
            apid: 512,
            sequenceFlags: SequenceFlag.FirstSegment,
            sequenceCount: 999,
            dataField: dataField);

        var parsed = CcsdsSpacePacket.Parse(built);

        Assert.Equal(0, parsed.VersionNumber);
        Assert.True(parsed.IsCommand);
        Assert.True(parsed.HasSecondaryHeader);
        Assert.Equal(512, parsed.Apid);
        Assert.Equal(SequenceFlag.FirstSegment, parsed.SequenceFlags);
        Assert.Equal(999, parsed.SequenceCount);
        Assert.Equal(dataField, parsed.GetDataFieldArray());
    }

    [Fact]
    public void BuildPacket_throws_on_empty_data_field()
    {
        Assert.Throws<ArgumentException>(() =>
            CcsdsSpacePacket.BuildPacket(0, false, false, 1, SequenceFlag.Unsegmented, 0, []));
    }

    [Fact]
    public void Parse_with_offset_skips_leading_bytes()
    {
        byte[] dataField = [0xAA, 0xBB];
        var packet = CcsdsSpacePacket.BuildPacket(0, false, false, 77,
            SequenceFlag.Unsegmented, 5, dataField);

        // Prepend 4 garbage bytes
        var padded = new byte[4 + packet.Length];
        padded[0] = 0xFF;
        padded[1] = 0xFF;
        padded[2] = 0xFF;
        padded[3] = 0xFF;
        packet.CopyTo(padded, 4);

        var parsed = CcsdsSpacePacket.Parse(padded, offset: 4);

        Assert.Equal(77, parsed.Apid);
        Assert.Equal(5, parsed.SequenceCount);
        Assert.Equal(dataField, parsed.GetDataFieldArray());
    }
}
