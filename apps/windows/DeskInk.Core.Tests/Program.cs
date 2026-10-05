using DeskInk.Core;
using DeskInk.Core.Input;
using DeskInk.Core.Overlay;
using DeskInk.Core.Protocol;
using DeskInk.Core.Transport;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

var failures = new List<string>();

Run("foundation configuration", TestFoundationConfiguration);
Run("golden vector decode", TestGoldenVectorDecode);
Run("golden vector encode", TestGoldenVectorEncode);
Run("malformed frames", TestMalformedFrames);
Run("sequence tracking", TestSequenceTracking);
Run("pen state failsafe", TestPenStateFailsafe);
Run("control handshake codec", TestControlHandshakeCodec);
Run("TCP stream framing", TestStreamFraming);
Run("LAN authenticated datagram", TestLanAuthenticatedDatagram);
Run("LAN pairing identity", TestLanPairingIdentity);
Run("LAN TLS server identity", TestLanTlsServerIdentity);
Run("LAN discovery codec", TestLanDiscoveryCodec);
Run("mouse input translation", TestMouseInputTranslation);
Run("synthetic pen translation", TestSyntheticPenTranslation);
Run("overlay stroke model", TestOverlayStrokeModel);
Run("overlay input translation", TestOverlayInputTranslation);
Run("desktop coordinate mapping", TestDesktopCoordinateMapping);
Run("100k synthetic samples", TestSyntheticLoad);

if (failures.Count != 0)
{
    foreach (var failure in failures) Console.Error.WriteLine($"FAIL: {failure}");
    return 1;
}

Console.WriteLine("DeskInk.Core protocol checks passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
    }
}

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void ExpectProtocolError(Action action, string message)
{
    try
    {
        action();
    }
    catch (ProtocolException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

void TestFoundationConfiguration()
{
    Check(
        DeskInkConstants.HasIsolatedChannels(DeskInkConstants.ControlPort, DeskInkConstants.InputPort),
        "Control and input ports must be valid and isolated");
    Check(
        DeskInkConstants.HasIsolatedChannels(DeskInkConstants.LanControlPort, DeskInkConstants.LanInputPort),
        "LAN control and input ports must be valid and isolated");
    Check(
        new[]
        {
            DeskInkConstants.ControlPort,
            DeskInkConstants.InputPort,
            DeskInkConstants.LanControlPort,
            DeskInkConstants.LanInputPort,
        }.Distinct().Count() == 4,
        "USB and LAN ports must not overlap");
    Check(!DeskInkConstants.IsValidUserPort(1024), "Privileged ports must be rejected");
    Check(!DeskInkConstants.HasIsolatedChannels(27183, 27183), "Shared channel port must be rejected");
    Check(
        DeskInkConstants.HasValidFrameLimits(
            DeskInkConstants.MaximumInputFrameBytes,
            DeskInkConstants.MaximumControlFrameBytes),
        "Input frame limit must be bounded");
}

void TestGoldenVectorDecode()
{
    var frame = ProtocolV1Codec.DecodeInputBatch(LoadGoldenVector());
    Check(frame.SessionId == 0x0102030405060708, "sessionId mismatch");
    Check(frame.Sequence == 42, "sequence mismatch");
    Check(frame.BaseMonotonicTimeUs == 1_000_000, "base timestamp mismatch");
    Check(frame.ToolKind == ToolKind.Stylus, "tool mismatch");
    Check(frame.PointerId == 7, "pointer mismatch");
    Check(frame.Samples.Count == 1, "sample count mismatch");
    Check(frame.Samples[0].PressureNormalized == 49151, "pressure mismatch");
    Check(frame.Samples[0].OrientationCentidegrees == -9000, "orientation mismatch");
}

void TestGoldenVectorEncode()
{
    var encoded = ProtocolV1Codec.EncodeInputBatch(GoldenFrame());
    Check(encoded.AsSpan().SequenceEqual(LoadGoldenVector()), "C# encoder differs from Android golden bytes");
}

void TestMalformedFrames()
{
    var badMagic = LoadGoldenVector();
    badMagic[0] = 0;
    ExpectProtocolError(() => ProtocolV1Codec.DecodeInputBatch(badMagic), "Bad magic was accepted");

    var truncated = LoadGoldenVector()[..^1];
    ExpectProtocolError(() => ProtocolV1Codec.DecodeInputBatch(truncated), "Truncated frame was accepted");

    var badCount = LoadGoldenVector();
    badCount[32] = 0;
    ExpectProtocolError(() => ProtocolV1Codec.DecodeInputBatch(badCount), "Zero sample count was accepted");

    var impossibleState = GoldenFrame().Samples[0] with { State = PenSampleState.Contact };
    ExpectProtocolError(
        () => ProtocolV1Codec.EncodeInputBatch(GoldenFrame() with { Samples = [impossibleState] }),
        "CONTACT without IN_RANGE was accepted");

    var unsignedBoundaries = GoldenFrame().Samples[0] with
    {
        XNormalized = ushort.MinValue,
        YNormalized = ushort.MaxValue,
        PressureNormalized = ushort.MaxValue,
        State = PenSampleState.InRange | PenSampleState.PressureValid,
    };
    var boundaryFrame = ProtocolV1Codec.DecodeInputBatch(ProtocolV1Codec.EncodeInputBatch(
        GoldenFrame() with { Samples = [unsignedBoundaries] }));
    Check(
        boundaryFrame.Samples[0].XNormalized == ushort.MinValue &&
        boundaryFrame.Samples[0].YNormalized == ushort.MaxValue &&
        boundaryFrame.Samples[0].PressureNormalized == ushort.MaxValue,
        "Normalized coordinate/pressure ushort boundaries did not round-trip");
}

void TestSequenceTracking()
{
    var tracker = new SequenceTracker();
    Check(tracker.Observe(10).Disposition == SequenceDisposition.Accepted, "first sequence rejected");
    Check(tracker.Observe(10).Disposition == SequenceDisposition.Duplicate, "duplicate accepted");
    var gap = tracker.Observe(13);
    Check(gap.Disposition == SequenceDisposition.AcceptedWithGap && gap.Gap == 2, "gap not counted");
    Check(tracker.Observe(12).Disposition == SequenceDisposition.ReorderedOrStale, "stale accepted");

    tracker.Reset();
    Check(tracker.Observe(uint.MaxValue).Disposition == SequenceDisposition.Accepted, "wrap seed rejected");
    Check(tracker.Observe(0).Disposition == SequenceDisposition.Accepted, "sequence wrap rejected");
}

void TestPenStateFailsafe()
{
    var machine = new PenStateMachine();
    Check(machine.Apply(Sample(PenSampleState.InRange)) == PenTransition.Hover, "hover transition failed");
    Check(
        machine.Apply(Sample(PenSampleState.InRange | PenSampleState.Contact)) == PenTransition.Down,
        "down transition failed");
    Check(machine.ReleaseAll() == PenTransition.Up, "release-all did not emit UP");
    Check(
        machine.Apply(Sample(PenSampleState.InRange | PenSampleState.Contact)) == PenTransition.Suppressed,
        "stale contact resurrected after release");
    Check(machine.Apply(Sample(PenSampleState.InRange)) == PenTransition.None, "physical release did not rearm");
    Check(
        machine.Apply(Sample(PenSampleState.InRange | PenSampleState.Contact)) == PenTransition.Down,
        "new physical down was not accepted");
    Check(
        machine.Apply(Sample(PenSampleState.Canceled)) == PenTransition.Up,
        "cancel did not release contact");
}

void TestControlHandshakeCodec()
{
    const ulong nonce = 0x8877665544332211;
    var hello = ControlProtocolV1.EncodeHello(new HelloMessage(nonce));
    Check(ControlProtocolV1.DecodeHello(hello).ClientNonce == nonce, "HELLO nonce mismatch");
    ExpectProtocolError(
        () => ControlProtocolV1.EncodeHello(new HelloMessage(0)),
        "Zero HELLO nonce was accepted");

    const ulong sessionId = 0x1020304050607080;
    var token = Enumerable.Range(0, ControlProtocolV1.BindTokenBytes).Select(value => (byte)value).ToArray();
    var ack = ControlProtocolV1.EncodeHelloAck(new HelloAckMessage(sessionId, token, 2, 0));
    var decodedAck = ControlProtocolV1.DecodeHelloAck(ack);
    Check(decodedAck.SessionId == sessionId, "ACK session mismatch");
    Check(decodedAck.InputBindToken.AsSpan().SequenceEqual(token), "ACK token mismatch");
    Check(decodedAck.MonitorCount == 2 && decodedAck.SelectedMonitorIndex == 0,
        "ACK monitor metadata mismatch");

    var bind = ControlProtocolV1.EncodeInputBind(new InputBindMessage(sessionId, token));
    var decodedBind = ControlProtocolV1.DecodeInputBind(bind);
    Check(decodedBind.SessionId == sessionId, "INPUT_BIND session mismatch");
    Check(decodedBind.InputBindToken.AsSpan().SequenceEqual(token), "INPUT_BIND token mismatch");
    ExpectProtocolError(
        () => ControlProtocolV1.EncodeHelloAck(new HelloAckMessage(sessionId, token[..^1], 2, 0)),
        "Short bind token was accepted");
    ExpectProtocolError(
        () => ControlProtocolV1.EncodeInputBind(new InputBindMessage(sessionId, token[..^1])),
        "Short INPUT_BIND token was accepted");

    Span<byte> pingPayload = stackalloc byte[sizeof(long)];
    BinaryPrimitives.WriteInt64LittleEndian(pingPayload, 1234);
    var ping = ControlProtocolV1.DecodePing(
        ControlProtocolV1.EncodeForTests(ControlMessageType.Ping, sessionId, pingPayload));
    Check(ping.SessionId == sessionId && ping.ClientTimeUs == 1234, "PING mismatch");
    var pong = ControlProtocolV1.EncodePong(new PongMessage(sessionId, 1234, 1250, 1251));
    Check(ControlProtocolV1.PeekType(pong) == ControlMessageType.Pong, "PONG type mismatch");
    Span<byte> clockPayload = stackalloc byte[12];
    BinaryPrimitives.WriteInt64LittleEndian(clockPayload[0..8], 99);
    BinaryPrimitives.WriteUInt32LittleEndian(clockPayload[8..12], 12);
    var clock = ControlProtocolV1.DecodeSetClockSync(
        ControlProtocolV1.EncodeForTests(ControlMessageType.SetClockSync, sessionId, clockPayload));
    Check(clock.HostMinusClientUs == 99 && clock.UncertaintyUs == 12, "clock sync mismatch");

    var modeFrame = ControlProtocolV1.EncodeForTests(
        ControlMessageType.SetMouseMode,
        sessionId,
        [(byte)MouseMode.PenScroll]);
    var mode = ControlProtocolV1.DecodeSetMouseMode(modeFrame);
    Check(mode.SessionId == sessionId && mode.Mode == MouseMode.PenScroll,
        "SET_MOUSE_MODE mismatch");
    var invalidMode = ControlProtocolV1.EncodeForTests(
        ControlMessageType.SetMouseMode,
        sessionId,
        [0xff]);
    ExpectProtocolError(
        () => ControlProtocolV1.DecodeSetMouseMode(invalidMode),
        "Unknown mouse mode was accepted");

    var monitorFrame = ControlProtocolV1.EncodeForTests(
        ControlMessageType.SetMonitor,
        sessionId,
        [1]);
    var monitor = ControlProtocolV1.DecodeSetMonitor(monitorFrame);
    Check(monitor.SessionId == sessionId && monitor.MonitorIndex == 1,
        "SET_MONITOR mismatch");

    var shortcutFrame = ControlProtocolV1.EncodeForTests(
        ControlMessageType.ExecuteShortcut,
        sessionId,
        [(byte)ShortcutCommand.Find]);
    var shortcut = ControlProtocolV1.DecodeExecuteShortcut(shortcutFrame);
    Check(shortcut.SessionId == sessionId && shortcut.Command == ShortcutCommand.Find,
        "EXECUTE_SHORTCUT mismatch");
    ExpectProtocolError(
        () => ControlProtocolV1.DecodeExecuteShortcut(ControlProtocolV1.EncodeForTests(
            ControlMessageType.ExecuteShortcut,
            sessionId,
            [0xff])),
        "Unknown shortcut command was accepted");
}

void TestStreamFraming()
{
    using var stream = new MemoryStream();
    var first = new byte[] { 1, 2, 3 };
    var second = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    FramedStream.WriteAsync(stream, first, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    FramedStream.WriteAsync(stream, second, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    stream.Position = 0;
    var decodedFirst = FramedStream.ReadAsync(stream, 128, CancellationToken.None)
        .AsTask().GetAwaiter().GetResult();
    var decodedSecond = FramedStream.ReadAsync(stream, 128, CancellationToken.None)
        .AsTask().GetAwaiter().GetResult();
    var eof = FramedStream.ReadAsync(stream, 128, CancellationToken.None)
        .AsTask().GetAwaiter().GetResult();
    Check(decodedFirst is not null && decodedFirst.AsSpan().SequenceEqual(first), "First framed message mismatch");
    Check(decodedSecond is not null && decodedSecond.AsSpan().SequenceEqual(second), "Second framed message mismatch");
    Check(eof is null, "Clean stream EOF was not detected");
}

void TestLanAuthenticatedDatagram()
{
    var secret = Enumerable.Range(0, LanAuthenticatedDatagram.SessionSecretBytes)
        .Select(value => (byte)value)
        .ToArray();
    var frame = ProtocolV1Codec.EncodeInputBatch(GoldenFrame());
    var datagram = LanAuthenticatedDatagram.Encode(frame, secret);
    Check(datagram.Length == frame.Length + LanAuthenticatedDatagram.AuthenticationTagBytes,
        "LAN authentication tag length mismatch");
    Check(LanAuthenticatedDatagram.Decode(datagram, secret).AsSpan().SequenceEqual(frame),
        "LAN authenticated frame roundtrip mismatch");

    var tampered = datagram.ToArray();
    tampered[ProtocolV1Codec.HeaderBytes] ^= 1;
    ExpectProtocolError(() => LanAuthenticatedDatagram.Decode(tampered, secret),
        "Tampered LAN datagram was accepted");
    var wrongSecret = secret.ToArray();
    wrongSecret[0] ^= 1;
    ExpectProtocolError(() => LanAuthenticatedDatagram.Decode(datagram, wrongSecret),
        "LAN datagram with wrong secret was accepted");
    ExpectProtocolError(() => LanAuthenticatedDatagram.Decode(datagram, secret[..^1]),
        "Short LAN session secret was accepted");
}

void TestLanPairingIdentity()
{
    using var identity = LanPairingIdentity.Create(TimeSpan.FromHours(1));
    Check(identity.Certificate.HasPrivateKey, "LAN pairing certificate has no private key");
    Check(identity.CertificateFingerprint.Length == 64, "LAN certificate fingerprint is not SHA-256");
    Check(identity.ComparisonCode.Length == 9 && identity.ComparisonCode[4] == '-',
        "LAN comparison code format mismatch");
    var first = identity.CreateSessionSecret();
    var second = identity.CreateSessionSecret();
    Check(first.Length == LanAuthenticatedDatagram.SessionSecretBytes,
        "LAN session secret length mismatch");
    Check(!first.AsSpan().SequenceEqual(second), "LAN session secrets were reused");

    var fingerprint = Convert.FromHexString(identity.CertificateFingerprint);
    var challenge = ControlProtocolV1.EncodeLanPairChallenge(fingerprint);
    Check(ControlProtocolV1.PeekType(challenge) == ControlMessageType.LanPairChallenge,
        "LAN pair challenge type mismatch");
    var confirm = ControlProtocolV1.EncodeForTests(ControlMessageType.LanPairConfirm, 0, fingerprint);
    Check(ControlProtocolV1.DecodeLanPairConfirm(confirm).AsSpan().SequenceEqual(fingerprint),
        "LAN pair confirmation fingerprint mismatch");
    var ack = ControlProtocolV1.EncodeLanPairAck(new LanPairAckMessage(7, first, 2, 1, 27186));
    Check(ControlProtocolV1.PeekType(ack) == ControlMessageType.LanPairAck,
        "LAN pair ack type mismatch");
    Check(ack[^4] == 2 && ack[^3] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(ack[^2..]) == 27186,
        "LAN pair ack metadata mismatch");
}

void TestLanTlsServerIdentity()
{
    using var identity = LanPairingIdentity.Create(TimeSpan.FromMinutes(5));
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var endpoint = (IPEndPoint)listener.LocalEndpoint;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var server = Task.Run(async () =>
    {
        using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
        using var tls = new SslStream(accepted.GetStream(), leaveInnerStreamOpen: false);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = identity.Certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        }, timeout.Token);
    }, timeout.Token);

    using var client = new TcpClient();
    client.ConnectAsync(IPAddress.Loopback, endpoint.Port, timeout.Token).GetAwaiter().GetResult();
    using var clientTls = new SslStream(
        client.GetStream(),
        leaveInnerStreamOpen: false,
        (_, _, _, _) => true);
    clientTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
    {
        TargetHost = "DeskInk LAN Pairing",
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    }, timeout.Token).GetAwaiter().GetResult();
    server.GetAwaiter().GetResult();
    listener.Stop();
}

void TestLanDiscoveryCodec()
{
    const ulong nonce = 0x1020304050607080;
    var query = LanDiscoveryProtocol.EncodeQuery(nonce);
    Check(query.Length == 16 && LanDiscoveryProtocol.DecodeQuery(query) == nonce,
        "LAN discovery query mismatch");
    var fingerprint = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    var encoded = LanDiscoveryProtocol.EncodeReply(
        new LanDiscoveryReply(nonce, DeskInkConstants.LanControlPort, fingerprint, "DESK-PC"));
    var reply = LanDiscoveryProtocol.DecodeReply(encoded);
    Check(reply.Nonce == nonce && reply.ControlPort == DeskInkConstants.LanControlPort,
        "LAN discovery reply metadata mismatch");
    Check(reply.CertificateFingerprint.AsSpan().SequenceEqual(fingerprint) && reply.HostName == "DESK-PC",
        "LAN discovery identity mismatch");
    var tampered = encoded.ToArray();
    tampered[6]--;
    ExpectProtocolError(() => LanDiscoveryProtocol.DecodeReply(tampered),
        "LAN discovery bad length was accepted");
}

void TestMouseInputTranslation()
{
    var translator = new MouseInputTranslator();
    var actions = new List<MouseAction>();
    var hover = Sample(PenSampleState.InRange) with { XNormalized = 10, YNormalized = 20 };
    var contact = hover with { State = PenSampleState.InRange | PenSampleState.Contact };
    var release = hover;
    translator.Apply(GoldenFrame() with { Samples = [hover, contact, contact, release] }, actions.Add);
    Check(
        actions.Select(action => action.Kind).SequenceEqual([
            MouseActionKind.MoveAbsolute,
            MouseActionKind.MoveAbsolute,
            MouseActionKind.LeftDown,
            MouseActionKind.MoveAbsolute,
            MouseActionKind.MoveAbsolute,
            MouseActionKind.LeftUp,
        ]),
        "Stylus click/drag actions mismatch");

    actions.Clear();
    var sideButton = hover with { Buttons = 32 };
    translator.Apply(GoldenFrame() with { Samples = [sideButton, hover] }, actions.Add);
    Check(
        actions.Select(action => action.Kind).SequenceEqual([
            MouseActionKind.MoveAbsolute,
            MouseActionKind.RightDown,
            MouseActionKind.MoveAbsolute,
            MouseActionKind.RightUp,
        ]),
        "Stylus side-button actions mismatch");

    actions.Clear();
    translator = new MouseInputTranslator();
    var fingerFrame = GoldenFrame() with
    {
        ToolKind = ToolKind.Finger,
        PointerId = 3,
        Samples = [
            Sample(PenSampleState.InRange | PenSampleState.Contact) with { YNormalized = 10_000 },
            Sample(PenSampleState.InRange | PenSampleState.Contact) with { YNormalized = 10_400 },
            Sample(PenSampleState.InRange) with { YNormalized = 10_400 },
        ],
    };
    translator.Apply(fingerFrame, actions.Add);
    Check(actions.Count == 1 && actions[0] == new MouseAction(MouseActionKind.Wheel, WheelDelta: 8),
        "Finger scroll action mismatch");

    actions.Clear();
    translator = new MouseInputTranslator();
    translator.SetMode(MouseMode.PenScroll, actions.Add);
    translator.Apply(GoldenFrame() with
    {
        Samples = [
            hover,
            contact with { YNormalized = 10_000 },
            contact with { YNormalized = 10_400 },
            release with { YNormalized = 10_400 },
        ],
    }, actions.Add);
    Check(
        actions.Select(action => action.Kind).SequenceEqual([
            MouseActionKind.MoveAbsolute,
            MouseActionKind.Wheel,
            MouseActionKind.MoveAbsolute,
        ]),
        "Persistent pen-scroll actions mismatch");
    Check(!actions.Any(action => action.Kind is MouseActionKind.LeftDown or MouseActionKind.LeftUp),
        "Pen-scroll mode emitted a left button action");

    actions.Clear();
    translator = new MouseInputTranslator();
    translator.Apply(GoldenFrame() with { Samples = [hover] }, actions.Add);
    actions.Clear();
    translator.Apply(fingerFrame, actions.Add);
    Check(actions.Count == 0, "Finger/palm scroll was not suppressed while stylus was in range");

    actions.Clear();
    translator = new MouseInputTranslator();
    translator.Apply(GoldenFrame() with { Samples = [contact] }, actions.Add);
    actions.Clear();
    translator.ReleaseAll(actions.Add);
    Check(actions.Count == 1 && actions[0].Kind == MouseActionKind.LeftUp,
        "Mouse failsafe did not release left button");
}

void TestSyntheticPenTranslation()
{
    var translator = new PenInputTranslator();
    var actions = new List<SyntheticPenAction>();
    var hover = Sample(
        PenSampleState.InRange |
        PenSampleState.PressureValid |
        PenSampleState.TiltValid |
        PenSampleState.OrientationValid) with
    {
        XNormalized = 10,
        YNormalized = 20,
        PressureNormalized = ushort.MaxValue,
        TiltCentidegrees = 3000,
        OrientationCentidegrees = 9000,
    };
    var contact = hover with { State = hover.State | PenSampleState.Contact };
    translator.Apply(GoldenFrame() with { Samples = [hover, contact, contact, hover, Sample(PenSampleState.None)] },
        actions.Add);

    Check(actions.Count == 5, "Synthetic pen lifecycle action count mismatch");
    Check(actions[0].PointerFlags.HasFlag(SyntheticPointerFlags.New) &&
        actions[0].PointerFlags.HasFlag(SyntheticPointerFlags.InRange), "Initial hover flags mismatch");
    Check(actions[1].PointerFlags.HasFlag(SyntheticPointerFlags.Down), "Pen down was not emitted");
    Check(actions[2].PointerFlags.HasFlag(SyntheticPointerFlags.Update), "Contact update was not emitted");
    Check(actions[3].PointerFlags.HasFlag(SyntheticPointerFlags.Up), "Pen up was not emitted");
    Check(!actions[4].PointerFlags.HasFlag(SyntheticPointerFlags.InRange), "Hover exit retained in-range");
    Check(actions[1].Pressure == 1024, "Pressure was not normalized to 0..1024");
    Check(actions[1].TiltX == 30 && actions[1].TiltY == 0, "Tilt/orientation conversion mismatch");
    Check(actions[1].PenMask.HasFlag(SyntheticPenMask.Pressure | SyntheticPenMask.TiltX | SyntheticPenMask.TiltY),
        "Valid pen fields were not advertised");

    Check(PenInputTranslator.ResolveTilt(3000, 0) == (0, -30), "Up orientation tilt mismatch");
    Check(PenInputTranslator.ResolveTilt(3000, -9000) == (-30, 0), "Left orientation tilt mismatch");
    Check(PenInputTranslator.ResolveTilt(3000, 18000) == (0, 30), "Down orientation tilt mismatch");

    actions.Clear();
    translator = new PenInputTranslator();
    translator.Apply(GoldenFrame() with
    {
        ToolKind = ToolKind.Eraser,
        Samples = [contact with { Buttons = 32 }],
    }, actions.Add);
    Check(actions.Single().PenFlags.HasFlag(
        SyntheticPenFlags.Barrel | SyntheticPenFlags.Inverted | SyntheticPenFlags.Eraser),
        "Eraser/barrel flags mismatch");

    actions.Clear();
    translator.ReleaseAll(actions.Add);
    Check(actions.Single().PointerFlags.HasFlag(SyntheticPointerFlags.Up | SyntheticPointerFlags.Canceled),
        "Synthetic pen failsafe release mismatch");
    actions.Clear();
    translator.Apply(GoldenFrame() with { Samples = [contact] }, actions.Add);
    Check(actions.Count == 0, "Stale contact resurrected synthetic pen after failsafe");
    translator.Apply(GoldenFrame() with { Samples = [hover, contact] }, actions.Add);
    Check(actions.Count == 1 && actions[0].PointerFlags.HasFlag(SyntheticPointerFlags.Down),
        "Physical release did not rearm synthetic pen");
}

void TestOverlayStrokeModel()
{
    var document = new OverlayStrokeDocument();
    var start = new OverlayPoint(1_000, 2_000, 10_000);
    var end = new OverlayPoint(5_000, 2_000, 50_000);
    document.BeginStroke(OverlayTool.Pen, start, 123);
    document.AppendPoint(end);
    Check(document.EndStroke(), "Overlay stroke was not committed");
    Check(document.Strokes.Count == 1 && document.Strokes[0].Points.Count == 2,
        "Overlay stroke points mismatch");
    Check(document.Undo() && document.Strokes.Count == 0, "Overlay undo failed");
    Check(document.Redo() && document.Strokes.Count == 1, "Overlay redo failed");

    Check(document.EraseAt(new OverlayPoint(3_000, 2_100, 0), 200),
        "Segment eraser did not hit the stroke");
    Check(document.Strokes.Count == 0, "Eraser left the hit stroke behind");
    Check(document.Undo() && document.Strokes.Count == 1, "Undo eraser failed");
    Check(document.Clear() && document.Strokes.Count == 0, "Overlay clear failed");
    Check(document.Undo() && document.Strokes.Count == 1, "Undo clear failed");
    document.BeginStroke(OverlayTool.Highlighter, start, 456);
    Check(document.SnapshotActiveStroke()?.Brush.Opacity == 80,
        "Highlighter opacity/style mismatch");
    document.CancelStroke();
}

void TestOverlayInputTranslation()
{
    var translator = new OverlayInputTranslator();
    var actions = new List<OverlayInputAction>();
    var hover = Sample(PenSampleState.InRange) with { XNormalized = 10, YNormalized = 20 };
    var contact = hover with
    {
        State = PenSampleState.InRange | PenSampleState.Contact | PenSampleState.PressureValid,
        PressureNormalized = 30_000,
    };
    translator.Apply(GoldenFrame() with { Samples = [hover, contact, contact, hover] },
        OverlayTool.Pen, actions.Add);
    Check(actions.Select(action => action.Kind).SequenceEqual([
        OverlayInputActionKind.BeginStroke,
        OverlayInputActionKind.AppendPoint,
        OverlayInputActionKind.AppendPoint,
        OverlayInputActionKind.EndStroke,
    ]), "Overlay draw action sequence mismatch");
    Check(actions[0].Point.PressureNormalized == 30_000, "Overlay pressure was not preserved");

    actions.Clear();
    translator.Apply(GoldenFrame() with
    {
        Samples = [contact with { Buttons = 32 }],
    }, OverlayTool.Pen, actions.Add);
    Check(actions.Count == 1 && actions[0].Kind == OverlayInputActionKind.EraseAt,
        "Side-button eraser action mismatch");

    actions.Clear();
    translator.Apply(GoldenFrame() with { Samples = [contact] }, OverlayTool.Highlighter, actions.Add);
    translator.ReleaseAll(actions.Add);
    Check(actions.Select(action => action.Kind).SequenceEqual([
        OverlayInputActionKind.BeginStroke,
        OverlayInputActionKind.CancelStroke,
    ]), "Overlay disconnect cancel mismatch");
}

void TestDesktopCoordinateMapping()
{
    var mapper = new DesktopCoordinateMapper(
        new DesktopRectangle(0, 0, 1920, 1080),
        new DesktopRectangle(-1920, 0, 3840, 1080));
    var left = mapper.Map(0, 0);
    var right = mapper.Map(ushort.MaxValue, ushort.MaxValue);
    var primaryVirtualPixel = mapper.MapToVirtualPixel(0, 0);
    Check(left.X is >= 32760 and <= 32780 && left.Y == 0,
        $"Negative-origin left mapping mismatch: {left}");
    Check(right.X == ushort.MaxValue && right.Y == ushort.MaxValue,
        $"Selected-monitor bottom-right mapping mismatch: {right}");
    Check(primaryVirtualPixel == new AbsolutePoint(1920, 0),
        $"Primary monitor virtual-pixel origin mismatch: {primaryVirtualPixel}");

    var leftMonitor = new DesktopCoordinateMapper(
        new DesktopRectangle(-1920, 0, 1920, 1080),
        new DesktopRectangle(-1920, 0, 3840, 1080));
    Check(leftMonitor.Map(0, 0).X == 0, "Negative monitor origin did not map to virtual origin");
    Check(leftMonitor.MapToPixel(0, 0) == new AbsolutePoint(-1920, 0),
        "Synthetic pointer pixel mapping ignored monitor origin");
    Check(leftMonitor.MapToPixel(ushort.MaxValue, ushort.MaxValue) == new AbsolutePoint(-1, 1079),
        "Synthetic pointer pixel mapping missed monitor edge");
    Check(leftMonitor.MapToVirtualPixel(0, 0) == new AbsolutePoint(0, 0),
        "Synthetic pointer coordinates were not relative to virtual-screen origin");
}

void TestSyntheticLoad()
{
    const int batchSize = 32;
    const int batches = 3125;
    var samples = Enumerable.Range(0, batchSize)
        .Select(index => Sample(PenSampleState.InRange | PenSampleState.Contact) with
        {
            DeltaTimeUs = (uint)index * 100,
            StateGeneration = 1,
        })
        .ToArray();
    _ = ProtocolV1Codec.DecodeInputBatch(
        ProtocolV1Codec.EncodeInputBatch(GoldenFrame() with { Samples = samples }));
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
    long decodedSamples = 0;
    for (var index = 0; index < batches; index++)
    {
        var bytes = ProtocolV1Codec.EncodeInputBatch(
            GoldenFrame() with { Sequence = (uint)index, Samples = samples });
        decodedSamples += ProtocolV1Codec.DecodeInputBatch(bytes).Samples.Count;
    }
    Check(decodedSamples == 100_000, $"synthetic sample total was {decodedSamples}");
    var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
    var retainedGrowth = memoryAfter - memoryBefore;
    Check(retainedGrowth < 16 * 1024 * 1024, $"retained memory grew by {retainedGrowth} bytes");
    Console.WriteLine($"INFO: retained memory growth after 100k samples: {retainedGrowth} bytes");
}

InputBatchFrame GoldenFrame() => new(
    0x0102030405060708,
    42,
    1_000_000,
    ToolKind.Stylus,
    7,
    [new PenSample(250, 9, 32768, 16384, 49151, 1234, 567, -9000, 32, (PenSampleState)0x003f)]);

PenSample Sample(PenSampleState state) => new(0, 0, 1, 1, 0, 0, 0, 0, 0, state);

byte[] LoadGoldenVector()
{
    var path = Path.Combine(AppContext.BaseDirectory, "input-batch-v1.hex");
    return Convert.FromHexString(File.ReadAllText(path).Trim());
}
