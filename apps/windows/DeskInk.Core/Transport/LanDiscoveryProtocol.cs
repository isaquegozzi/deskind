using System.Buffers.Binary;
using System.Text;
using DeskInk.Core.Protocol;

namespace DeskInk.Core.Transport;

public readonly record struct LanDiscoveryReply(
    ulong Nonce,
    ushort ControlPort,
    byte[] CertificateFingerprint,
    string HostName);

public static class LanDiscoveryProtocol
{
    private const uint Magic = 0x444B5344;
    private const byte Version = 1;
    private const byte QueryType = 1;
    private const byte ReplyType = 2;
    private const int HeaderBytes = 16;
    private const int FingerprintBytes = 32;
    private const int MaximumHostNameBytes = 63;

    public static byte[] EncodeQuery(ulong nonce) => EncodeHeader(QueryType, HeaderBytes, nonce);

    public static ulong DecodeQuery(ReadOnlySpan<byte> packet)
    {
        ValidateHeader(packet, QueryType, HeaderBytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(packet[8..16]);
    }

    public static byte[] EncodeReply(LanDiscoveryReply reply)
    {
        if (reply.ControlPort == 0) throw new ProtocolException("Discovery control port is zero");
        if (reply.CertificateFingerprint.Length != FingerprintBytes)
            throw new ProtocolException("Discovery fingerprint must be SHA-256");
        var hostName = Encoding.UTF8.GetBytes(reply.HostName);
        if (hostName.Length is 0 or > MaximumHostNameBytes)
            throw new ProtocolException("Discovery host name length is invalid");
        var packet = EncodeHeader(ReplyType, HeaderBytes + 2 + FingerprintBytes + 1 + hostName.Length, reply.Nonce);
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[16..18], reply.ControlPort);
        reply.CertificateFingerprint.CopyTo(span[18..50]);
        span[50] = checked((byte)hostName.Length);
        hostName.CopyTo(span[51..]);
        return packet;
    }

    public static LanDiscoveryReply DecodeReply(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderBytes + 2 + FingerprintBytes + 1)
            throw new ProtocolException("Discovery reply is truncated");
        ValidateHeader(packet, ReplyType, packet.Length);
        var nameBytes = packet[50];
        if (nameBytes is 0 or > MaximumHostNameBytes || packet.Length != 51 + nameBytes)
            throw new ProtocolException("Discovery host name length mismatch");
        string hostName;
        try { hostName = new UTF8Encoding(false, true).GetString(packet[51..]); }
        catch (DecoderFallbackException exception)
        {
            throw new ProtocolException($"Discovery host name is not UTF-8: {exception.Message}");
        }
        return new LanDiscoveryReply(
            BinaryPrimitives.ReadUInt64LittleEndian(packet[8..16]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[16..18]),
            packet[18..50].ToArray(),
            hostName);
    }

    private static byte[] EncodeHeader(byte type, int length, ulong nonce)
    {
        var packet = new byte[length];
        var span = packet.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[0..4], Magic);
        span[4] = Version;
        span[5] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..8], checked((ushort)length));
        BinaryPrimitives.WriteUInt64LittleEndian(span[8..16], nonce);
        return packet;
    }

    private static void ValidateHeader(ReadOnlySpan<byte> packet, byte type, int expectedLength)
    {
        if (packet.Length != expectedLength) throw new ProtocolException("Discovery packet length mismatch");
        if (BinaryPrimitives.ReadUInt32LittleEndian(packet[0..4]) != Magic)
            throw new ProtocolException("Discovery magic mismatch");
        if (packet[4] != Version) throw new ProtocolException("Discovery version mismatch");
        if (packet[5] != type) throw new ProtocolException("Discovery packet type mismatch");
        if (BinaryPrimitives.ReadUInt16LittleEndian(packet[6..8]) != packet.Length)
            throw new ProtocolException("Discovery header length mismatch");
    }
}
