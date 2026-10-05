using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Authentication;
using DeskInk.Core;
using DeskInk.Core.Input;
using DeskInk.Core.Overlay;
using DeskInk.Core.Protocol;
using DeskInk.Core.Transport;
using DeskInk.Overlay;

namespace DeskInk.WindowsHost;

public sealed class DeskInkHost : IAsyncDisposable
{
    private readonly TcpListener _controlListener = new(IPAddress.Loopback, DeskInkConstants.ControlPort);
    private readonly TcpListener _inputListener = new(IPAddress.Loopback, DeskInkConstants.InputPort);
    private readonly TcpListener? _lanControlListener;
    private readonly UdpClient? _lanInputListener;
    private readonly UdpClient? _lanDiscoveryListener;
    private readonly LanPairingIdentity? _lanIdentity;
    private readonly SemaphoreSlim _lanPairingGate = new(1, 1);
    private readonly SessionRegistry _sessions = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly MouseInputController? _mouseInput;
    private readonly SyntheticPenController? _penInput;
    private readonly OverlayWindowController? _overlay;
    private readonly KeyboardShortcutController? _shortcutInput;
    private readonly byte _monitorCount;
    private readonly byte _initialMonitorIndex;
    private readonly DeskInkOutputMode _initialOutputMode;
    private long _usbControlSession;
    private long _usbInputSession;
    private long _lanControlSession;
    private long _statusFrames;
    private long _statusSamples;
    private long _lastInputTimestamp;
    private int _statusTransport;
    private int _statusMonitorIndex;
    private int _statusOutputMode;

    public DeskInkHost(
        MouseInputController? mouseInput = null,
        SyntheticPenController? penInput = null,
        OverlayWindowController? overlay = null,
        KeyboardShortcutController? shortcutInput = null,
        bool enableLan = false)
    {
        _mouseInput = mouseInput;
        _penInput = penInput;
        _overlay = overlay;
        _shortcutInput = shortcutInput;
        _monitorCount = checked((byte)(mouseInput?.MonitorCount ?? penInput?.MonitorCount ?? overlay?.MonitorCount ?? 1));
        _initialMonitorIndex = checked((byte)(mouseInput?.MonitorIndex ?? penInput?.MonitorIndex ?? overlay?.MonitorIndex ?? 0));
        _initialOutputMode = mouseInput is not null
            ? DeskInkOutputMode.Mouse
            : penInput is not null
                ? DeskInkOutputMode.SyntheticPen
                : DeskInkOutputMode.Overlay;
        _statusMonitorIndex = _initialMonitorIndex;
        _statusOutputMode = (int)_initialOutputMode;
        if (_initialOutputMode != DeskInkOutputMode.Overlay) _overlay?.SetOverlayVisible(false);
        if (enableLan)
        {
            _lanControlListener = new TcpListener(IPAddress.Any, DeskInkConstants.LanControlPort);
            _lanInputListener = new UdpClient(new IPEndPoint(IPAddress.Any, DeskInkConstants.LanInputPort));
            _lanDiscoveryListener = new UdpClient(new IPEndPoint(IPAddress.Any, DeskInkConstants.LanDiscoveryPort));
            _lanIdentity = LanHostIdentityStore.LoadOrCreate();
        }
    }

    public HostStatusSnapshot GetStatusSnapshot()
    {
        var transport = (HostTransportKind)Volatile.Read(ref _statusTransport);
        var usbControl = unchecked((ulong)Interlocked.Read(ref _usbControlSession));
        var usbInput = unchecked((ulong)Interlocked.Read(ref _usbInputSession));
        var lanControl = unchecked((ulong)Interlocked.Read(ref _lanControlSession));
        var sessionId = transport switch
        {
            HostTransportKind.UsbAdb when usbInput != 0 => usbInput,
            HostTransportKind.Lan when lanControl != 0 => lanControl,
            _ when usbInput != 0 => usbInput,
            _ when lanControl != 0 => lanControl,
            _ => 0UL,
        };
        var lastInput = Interlocked.Read(ref _lastInputTimestamp);
        return new HostStatusSnapshot(
            transport == HostTransportKind.UsbAdb ? usbControl != 0 : lanControl != 0,
            transport == HostTransportKind.UsbAdb ? usbInput != 0 : lanControl != 0,
            transport,
            sessionId == 0 ? null : sessionId,
            checked((byte)Volatile.Read(ref _statusMonitorIndex)),
            (DeskInkOutputMode)Volatile.Read(ref _statusOutputMode),
            Interlocked.Read(ref _statusFrames),
            Interlocked.Read(ref _statusSamples),
            lastInput == 0 ? null : Stopwatch.GetElapsedTime(lastInput).TotalMilliseconds);
    }

    private void SetSession(ref long target, ulong sessionId) =>
        Interlocked.Exchange(ref target, unchecked((long)sessionId));

    private static void ClearSession(ref long target, ulong sessionId) =>
        Interlocked.CompareExchange(ref target, 0, unchecked((long)sessionId));

    private void RecordInputActivity(string transport, int samples)
    {
        Interlocked.Increment(ref _statusFrames);
        Interlocked.Add(ref _statusSamples, samples);
        Interlocked.Exchange(ref _lastInputTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(
            ref _statusTransport,
            (int)(transport == "USB" ? HostTransportKind.UsbAdb : HostTransportKind.Lan));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        _controlListener.Start();
        _inputListener.Start();
        Console.WriteLine($"Control listener: 127.0.0.1:{DeskInkConstants.ControlPort}");
        Console.WriteLine($"Input listener:   127.0.0.1:{DeskInkConstants.InputPort}");

        var workers = new List<Task>
        {
            AcceptControlClientsAsync(linked.Token),
            AcceptInputClientsAsync(linked.Token),
        };
        if (_lanControlListener is not null && _lanInputListener is not null &&
            _lanDiscoveryListener is not null && _lanIdentity is not null)
        {
            _lanControlListener.Start();
            Console.WriteLine($"LAN pairing:      0.0.0.0:{DeskInkConstants.LanControlPort}/TCP TLS");
            Console.WriteLine($"LAN input:        0.0.0.0:{DeskInkConstants.LanInputPort}/UDP authenticated");
            Console.WriteLine($"LAN discovery:    0.0.0.0:{DeskInkConstants.LanDiscoveryPort}/UDP");
            Console.WriteLine($"LAN PAIR CODE:    {_lanIdentity.ComparisonCode}");
            Console.WriteLine($"LAN FINGERPRINT:  {_lanIdentity.CertificateFingerprint}");
            workers.Add(AcceptLanControlClientsAsync(linked.Token));
            workers.Add(ReceiveLanInputAsync(linked.Token));
            workers.Add(ReceiveLanDiscoveryAsync(linked.Token));
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Expected shutdown path.
        }
    }

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _controlListener.Stop();
        _inputListener.Stop();
        _lanControlListener?.Stop();
        _lanInputListener?.Dispose();
        _lanDiscoveryListener?.Dispose();
        _lanIdentity?.Dispose();
        _lanPairingGate.Dispose();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task AcceptControlClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await _controlListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _ = HandleControlClientAsync(client, cancellationToken);
        }
    }

    private async Task AcceptInputClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await _inputListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _ = HandleInputClientAsync(client, cancellationToken);
        }
    }

    private async Task AcceptLanControlClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await _lanControlListener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _ = HandleLanControlClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleLanControlClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var ownedClient = client;
        if (!await _lanPairingGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            Console.Error.WriteLine("LAN PAIR REJECTED: another pairing/control connection is active");
            return;
        }

        HostSession? session = null;
        try
        {
            client.NoDelay = true;
            using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = _lanIdentity!.Certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
                cancellationToken).ConfigureAwait(false);

            var fingerprint = Convert.FromHexString(_lanIdentity.CertificateFingerprint);
            await FramedStream.WriteAsync(
                tls,
                ControlProtocolV1.EncodeLanPairChallenge(fingerprint),
                cancellationToken).ConfigureAwait(false);
            var confirmFrame = await FramedStream.ReadAsync(
                tls,
                DeskInkConstants.MaximumControlFrameBytes,
                cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("LAN pairing ended before confirmation");
            var confirmedFingerprint = ControlProtocolV1.DecodeLanPairConfirm(confirmFrame);
            if (!CryptographicOperations.FixedTimeEquals(fingerprint, confirmedFingerprint))
                throw new ProtocolException("LAN certificate confirmation mismatch");

            var secret = _lanIdentity.CreateSessionSecret();
            session = _sessions.CreateLan(secret, _initialMonitorIndex, _initialOutputMode);
            SetSession(ref _lanControlSession, session.SessionId);
            Volatile.Write(ref _statusTransport, (int)HostTransportKind.Lan);
            var ack = ControlProtocolV1.EncodeLanPairAck(new LanPairAckMessage(
                session.SessionId,
                secret,
                _monitorCount,
                _initialMonitorIndex,
                DeskInkConstants.LanInputPort));
            await FramedStream.WriteAsync(tls, ack, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(
                $"LAN CONTROL READY session={session.SessionId:X16} peer={client.Client.RemoteEndPoint}");

            while (await FramedStream.ReadAsync(
                tls,
                DeskInkConstants.MaximumControlFrameBytes,
                cancellationToken).ConfigureAwait(false) is { } controlFrame)
            {
                if (ApplyControlFrame(session, controlFrame) is { } response)
                    await FramedStream.WriteAsync(tls, response, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"LAN CONTROL ERROR {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (session is not null)
            {
                _sessions.Remove(session.SessionId);
                ClearSession(ref _lanControlSession, session.SessionId);
                CryptographicOperations.ZeroMemory(session.LanSessionSecret!);
                Console.WriteLine($"LAN CONTROL CLOSED session={session.SessionId:X16}");
            }
            _lanPairingGate.Release();
        }
    }

    private async Task ReceiveLanInputAsync(CancellationToken cancellationToken)
    {
        const int watchdogMilliseconds = 750;
        var pipeline = new InputPipeline(this, "LAN");
        HostSession? activeSession = null;
        IPEndPoint? activePeer = null;
        var receivedSinceRelease = false;
        var awaitingNeutral = false;
        long rejected = 0;
        var rejectionClock = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult datagram;
                using (var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    receiveTimeout.CancelAfter(watchdogMilliseconds);
                    try
                    {
                        datagram = await _lanInputListener!.ReceiveAsync(receiveTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        if (receivedSinceRelease)
                        {
                            pipeline.ReleaseAll();
                            receivedSinceRelease = false;
                            awaitingNeutral = true;
                            Console.Error.WriteLine("LAN INPUT WATCHDOG: outputs released; waiting for a neutral sample");
                        }
                        continue;
                    }
                    catch (SocketException exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        Console.Error.WriteLine(
                            $"LAN INPUT SOCKET ERROR {exception.SocketErrorCode}: {exception.Message}; retrying");
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var session = _sessions.GetCurrentLan();
                if (session is null) continue;
                if (!ReferenceEquals(activeSession, session))
                {
                    pipeline.ReleaseAll();
                    pipeline = new InputPipeline(this, "LAN");
                    activeSession = session;
                    activePeer = null;
                    receivedSinceRelease = false;
                    awaitingNeutral = false;
                }

                try
                {
                    var decodeStarted = Stopwatch.GetTimestamp();
                    var frameBytes = LanAuthenticatedDatagram.Decode(
                        datagram.Buffer,
                        session.LanSessionSecret!);
                    var frame = ProtocolV1Codec.DecodeInputBatch(frameBytes);
                    var decodeUs = ElapsedMicroseconds(decodeStarted);
                    if (frame.SessionId != session.SessionId)
                        throw new ProtocolException("LAN input session mismatch");
                    if (activePeer is not null && !activePeer.Equals(datagram.RemoteEndPoint))
                        throw new ProtocolException("LAN input endpoint changed during the session");
                    activePeer ??= datagram.RemoteEndPoint;

                    if (awaitingNeutral)
                    {
                        if (!IsNeutralFrame(frame)) continue;
                        awaitingNeutral = false;
                    }
                    pipeline.Apply(session, frame, decodeUs);
                    receivedSinceRelease = true;
                }
                catch (ProtocolException)
                {
                    rejected++;
                    if (rejectionClock.ElapsedMilliseconds >= 1000)
                    {
                        Console.Error.WriteLine($"LAN INPUT REJECTED count={rejected}");
                        rejectionClock.Restart();
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine(
                        $"LAN INPUT PROCESSING ERROR {exception.GetType().Name}: {exception.Message}; " +
                        "outputs released and pipeline reset");
                    pipeline.ReleaseAll();
                    pipeline = new InputPipeline(this, "LAN");
                    activePeer = null;
                    receivedSinceRelease = false;
                    awaitingNeutral = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            pipeline.ReleaseAll();
            Console.WriteLine($"LAN INPUT CLOSED {pipeline.Summary} rejected={rejected}");
        }
    }

    private async Task ReceiveLanDiscoveryAsync(CancellationToken cancellationToken)
    {
        var fingerprint = Convert.FromHexString(_lanIdentity!.CertificateFingerprint);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var request = await _lanDiscoveryListener!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var nonce = LanDiscoveryProtocol.DecodeQuery(request.Buffer);
                var reply = LanDiscoveryProtocol.EncodeReply(new LanDiscoveryReply(
                    nonce,
                    DeskInkConstants.LanControlPort,
                    fingerprint,
                    Environment.MachineName));
                await _lanDiscoveryListener.SendAsync(reply, request.RemoteEndPoint, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ProtocolException)
            {
                // Discovery is unauthenticated; silently ignore malformed broadcast traffic.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static bool IsNeutralFrame(InputBatchFrame frame) =>
        frame.Samples.All(sample => !sample.State.HasFlag(PenSampleState.Contact));

    private async Task HandleControlClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var ownedClient = client;
        client.NoDelay = true;
        HostSession? session = null;
        try
        {
            var stream = client.GetStream();
            var helloFrame = await FramedStream.ReadAsync(
                stream,
                DeskInkConstants.MaximumControlFrameBytes,
                cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Control connection ended before HELLO");
            var hello = ControlProtocolV1.DecodeHello(helloFrame);
            session = _sessions.Create(hello.ClientNonce, _initialMonitorIndex, _initialOutputMode);
            SetSession(ref _usbControlSession, session.SessionId);
            Volatile.Write(ref _statusTransport, (int)HostTransportKind.UsbAdb);
            var ack = ControlProtocolV1.EncodeHelloAck(
                new HelloAckMessage(
                    session.SessionId,
                    session.InputBindToken,
                    _monitorCount,
                    _initialMonitorIndex));
            await FramedStream.WriteAsync(stream, ack, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"CONTROL READY session={session.SessionId:X16}");

            while (await FramedStream.ReadAsync(
                stream,
                DeskInkConstants.MaximumControlFrameBytes,
                cancellationToken).ConfigureAwait(false) is { } controlFrame)
            {
                if (ApplyControlFrame(session, controlFrame) is { } response)
                    await FramedStream.WriteAsync(stream, response, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CONTROL ERROR {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (session is not null)
            {
                _sessions.Remove(session.SessionId);
                ClearSession(ref _usbControlSession, session.SessionId);
                Console.WriteLine($"CONTROL CLOSED session={session.SessionId:X16}");
            }
        }
    }

    private async Task HandleInputClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        const int watchdogMilliseconds = 750;
        using var ownedClient = client;
        client.NoDelay = true;
        HostSession? session = null;
        var pipeline = new InputPipeline(this, "USB");
        var awaitingNeutral = false;

        try
        {
            var stream = client.GetStream();
            var bindFrame = await FramedStream.ReadAsync(
                stream,
                DeskInkConstants.MaximumControlFrameBytes,
                cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Input connection ended before INPUT_BIND");
            var bind = ControlProtocolV1.DecodeInputBind(bindFrame);
            session = _sessions.ValidateBind(bind);
            SetSession(ref _usbInputSession, session.SessionId);
            Volatile.Write(ref _statusTransport, (int)HostTransportKind.UsbAdb);
            Console.WriteLine($"INPUT READY session={session.SessionId:X16}");

            using var watchdog = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            var watchdogTick = watchdog.WaitForNextTickAsync(cancellationToken).AsTask();
            var lastInputTimestamp = Stopwatch.GetTimestamp();
            while (true)
            {
                var readTask = FramedStream.ReadAsync(
                    stream,
                    DeskInkConstants.MaximumInputFrameBytes,
                    cancellationToken).AsTask();
                while (!readTask.IsCompleted)
                {
                    if (await Task.WhenAny(readTask, watchdogTick).ConfigureAwait(false) == readTask) break;
                    if (await watchdogTick.ConfigureAwait(false) &&
                        pipeline.HasActiveContact &&
                        Stopwatch.GetElapsedTime(lastInputTimestamp).TotalMilliseconds >= watchdogMilliseconds)
                    {
                        pipeline.ReleaseAll();
                        awaitingNeutral = true;
                        Console.Error.WriteLine(
                            "USB INPUT WATCHDOG: outputs released; waiting for a neutral sample");
                    }
                    watchdogTick = watchdog.WaitForNextTickAsync(cancellationToken).AsTask();
                }
                var frameBytes = await readTask.ConfigureAwait(false);
                if (frameBytes is null) break;
                lastInputTimestamp = Stopwatch.GetTimestamp();
                var decodeStarted = Stopwatch.GetTimestamp();
                var frame = ProtocolV1Codec.DecodeInputBatch(frameBytes);
                var decodeUs = ElapsedMicroseconds(decodeStarted);
                if (frame.SessionId != session.SessionId) throw new ProtocolException("Input session mismatch");
                if (awaitingNeutral)
                {
                    if (!IsNeutralFrame(frame)) continue;
                    awaitingNeutral = false;
                }
                pipeline.Apply(session, frame, decodeUs);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"INPUT ERROR {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (session is not null) ClearSession(ref _usbInputSession, session.SessionId);
            var release = pipeline.ReleaseAll();
            Console.WriteLine(
                $"INPUT CLOSED session={(session is null ? "none" : session.SessionId.ToString("X16"))} " +
                $"{pipeline.Summary} release={release}");
        }
    }

    private byte[]? ApplyControlFrame(HostSession session, ReadOnlySpan<byte> controlFrame)
    {
        switch (ControlProtocolV1.PeekType(controlFrame))
        {
            case ControlMessageType.Ping:
                var hostReceiveUs = MonotonicTimeUs();
                var ping = ControlProtocolV1.DecodePing(controlFrame);
                EnsureSession(ping.SessionId, session.SessionId);
                return ControlProtocolV1.EncodePong(new PongMessage(
                    session.SessionId,
                    ping.ClientTimeUs,
                    hostReceiveUs,
                    MonotonicTimeUs()));
            case ControlMessageType.SetClockSync:
                var clock = ControlProtocolV1.DecodeSetClockSync(controlFrame);
                EnsureSession(clock.SessionId, session.SessionId);
                session.SetClockSync(clock.HostMinusClientUs, clock.UncertaintyUs);
                Console.WriteLine(
                    $"CLOCK SYNC session={session.SessionId:X16} offsetUs={clock.HostMinusClientUs} " +
                    $"uncertaintyUs={clock.UncertaintyUs}");
                break;
            case ControlMessageType.SetMouseMode:
                var mode = ControlProtocolV1.DecodeSetMouseMode(controlFrame);
                EnsureSession(mode.SessionId, session.SessionId);
                session.MouseMode = mode.Mode;
                Console.WriteLine($"MOUSE MODE session={session.SessionId:X16} mode={mode.Mode}");
                break;
            case ControlMessageType.SetMonitor:
                var monitor = ControlProtocolV1.DecodeSetMonitor(controlFrame);
                EnsureSession(monitor.SessionId, session.SessionId);
                if (monitor.MonitorIndex >= _monitorCount)
                    throw new ProtocolException($"Monitor index {monitor.MonitorIndex} is unavailable");
                session.MonitorIndex = monitor.MonitorIndex;
                Volatile.Write(ref _statusMonitorIndex, monitor.MonitorIndex);
                Console.WriteLine($"MONITOR REQUEST session={session.SessionId:X16} index={monitor.MonitorIndex}");
                break;
            case ControlMessageType.SetOverlayTool:
                var tool = ControlProtocolV1.DecodeSetOverlayTool(controlFrame);
                EnsureSession(tool.SessionId, session.SessionId);
                session.OverlayTool = tool.Tool;
                Console.WriteLine($"OVERLAY TOOL session={session.SessionId:X16} tool={tool.Tool}");
                break;
            case ControlMessageType.OverlayCommand:
                var command = ControlProtocolV1.DecodeOverlayCommand(controlFrame);
                EnsureSession(command.SessionId, session.SessionId);
                switch (command.Command)
                {
                    case OverlayControlCommand.Undo: _overlay?.Undo(); break;
                    case OverlayControlCommand.Redo: _overlay?.Redo(); break;
                    case OverlayControlCommand.Clear: _overlay?.Clear(); break;
                }
                break;
            case ControlMessageType.SetOverlayMode:
                var overlayMode = ControlProtocolV1.DecodeSetOverlayMode(controlFrame);
                EnsureSession(overlayMode.SessionId, session.SessionId);
                session.OverlayMode = overlayMode.Mode;
                _overlay?.SetOverlayVisible(
                    session.OutputMode == DeskInkOutputMode.Overlay && overlayMode.Mode != OverlayMode.Hidden);
                _overlay?.SetClickThrough(overlayMode.Mode != OverlayMode.Interactive);
                break;
            case ControlMessageType.SetOutputMode:
                var output = ControlProtocolV1.DecodeSetOutputMode(controlFrame);
                EnsureSession(output.SessionId, session.SessionId);
                if ((output.Mode == DeskInkOutputMode.Mouse && _mouseInput is null) ||
                    (output.Mode == DeskInkOutputMode.SyntheticPen && _penInput is null) ||
                    (output.Mode == DeskInkOutputMode.Overlay && _overlay is null))
                    throw new ProtocolException($"Output {output.Mode} is unavailable");
                session.OutputMode = output.Mode;
                Volatile.Write(ref _statusOutputMode, (int)output.Mode);
                break;
            case ControlMessageType.ExecuteShortcut:
                var shortcut = ControlProtocolV1.DecodeExecuteShortcut(controlFrame);
                EnsureSession(shortcut.SessionId, session.SessionId);
                if (_shortcutInput is null)
                    throw new ProtocolException("Shortcuts are unavailable");
                try
                {
                    _shortcutInput.Execute(shortcut.Command);
                    Console.WriteLine($"SHORTCUT session={session.SessionId:X16} command={shortcut.Command}");
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    Console.Error.WriteLine(
                        $"SHORTCUT ERROR session={session.SessionId:X16} command={shortcut.Command} " +
                        $"native={exception.NativeErrorCode}: {exception.Message}");
                }
                break;
            default:
                throw new ProtocolException("Unsupported control message");
        }
        return null;
    }

    private static string FormatRange(int? minimum, int? maximum) =>
        minimum is null || maximum is null ? "n/a" : $"{minimum}..{maximum}";

    private static void EnsureSession(ulong received, ulong expected)
    {
        if (received != expected) throw new ProtocolException("Control session mismatch");
    }

    private static long MonotonicTimeUs() =>
        (long)(Stopwatch.GetTimestamp() * (1_000_000d / Stopwatch.Frequency));

    private static long ElapsedMicroseconds(long startedTimestamp) =>
        (long)((Stopwatch.GetTimestamp() - startedTimestamp) * (1_000_000d / Stopwatch.Frequency));

    private sealed class InputPipeline
    {
        private readonly DeskInkHost _host;
        private readonly string _transport;
        private readonly PenStateMachine _stateMachine = new();
        private readonly MouseInputTranslator? _mouseTranslator;
        private readonly PenInputTranslator? _penTranslator;
        private readonly OverlayInputTranslator? _overlayTranslator;
        private readonly SequenceTracker _sequences = new();
        private readonly Stopwatch _reportClock = Stopwatch.StartNew();
        private long _frames;
        private long _samples;
        private long _historical;
        private long _gaps;
        private long _duplicates;
        private long _reordered;
        private int? _pressureMin, _pressureMax;
        private int? _distanceMin, _distanceMax;
        private int? _tiltMin, _tiltMax;
        private int? _orientationMin, _orientationMax;
        private byte _appliedMonitorIndex;
        private DeskInkOutputMode _appliedOutputMode;
        private readonly long[] _latencyUs = new long[4096];
        private int _latencyCount;
        private int _latencyNext;
        private readonly TimingWindow _decodeTiming = new(4096);
        private readonly TimingWindow _dispatchTiming = new(4096);
        private readonly Process _process = Process.GetCurrentProcess();
        private TimeSpan _lastCpuTime;

        public InputPipeline(DeskInkHost host, string transport)
        {
            _host = host;
            _transport = transport;
            _mouseTranslator = host._mouseInput is null ? null : new MouseInputTranslator();
            _penTranslator = host._penInput is null ? null : new PenInputTranslator();
            _overlayTranslator = host._overlay is null ? null : new OverlayInputTranslator();
            _appliedMonitorIndex = host._initialMonitorIndex;
            _appliedOutputMode = host._initialOutputMode;
            _lastCpuTime = _process.TotalProcessorTime;
        }

        public string Summary =>
            $"frames={_frames} samples={_samples} gaps={_gaps} dup={_duplicates} reorder={_reordered}";

        public bool HasActiveContact => _stateMachine.State == PenLifecycleState.Contact;

        public void Apply(HostSession session, InputBatchFrame frame, long decodeUs)
        {
            if (!_host._sessions.IsCurrent(session)) throw new ProtocolException("Input session is no longer active");
            var sequence = _sequences.Observe(frame.Sequence);
            switch (sequence.Disposition)
            {
                case SequenceDisposition.Duplicate:
                    _duplicates++;
                    return;
                case SequenceDisposition.ReorderedOrStale:
                    _reordered++;
                    return;
                case SequenceDisposition.AcceptedWithGap:
                    _gaps += sequence.Gap;
                    break;
            }

            _frames++;
            _host.RecordInputActivity(_transport, frame.Samples.Count);
            _decodeTiming.Record(decodeUs);
            if (session.TryGetClockSync(out var clockOffsetUs, out _) && frame.Samples.Count > 0)
            {
                var freshestClientUs = checked((long)frame.BaseMonotonicTimeUs + frame.Samples[^1].DeltaTimeUs);
                RecordLatency(MonotonicTimeUs() - (freshestClientUs + clockOffsetUs));
            }
            foreach (var sample in frame.Samples)
            {
                if (frame.ToolKind is ToolKind.Stylus or ToolKind.Eraser) _stateMachine.Apply(sample);
                _samples++;
                if (sample.State.HasFlag(PenSampleState.Historical)) _historical++;
                UpdateRange(sample, PenSampleState.PressureValid, sample.PressureNormalized,
                    ref _pressureMin, ref _pressureMax);
                UpdateRange(sample, PenSampleState.DistanceValid, sample.DistanceNormalized,
                    ref _distanceMin, ref _distanceMax);
                UpdateRange(sample, PenSampleState.TiltValid, sample.TiltCentidegrees,
                    ref _tiltMin, ref _tiltMax);
                UpdateRange(sample, PenSampleState.OrientationValid, sample.OrientationCentidegrees,
                    ref _orientationMin, ref _orientationMax);
            }

            if (session.MonitorIndex != _appliedMonitorIndex)
            {
                ReleaseOutputs();
                _host._mouseInput?.SetMonitor(session.MonitorIndex);
                _host._penInput?.SetMonitor(session.MonitorIndex);
                _host._overlay?.SetMonitor(session.MonitorIndex);
                _appliedMonitorIndex = session.MonitorIndex;
                Console.WriteLine($"MONITOR ACTIVE session={session.SessionId:X16} index={_appliedMonitorIndex}");
            }
            if (session.OutputMode != _appliedOutputMode)
            {
                ReleaseOutputs();
                _appliedOutputMode = session.OutputMode;
                _host._overlay?.SetOverlayVisible(
                    _appliedOutputMode == DeskInkOutputMode.Overlay && session.OverlayMode != OverlayMode.Hidden);
                Console.WriteLine($"OUTPUT ACTIVE session={session.SessionId:X16} mode={_appliedOutputMode}");
            }

            var dispatchStarted = Stopwatch.GetTimestamp();
            if (_appliedOutputMode == DeskInkOutputMode.Mouse && _mouseTranslator is not null)
            {
                _mouseTranslator.SetMode(session.MouseMode, _host._mouseInput!.Apply);
                _mouseTranslator.Apply(frame, _host._mouseInput.Apply);
            }
            if (_appliedOutputMode == DeskInkOutputMode.SyntheticPen)
                _penTranslator?.Apply(frame, _host._penInput!.Apply);
            if (_appliedOutputMode == DeskInkOutputMode.Overlay)
                _overlayTranslator?.Apply(frame, session.OverlayTool, _host._overlay!.Apply);
            _dispatchTiming.Record(ElapsedMicroseconds(dispatchStarted));

            if (_reportClock.ElapsedMilliseconds >= 1000)
            {
                var elapsedSeconds = _reportClock.Elapsed.TotalSeconds;
                var cpuNow = _process.TotalProcessorTime;
                var cpuPercent = (cpuNow - _lastCpuTime).TotalSeconds /
                    elapsedSeconds / Environment.ProcessorCount * 100;
                _lastCpuTime = cpuNow;
                var latency = LatencyPercentiles();
                session.TryGetClockSync(out _, out var uncertaintyUs);
                Console.WriteLine(
                    $"INPUT {_transport} frames={_frames} samples={_samples} historical={_historical} " +
                    $"gaps={_gaps} dup={_duplicates} reorder={_reordered} " +
                    $"state={_stateMachine.State} lastSeq={frame.Sequence} " +
                    $"pressure={FormatRange(_pressureMin, _pressureMax)} " +
                    $"tilt={FormatRange(_tiltMin, _tiltMax)} " +
                    $"orientation={FormatRange(_orientationMin, _orientationMax)} " +
                    $"distance={FormatRange(_distanceMin, _distanceMax)} " +
                    $"latencyUs={latency} uncertaintyUs={uncertaintyUs} " +
                    $"decodeUs={_decodeTiming.Percentiles()} dispatchUs={_dispatchTiming.Percentiles()} " +
                    $"cpu={cpuPercent:F1}% memoryMiB={_process.WorkingSet64 / 1024d / 1024d:F1} " +
                    $"mode={session.MouseMode} mouse={_host._mouseInput?.MetricsText ?? "off"} " +
                    $"pen={_host._penInput?.MetricsText ?? "off"} " +
                    $"overlay={_host._overlay?.MetricsText ?? "off"}");
                _reportClock.Restart();
            }
        }

        private void RecordLatency(long latencyUs)
        {
            if (latencyUs < -1_000_000 || latencyUs > 10_000_000) return;
            _latencyUs[_latencyNext] = Math.Max(0, latencyUs);
            _latencyNext = (_latencyNext + 1) % _latencyUs.Length;
            _latencyCount = Math.Min(_latencyCount + 1, _latencyUs.Length);
        }

        private string LatencyPercentiles()
        {
            if (_latencyCount == 0) return "n/a";
            var snapshot = _latencyUs.AsSpan(0, _latencyCount).ToArray();
            Array.Sort(snapshot);
            static long At(long[] values, double percentile) =>
                values[Math.Clamp((int)Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1)];
            return $"p50:{At(snapshot, 0.50)},p95:{At(snapshot, 0.95)},p99:{At(snapshot, 0.99)}";
        }

        private sealed class TimingWindow(int capacity)
        {
            private readonly long[] _values = new long[capacity];
            private int _count;
            private int _next;

            public void Record(long durationUs)
            {
                _values[_next] = Math.Max(0, durationUs);
                _next = (_next + 1) % _values.Length;
                _count = Math.Min(_count + 1, _values.Length);
            }

            public string Percentiles()
            {
                if (_count == 0) return "n/a";
                var snapshot = _values.AsSpan(0, _count).ToArray();
                Array.Sort(snapshot);
                long At(double percentile) => snapshot[
                    Math.Clamp((int)Math.Ceiling(percentile * snapshot.Length) - 1, 0, snapshot.Length - 1)];
                return $"p50:{At(0.50)},p95:{At(0.95)},p99:{At(0.99)}";
            }
        }

        public PenTransition ReleaseAll()
        {
            var release = _stateMachine.ReleaseAll();
            ReleaseOutputs();
            return release;
        }

        private void ReleaseOutputs()
        {
            try { _mouseTranslator?.ReleaseAll(_host._mouseInput!.Apply); }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"MOUSE RELEASE ERROR {exception.GetType().Name}: {exception.Message}");
            }
            try { _penTranslator?.ReleaseAll(_host._penInput!.Apply); }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"PEN RELEASE ERROR {exception.GetType().Name}: {exception.Message}");
            }
            if (_overlayTranslator is not null) _overlayTranslator.ReleaseAll(_host._overlay!.Apply);
        }

        private static void UpdateRange(
            PenSample sample,
            PenSampleState flag,
            int value,
            ref int? minimum,
            ref int? maximum)
        {
            if (!sample.State.HasFlag(flag)) return;
            minimum = Math.Min(minimum ?? value, value);
            maximum = Math.Max(maximum ?? value, value);
        }
    }

    private sealed class HostSession(
        ulong sessionId,
        ulong clientNonce,
        byte[] inputBindToken)
    {
        private int _mouseMode;
        private int _monitorIndex;
        private int _overlayTool;
        private int _overlayMode = (int)OverlayMode.ClickThrough;
        private int _outputMode;
        private long _clockOffsetUs;
        private long _clockUncertaintyUs;
        private int _clockSynchronized;

        public ulong SessionId { get; } = sessionId;
        public ulong ClientNonce { get; } = clientNonce;
        public byte[] InputBindToken { get; } = inputBindToken;
        public byte[]? LanSessionSecret { get; private set; }
        public MouseMode MouseMode
        {
            get => (MouseMode)Volatile.Read(ref _mouseMode);
            set => Volatile.Write(ref _mouseMode, (int)value);
        }
        public byte MonitorIndex
        {
            get => checked((byte)Volatile.Read(ref _monitorIndex));
            set => Volatile.Write(ref _monitorIndex, value);
        }
        public OverlayTool OverlayTool
        {
            get => (OverlayTool)Volatile.Read(ref _overlayTool);
            set => Volatile.Write(ref _overlayTool, (int)value);
        }
        public OverlayMode OverlayMode
        {
            get => (OverlayMode)Volatile.Read(ref _overlayMode);
            set => Volatile.Write(ref _overlayMode, (int)value);
        }
        public DeskInkOutputMode OutputMode
        {
            get => (DeskInkOutputMode)Volatile.Read(ref _outputMode);
            set => Volatile.Write(ref _outputMode, (int)value);
        }

        public void SetInitialMonitor(byte monitorIndex) => _monitorIndex = monitorIndex;
        public void SetInitialOutput(DeskInkOutputMode outputMode) => _outputMode = (int)outputMode;
        public void SetLanSessionSecret(byte[] sessionSecret) => LanSessionSecret = sessionSecret;
        public void SetClockSync(long hostMinusClientUs, uint uncertaintyUs)
        {
            Interlocked.Exchange(ref _clockOffsetUs, hostMinusClientUs);
            Interlocked.Exchange(ref _clockUncertaintyUs, uncertaintyUs);
            Volatile.Write(ref _clockSynchronized, 1);
        }
        public bool TryGetClockSync(out long hostMinusClientUs, out long uncertaintyUs)
        {
            if (Volatile.Read(ref _clockSynchronized) == 0)
            {
                hostMinusClientUs = 0;
                uncertaintyUs = 0;
                return false;
            }
            hostMinusClientUs = Interlocked.Read(ref _clockOffsetUs);
            uncertaintyUs = Interlocked.Read(ref _clockUncertaintyUs);
            return true;
        }
    }

    private sealed class SessionRegistry
    {
        private readonly object _gate = new();
        private HostSession? _current;

        public HostSession Create(
            ulong clientNonce,
            byte monitorIndex,
            DeskInkOutputMode outputMode)
        {
            Span<byte> sessionBytes = stackalloc byte[sizeof(ulong)];
            RandomNumberGenerator.Fill(sessionBytes);
            var sessionId = BitConverter.ToUInt64(sessionBytes);
            if (sessionId == 0) sessionId = 1;
            var session = new HostSession(
                sessionId,
                clientNonce,
                RandomNumberGenerator.GetBytes(ControlProtocolV1.BindTokenBytes));
            session.SetInitialMonitor(monitorIndex);
            session.SetInitialOutput(outputMode);
            lock (_gate) _current = session;
            return session;
        }

        public HostSession ValidateBind(InputBindMessage bind)
        {
            lock (_gate)
            {
                if (_current is null || _current.SessionId != bind.SessionId)
                {
                    throw new ProtocolException("Unknown input session");
                }
                if (!CryptographicOperations.FixedTimeEquals(_current.InputBindToken, bind.InputBindToken))
                {
                    throw new ProtocolException("Invalid input bind token");
                }
                return _current;
            }
        }

        public HostSession CreateLan(
            byte[] sessionSecret,
            byte monitorIndex,
            DeskInkOutputMode outputMode)
        {
            var session = Create(0, monitorIndex, outputMode);
            session.SetLanSessionSecret(sessionSecret);
            return session;
        }

        public HostSession? GetCurrentLan()
        {
            lock (_gate) return _current?.LanSessionSecret is null ? null : _current;
        }

        public bool IsCurrent(HostSession session)
        {
            lock (_gate) return ReferenceEquals(_current, session);
        }

        public void Remove(ulong sessionId)
        {
            lock (_gate)
            {
                if (_current?.SessionId == sessionId) _current = null;
            }
        }
    }
}
