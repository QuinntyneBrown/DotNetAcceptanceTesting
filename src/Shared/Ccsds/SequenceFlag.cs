namespace Shared.Ccsds;

public enum SequenceFlag : byte
{
    ContinuationSegment = 0b00,
    FirstSegment = 0b01,
    LastSegment = 0b10,
    Unsegmented = 0b11
}
