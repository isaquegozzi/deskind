using System.Buffers.Binary;

namespace DeskInk.Core.Transport;

public static class FramedStream
{
    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, checked((uint)frame.Length));
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<byte[]?> ReadAsync(
        Stream stream,
        int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(uint)];
        var prefixBytes = await ReadExactlyOrEofAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        if (prefixBytes == 0) return null;
        var frameBytes = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (frameBytes == 0 || frameBytes > maximumFrameBytes)
        {
            throw new Protocol.ProtocolException($"Stream frame length outside limits: {frameBytes}");
        }
        var frame = new byte[frameBytes];
        await stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
        return frame;
    }

    private static async ValueTask<int> ReadExactlyOrEofAsync(
        Stream stream,
        Memory<byte> target,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < target.Length)
        {
            var read = await stream.ReadAsync(target[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (total == 0) return 0;
                throw new EndOfStreamException("Stream ended inside frame prefix");
            }
            total += read;
        }
        return total;
    }
}
