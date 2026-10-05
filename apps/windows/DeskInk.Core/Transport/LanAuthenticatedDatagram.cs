using System.Security.Cryptography;
using DeskInk.Core.Protocol;

namespace DeskInk.Core.Transport;

public static class LanAuthenticatedDatagram
{
    public const int SessionSecretBytes = 32;
    public const int AuthenticationTagBytes = 16;

    public static byte[] Encode(ReadOnlySpan<byte> logicalFrame, ReadOnlySpan<byte> sessionSecret)
    {
        ValidateSecret(sessionSecret);
        if (logicalFrame.Length < ProtocolV1Codec.HeaderBytes ||
            logicalFrame.Length > DeskInkConstants.MaximumInputFrameBytes)
            throw new ProtocolException("LAN logical frame length is invalid");

        var datagram = new byte[logicalFrame.Length + AuthenticationTagBytes];
        logicalFrame.CopyTo(datagram);
        Span<byte> fullTag = stackalloc byte[SHA256.HashSizeInBytes];
        HMACSHA256.HashData(sessionSecret, logicalFrame, fullTag);
        fullTag[..AuthenticationTagBytes].CopyTo(datagram.AsSpan(logicalFrame.Length));
        CryptographicOperations.ZeroMemory(fullTag);
        return datagram;
    }

    public static byte[] Decode(ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> sessionSecret)
    {
        ValidateSecret(sessionSecret);
        var frameBytes = datagram.Length - AuthenticationTagBytes;
        if (frameBytes < ProtocolV1Codec.HeaderBytes ||
            frameBytes > DeskInkConstants.MaximumInputFrameBytes)
            throw new ProtocolException("LAN datagram length is invalid");

        var frame = datagram[..frameBytes];
        var receivedTag = datagram[frameBytes..];
        Span<byte> fullTag = stackalloc byte[SHA256.HashSizeInBytes];
        HMACSHA256.HashData(sessionSecret, frame, fullTag);
        var authenticated = CryptographicOperations.FixedTimeEquals(
            receivedTag,
            fullTag[..AuthenticationTagBytes]);
        CryptographicOperations.ZeroMemory(fullTag);
        if (!authenticated) throw new ProtocolException("LAN authentication tag mismatch");
        return frame.ToArray();
    }

    private static void ValidateSecret(ReadOnlySpan<byte> sessionSecret)
    {
        if (sessionSecret.Length != SessionSecretBytes)
            throw new ProtocolException("LAN session secret must be 32 bytes");
    }
}
