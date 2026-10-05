using System.Buffers.Binary;
using System.Security.Cryptography;
using DeskInk.Core.Overlay;
using DeskInk.Core.Transport;

namespace DeskInk.Core.Protocol;

public enum ControlMessageType : byte
{
    Hello = 0x01,
    HelloAck = 0x02,
    InputBind = 0x03,
    Ping = 0x04,
    Pong = 0x05,
    Goodbye = 0x06,
    SetClockSync = 0x07,
    SetMouseMode = 0x20,
    SetMonitor = 0x21,
    SetOverlayTool = 0x22,
    OverlayCommand = 0x23,
    SetOverlayMode = 0x24,
    SetOutputMode = 0x25,
    ExecuteShortcut = 0x26,
    LanPairChallenge = 0x30,
    LanPairConfirm = 0x31,
    LanPairAck = 0x32,
}

public enum MouseMode : byte
{
    ClickDrag = 0,
    PenScroll = 1,
}

public readonly record struct HelloMessage(ulong ClientNonce);
public readonly record struct HelloAckMessage(
    ulong SessionId,
    byte[] InputBindToken,
    byte MonitorCount,
    byte SelectedMonitorIndex);
public readonly record struct InputBindMessage(ulong SessionId, byte[] InputBindToken);
public readonly record struct PingMessage(ulong SessionId, long ClientTimeUs);
public readonly record struct PongMessage(ulong SessionId, long ClientTimeUs, long HostReceiveTimeUs, long HostSendTimeUs);
public readonly record struct SetClockSyncMessage(ulong SessionId, long HostMinusClientUs, uint UncertaintyUs);
public readonly record struct SetMouseModeMessage(ulong SessionId, MouseMode Mode);
public readonly record struct SetMonitorMessage(ulong SessionId, byte MonitorIndex);
public enum OverlayControlCommand : byte { Undo = 0, Redo = 1, Clear = 2 }
public enum OverlayMode : byte { Hidden = 0, ClickThrough = 1, Interactive = 2 }
public readonly record struct SetOverlayToolMessage(ulong SessionId, OverlayTool Tool);
public readonly record struct OverlayCommandMessage(ulong SessionId, OverlayControlCommand Command);
public readonly record struct SetOverlayModeMessage(ulong SessionId, OverlayMode Mode);
public enum DeskInkOutputMode : byte { Mouse = 0, SyntheticPen = 1, Overlay = 2 }
public readonly record struct SetOutputModeMessage(ulong SessionId, DeskInkOutputMode Mode);
public enum ShortcutCommand : byte
{
    Copy = 0,
    Paste = 1,
    Find = 2,
    PageUp = 3,
    PageDown = 4,
    MediaPlayPause = 5,
    VolumeDown = 6,
    VolumeUp = 7,
}
public readonly record struct ExecuteShortcutMessage(ulong SessionId, ShortcutCommand Command);
public readonly record struct LanPairChallengeMessage(byte[] CertificateFingerprint);
public readonly record struct LanPairAckMessage(
    ulong SessionId,
    byte[] SessionSecret,
    byte MonitorCount,
    byte SelectedMonitorIndex,
    ushort UdpInputPort);

public static class ControlProtocolV1
{
    private delegate void PayloadWriter(Span<byte> payload);

    public const int BindTokenBytes = 16;

    public static byte[] EncodeHello(HelloMessage message)
    {
        if (message.ClientNonce == 0) throw new ProtocolException("HELLO nonce is zero");
        return Encode(ControlMessageType.Hello, 0, sizeof(ulong), payload =>
            BinaryPrimitives.WriteUInt64LittleEndian(payload, message.ClientNonce));
    }

    public static HelloAckMessage DecodeHelloAck(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.HelloAck, BindTokenBytes + 2);
        var sessionId = ReadSession(frame, "HELLO_ACK");
        var monitorCount = payload[BindTokenBytes];
        var selectedMonitorIndex = payload[BindTokenBytes + 1];
        if (monitorCount == 0 || selectedMonitorIndex >= monitorCount)
        {
            throw new ProtocolException("Invalid monitor selection in HELLO_ACK");
        }
        return new HelloAckMessage(
            sessionId,
            payload[..BindTokenBytes].ToArray(),
            monitorCount,
            selectedMonitorIndex);
    }

    public static byte[] EncodeInputBind(InputBindMessage message)
    {
        if (message.SessionId == 0) throw new ProtocolException("INPUT_BIND session is zero");
        if (message.InputBindToken.Length != BindTokenBytes)
        {
            throw new ProtocolException("Invalid bind token length");
        }
        return Encode(ControlMessageType.InputBind, message.SessionId, BindTokenBytes, payload =>
            message.InputBindToken.CopyTo(payload));
    }

    public static byte[] EncodeHelloAck(HelloAckMessage message)
    {
        if (message.SessionId == 0) throw new ProtocolException("HELLO_ACK session is zero");
        if (message.InputBindToken.Length != BindTokenBytes)
        {
            throw new ProtocolException("Invalid bind token length");
        }
        if (message.MonitorCount == 0 || message.SelectedMonitorIndex >= message.MonitorCount)
        {
            throw new ProtocolException("Invalid monitor selection in HELLO_ACK");
        }
        return Encode(
            ControlMessageType.HelloAck,
            message.SessionId,
            BindTokenBytes + 2,
            payload =>
            {
                message.InputBindToken.CopyTo(payload);
                payload[BindTokenBytes] = message.MonitorCount;
                payload[BindTokenBytes + 1] = message.SelectedMonitorIndex;
            });
    }

    public static HelloMessage DecodeHello(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.Hello, expectedPayloadBytes: sizeof(ulong));
        return new HelloMessage(BinaryPrimitives.ReadUInt64LittleEndian(payload));
    }

    public static InputBindMessage DecodeInputBind(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.InputBind, BindTokenBytes);
        var sessionId = BinaryPrimitives.ReadUInt64LittleEndian(frame[12..20]);
        if (sessionId == 0) throw new ProtocolException("INPUT_BIND session is zero");
        return new InputBindMessage(sessionId, payload.ToArray());
    }

    public static PingMessage DecodePing(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.Ping, sizeof(long));
        return new PingMessage(ReadSession(frame, "PING"), BinaryPrimitives.ReadInt64LittleEndian(payload));
    }

    public static byte[] EncodePong(PongMessage message)
    {
        if (message.SessionId == 0) throw new ProtocolException("PONG session is zero");
        return Encode(ControlMessageType.Pong, message.SessionId, sizeof(long) * 3, payload =>
        {
            BinaryPrimitives.WriteInt64LittleEndian(payload[0..8], message.ClientTimeUs);
            BinaryPrimitives.WriteInt64LittleEndian(payload[8..16], message.HostReceiveTimeUs);
            BinaryPrimitives.WriteInt64LittleEndian(payload[16..24], message.HostSendTimeUs);
        });
    }

    public static SetClockSyncMessage DecodeSetClockSync(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.SetClockSync, 12);
        return new SetClockSyncMessage(
            ReadSession(frame, "SET_CLOCK_SYNC"),
            BinaryPrimitives.ReadInt64LittleEndian(payload[0..8]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]));
    }

    public static SetMouseModeMessage DecodeSetMouseMode(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.SetMouseMode, expectedPayloadBytes: 1);
        var sessionId = BinaryPrimitives.ReadUInt64LittleEndian(frame[12..20]);
        if (sessionId == 0) throw new ProtocolException("SET_MOUSE_MODE session is zero");
        if (!Enum.IsDefined(typeof(MouseMode), payload[0]))
        {
            throw new ProtocolException($"Unknown mouse mode: {payload[0]}");
        }
        return new SetMouseModeMessage(sessionId, (MouseMode)payload[0]);
    }

    public static SetMonitorMessage DecodeSetMonitor(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.SetMonitor, expectedPayloadBytes: 1);
        var sessionId = BinaryPrimitives.ReadUInt64LittleEndian(frame[12..20]);
        if (sessionId == 0) throw new ProtocolException("SET_MONITOR session is zero");
        return new SetMonitorMessage(sessionId, payload[0]);
    }

    public static SetOverlayToolMessage DecodeSetOverlayTool(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.SetOverlayTool, 1);
        var sessionId = ReadSession(frame, "SET_OVERLAY_TOOL");
        if (!Enum.IsDefined(typeof(OverlayTool), payload[0]))
            throw new ProtocolException($"Unknown overlay tool: {payload[0]}");
        return new SetOverlayToolMessage(sessionId, (OverlayTool)payload[0]);
    }

    public static OverlayCommandMessage DecodeOverlayCommand(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.OverlayCommand, 1);
        var sessionId = ReadSession(frame, "OVERLAY_COMMAND");
        if (!Enum.IsDefined(typeof(OverlayControlCommand), payload[0]))
            throw new ProtocolException($"Unknown overlay command: {payload[0]}");
        return new OverlayCommandMessage(sessionId, (OverlayControlCommand)payload[0]);
    }

    public static SetOverlayModeMessage DecodeSetOverlayMode(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.SetOverlayMode, 1);
        var sessionId = ReadSession(frame, "SET_OVERLAY_MODE");
        if (!Enum.IsDefined(typeof(OverlayMode), payload[0]))
            throw new ProtocolException($"Unknown overlay mode: {payload[0]}");
        return new SetOverlayModeMessage(sessionId, (OverlayMode)payload[0]);
    }

    public static SetOutputModeMessage DecodeSetOutputMode(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.SetOutputMode, 1);
        var sessionId = ReadSession(frame, "SET_OUTPUT_MODE");
        if (!Enum.IsDefined(typeof(DeskInkOutputMode), payload[0]))
            throw new ProtocolException($"Unknown output mode: {payload[0]}");
        return new SetOutputModeMessage(sessionId, (DeskInkOutputMode)payload[0]);
    }

    public static ExecuteShortcutMessage DecodeExecuteShortcut(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.ExecuteShortcut, 1);
        var sessionId = ReadSession(frame, "EXECUTE_SHORTCUT");
        if (!Enum.IsDefined(typeof(ShortcutCommand), payload[0]))
            throw new ProtocolException($"Unknown shortcut command: {payload[0]}");
        return new ExecuteShortcutMessage(sessionId, (ShortcutCommand)payload[0]);
    }

    public static byte[] EncodeLanPairChallenge(ReadOnlySpan<byte> certificateFingerprint)
    {
        if (certificateFingerprint.Length != SHA256.HashSizeInBytes)
            throw new ProtocolException("LAN certificate fingerprint must be SHA-256");
        var copy = certificateFingerprint.ToArray();
        return Encode(ControlMessageType.LanPairChallenge, 0, copy.Length, payload => copy.CopyTo(payload));
    }

    public static byte[] DecodeLanPairConfirm(ReadOnlySpan<byte> frame)
    {
        var payload = Validate(frame, ControlMessageType.LanPairConfirm, SHA256.HashSizeInBytes);
        if (BinaryPrimitives.ReadUInt64LittleEndian(frame[12..20]) != 0)
            throw new ProtocolException("LAN_PAIR_CONFIRM session must be zero");
        return payload.ToArray();
    }

    public static byte[] EncodeLanPairAck(LanPairAckMessage message)
    {
        if (message.SessionId == 0) throw new ProtocolException("LAN_PAIR_ACK session is zero");
        if (message.SessionSecret.Length != LanAuthenticatedDatagram.SessionSecretBytes)
            throw new ProtocolException("Invalid LAN session secret length");
        if (message.MonitorCount == 0 || message.SelectedMonitorIndex >= message.MonitorCount)
            throw new ProtocolException("Invalid LAN monitor selection");
        if (message.UdpInputPort == 0) throw new ProtocolException("LAN UDP port is zero");
        return Encode(ControlMessageType.LanPairAck, message.SessionId, 36, payload =>
        {
            message.SessionSecret.CopyTo(payload);
            payload[32] = message.MonitorCount;
            payload[33] = message.SelectedMonitorIndex;
            BinaryPrimitives.WriteUInt16LittleEndian(payload[34..36], message.UdpInputPort);
        });
    }

    private static ulong ReadSession(ReadOnlySpan<byte> frame, string messageName)
    {
        var sessionId = BinaryPrimitives.ReadUInt64LittleEndian(frame[12..20]);
        if (sessionId == 0) throw new ProtocolException($"{messageName} session is zero");
        return sessionId;
    }

    public static ControlMessageType PeekType(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < ProtocolV1Codec.HeaderBytes) throw new ProtocolException("Control frame is truncated");
        if (BinaryPrimitives.ReadUInt32LittleEndian(frame[0..4]) != DeskInkConstants.ProtocolMagic)
        {
            throw new ProtocolException("Bad protocol magic");
        }
        return (ControlMessageType)frame[6];
    }

    public static byte[] EncodeForTests(
        ControlMessageType type,
        ulong sessionId,
        ReadOnlySpan<byte> payload)
    {
        var copy = payload.ToArray();
        return Encode(type, sessionId, copy.Length, target => copy.CopyTo(target));
    }

    private static byte[] Encode(
        ControlMessageType type,
        ulong sessionId,
        int payloadBytes,
        PayloadWriter writePayload)
    {
        var frameBytes = ProtocolV1Codec.HeaderBytes + payloadBytes;
        if (frameBytes > DeskInkConstants.MaximumControlFrameBytes)
        {
            throw new ProtocolException("Control frame exceeds limit");
        }
        var frame = new byte[frameBytes];
        var span = frame.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[0..4], DeskInkConstants.ProtocolMagic);
        span[4] = DeskInkConstants.ProtocolMajor;
        span[5] = DeskInkConstants.ProtocolMinor;
        span[6] = (byte)type;
        span[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..10], ProtocolV1Codec.HeaderBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..12], checked((ushort)frameBytes));
        BinaryPrimitives.WriteUInt64LittleEndian(span[12..20], sessionId);
        writePayload(span[ProtocolV1Codec.HeaderBytes..]);
        return frame;
    }

    private static ReadOnlySpan<byte> Validate(
        ReadOnlySpan<byte> frame,
        ControlMessageType expectedType,
        int expectedPayloadBytes)
    {
        var expectedFrameBytes = ProtocolV1Codec.HeaderBytes + expectedPayloadBytes;
        if (frame.Length != expectedFrameBytes) throw new ProtocolException("Control frame length mismatch");
        if (BinaryPrimitives.ReadUInt32LittleEndian(frame[0..4]) != DeskInkConstants.ProtocolMagic)
        {
            throw new ProtocolException("Bad protocol magic");
        }
        if (frame[4] != DeskInkConstants.ProtocolMajor || frame[5] != DeskInkConstants.ProtocolMinor)
        {
            throw new ProtocolException("Unsupported protocol version");
        }
        if (frame[6] != (byte)expectedType) throw new ProtocolException("Unexpected control message type");
        if (frame[7] != 0) throw new ProtocolException("Unknown control flags");
        if (BinaryPrimitives.ReadUInt16LittleEndian(frame[8..10]) != ProtocolV1Codec.HeaderBytes)
        {
            throw new ProtocolException("Unexpected control header size");
        }
        if (BinaryPrimitives.ReadUInt16LittleEndian(frame[10..12]) != frame.Length)
        {
            throw new ProtocolException("Control header length mismatch");
        }
        return frame[ProtocolV1Codec.HeaderBytes..];
    }
}
