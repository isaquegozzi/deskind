using System.Buffers.Binary;

namespace DeskInk.Core.Protocol;

public static class ProtocolV1Codec
{
    public const int HeaderBytes = 32;
    public const int InputPrefixBytes = 4;
    public const int PenSampleBytes = 24;
    public const byte InputBatchMessageType = 0x10;

    private const ushort KnownSampleFlags = 0x00ff;

    public static byte[] EncodeInputBatch(InputBatchFrame frame)
    {
        ValidateFrame(frame);
        var frameBytes = HeaderBytes + InputPrefixBytes + frame.Samples.Count * PenSampleBytes;
        var bytes = new byte[frameBytes];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[0..4], DeskInkConstants.ProtocolMagic);
        span[4] = DeskInkConstants.ProtocolMajor;
        span[5] = DeskInkConstants.ProtocolMinor;
        span[6] = InputBatchMessageType;
        span[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..10], HeaderBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..12], checked((ushort)frameBytes));
        BinaryPrimitives.WriteUInt64LittleEndian(span[12..20], frame.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..24], frame.Sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(span[24..32], frame.BaseMonotonicTimeUs);

        span[32] = checked((byte)frame.Samples.Count);
        span[33] = (byte)frame.ToolKind;
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..36], frame.PointerId);

        var offset = HeaderBytes + InputPrefixBytes;
        foreach (var sample in frame.Samples)
        {
            WriteSample(span.Slice(offset, PenSampleBytes), sample);
            offset += PenSampleBytes;
        }
        return bytes;
    }

    public static InputBatchFrame DecodeInputBatch(ReadOnlySpan<byte> bytes)
    {
        var minimum = HeaderBytes + InputPrefixBytes + PenSampleBytes;
        if (bytes.Length < minimum || bytes.Length > DeskInkConstants.MaximumInputFrameBytes)
        {
            throw new ProtocolException($"Input frame length is outside limits: {bytes.Length}");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[0..4]) != DeskInkConstants.ProtocolMagic)
        {
            throw new ProtocolException("Bad protocol magic");
        }
        if (bytes[4] != DeskInkConstants.ProtocolMajor || bytes[5] != DeskInkConstants.ProtocolMinor)
        {
            throw new ProtocolException($"Unsupported protocol version: {bytes[4]}.{bytes[5]}");
        }
        if (bytes[6] != InputBatchMessageType) throw new ProtocolException("Not an INPUT_BATCH");
        if (bytes[7] != 0) throw new ProtocolException("Unknown header flags");
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10]) != HeaderBytes)
        {
            throw new ProtocolException("Unexpected header size");
        }
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..12]) != bytes.Length)
        {
            throw new ProtocolException("Frame length mismatch");
        }

        var sampleCount = bytes[32];
        if (sampleCount is < 1 or > DeskInkConstants.MaximumSamplesPerBatch)
        {
            throw new ProtocolException($"Invalid sample count: {sampleCount}");
        }
        var expectedBytes = HeaderBytes + InputPrefixBytes + sampleCount * PenSampleBytes;
        if (expectedBytes != bytes.Length) throw new ProtocolException("Sample count/length mismatch");

        var toolKind = (ToolKind)bytes[33];
        if (!Enum.IsDefined(toolKind)) throw new ProtocolException($"Unknown tool kind: {bytes[33]}");

        var samples = new PenSample[sampleCount];
        var offset = HeaderBytes + InputPrefixBytes;
        for (var index = 0; index < sampleCount; index++)
        {
            samples[index] = ReadSample(bytes.Slice(offset, PenSampleBytes));
            offset += PenSampleBytes;
        }

        return new InputBatchFrame(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[12..20]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..32]),
            toolKind,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[34..36]),
            samples);
    }

    private static void WriteSample(Span<byte> bytes, PenSample sample)
    {
        ValidateSample(sample);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[0..4], sample.DeltaTimeUs);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..8], sample.StateGeneration);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..10], sample.XNormalized);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[10..12], sample.YNormalized);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..14], sample.PressureNormalized);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[14..16], sample.DistanceNormalized);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[16..18], sample.TiltCentidegrees);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[18..20], sample.OrientationCentidegrees);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[20..22], sample.Buttons);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[22..24], (ushort)sample.State);
    }

    private static PenSample ReadSample(ReadOnlySpan<byte> bytes)
    {
        var sample = new PenSample(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[0..4]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..12]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..14]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..16]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..18]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[18..20]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..22]),
            (PenSampleState)BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..24]));
        ValidateSample(sample);
        return sample;
    }

    private static void ValidateFrame(InputBatchFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Samples.Count is < 1 or > DeskInkConstants.MaximumSamplesPerBatch)
        {
            throw new ProtocolException($"Invalid sample count: {frame.Samples.Count}");
        }
        if (!Enum.IsDefined(frame.ToolKind)) throw new ProtocolException("Unknown tool kind");
        var frameBytes = HeaderBytes + InputPrefixBytes + frame.Samples.Count * PenSampleBytes;
        if (frameBytes > DeskInkConstants.MaximumInputFrameBytes)
        {
            throw new ProtocolException("Input frame exceeds limit");
        }
    }

    private static void ValidateSample(PenSample sample)
    {
        var rawFlags = (ushort)sample.State;
        if ((rawFlags & ~KnownSampleFlags) != 0) throw new ProtocolException("Unknown sample flags");
        if (sample.State.HasFlag(PenSampleState.Contact) && !sample.State.HasFlag(PenSampleState.InRange))
        {
            throw new ProtocolException("CONTACT requires IN_RANGE");
        }
        if (sample.TiltCentidegrees > 9000) throw new ProtocolException("Tilt outside range");
        if (sample.OrientationCentidegrees is < -18000 or > 18000)
        {
            throw new ProtocolException("Orientation outside range");
        }
    }
}
