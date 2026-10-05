namespace DeskInk.Core.Protocol;

public enum ToolKind : byte
{
    Unknown = 0,
    Finger = 1,
    Stylus = 2,
    Eraser = 3,
    Mouse = 4,
}

[Flags]
public enum PenSampleState : ushort
{
    None = 0,
    InRange = 0x0001,
    Contact = 0x0002,
    PressureValid = 0x0004,
    DistanceValid = 0x0008,
    TiltValid = 0x0010,
    OrientationValid = 0x0020,
    Historical = 0x0040,
    Canceled = 0x0080,
}

public readonly record struct PenSample(
    uint DeltaTimeUs,
    uint StateGeneration,
    ushort XNormalized,
    ushort YNormalized,
    ushort PressureNormalized,
    ushort DistanceNormalized,
    ushort TiltCentidegrees,
    short OrientationCentidegrees,
    ushort Buttons,
    PenSampleState State);

public sealed record InputBatchFrame(
    ulong SessionId,
    uint Sequence,
    ulong BaseMonotonicTimeUs,
    ToolKind ToolKind,
    ushort PointerId,
    IReadOnlyList<PenSample> Samples);

public sealed class ProtocolException(string message) : Exception(message);
