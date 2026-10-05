using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DeskInk.Core;
using DeskInk.Core.Protocol;
using DeskInk.Core.Transport;

return await InputSimulator.RunAsync(args);

internal static class InputSimulator
{
    private const int DefaultMoveCount = 1000;
    private const uint SampleIntervalUs = 1000;
    private const ushort PointerId = 1;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = SimulatorOptions.Parse(args);
            using var timeout = new CancellationTokenSource(options.Timeout);
            var cancellationToken = timeout.Token;

            using var controlClient = new TcpClient { NoDelay = true };
            await controlClient.ConnectAsync(options.Host, options.ControlPort, cancellationToken);
            var controlStream = controlClient.GetStream();

            var nonce = CreateNonce();
            await FramedStream.WriteAsync(
                controlStream,
                ControlProtocolV1.EncodeHello(new HelloMessage(nonce)),
                cancellationToken);
            var ackFrame = await FramedStream.ReadAsync(
                controlStream,
                DeskInkConstants.MaximumControlFrameBytes,
                cancellationToken) ?? throw new EndOfStreamException("Host closed before HELLO_ACK");
            var ack = ControlProtocolV1.DecodeHelloAck(ackFrame);

            using var inputClient = new TcpClient { NoDelay = true };
            await inputClient.ConnectAsync(options.Host, options.InputPort, cancellationToken);
            var inputStream = inputClient.GetStream();
            var bindToken = ack.InputBindToken.ToArray();
            if (options.Scenario == SimulatorScenario.BadBind) bindToken[0] ^= 1;
            await FramedStream.WriteAsync(
                inputStream,
                ControlProtocolV1.EncodeInputBind(
                    new InputBindMessage(ack.SessionId, bindToken)),
                cancellationToken);

            if (options.Scenario == SimulatorScenario.BadBind)
            {
                await WaitForInputCloseAsync(inputClient, inputStream, cancellationToken);
                Console.WriteLine("SIMULATOR REJECTION PASS scenario=bad-bind");
                return 0;
            }

            var includeUp = options.Scenario != SimulatorScenario.MissingUp;
            var samples = CreateStroke(options.MoveCount, includeUp);
            var startUs = MonotonicTimeUs();
            var frames = CreateFrames(ack.SessionId, samples, startUs);

            if (IsMalformedFrameScenario(options.Scenario))
            {
                var frame = options.Scenario == SimulatorScenario.WrongSession
                    ? frames[0] with { SessionId = ack.SessionId == ulong.MaxValue ? 1 : ack.SessionId + 1 }
                    : frames[0];
                var encoded = ProtocolV1Codec.EncodeInputBatch(frame);
                encoded = options.Scenario switch
                {
                    SimulatorScenario.BadVersion => Mutate(encoded, bytes => bytes[4]++),
                    SimulatorScenario.Truncated => encoded[..10],
                    SimulatorScenario.InvalidSampleCount => Mutate(encoded, bytes => bytes[32] = 0),
                    _ => encoded,
                };
                await FramedStream.WriteAsync(inputStream, encoded, cancellationToken);
                await WaitForInputCloseAsync(inputClient, inputStream, cancellationToken);
                Console.WriteLine($"SIMULATOR REJECTION PASS scenario={ScenarioName(options.Scenario)}");
                return 0;
            }

            if (options.Scenario == SimulatorScenario.Timeout)
            {
                var timeoutFrames = new[]
                {
                    CreateSingleSampleFrame(ack.SessionId, 0, startUs, samples[0]),
                    CreateSingleSampleFrame(ack.SessionId, 1, startUs + 1_000, samples[1]),
                    CreateSingleSampleFrame(ack.SessionId, 2, startUs + 2_000, samples[^1]),
                    CreateSingleSampleFrame(
                        ack.SessionId,
                        3,
                        startUs + 3_000,
                        samples[0] with { StateGeneration = 3 }),
                    CreateSingleSampleFrame(
                        ack.SessionId,
                        4,
                        startUs + 4_000,
                        samples[^1] with { StateGeneration = 4 }),
                };
                await FramedStream.WriteAsync(
                    inputStream,
                    ProtocolV1Codec.EncodeInputBatch(timeoutFrames[0]),
                    cancellationToken);
                await Task.Delay(options.PauseMilliseconds, cancellationToken);
                foreach (var frame in timeoutFrames[1..])
                {
                    await FramedStream.WriteAsync(
                        inputStream,
                        ProtocolV1Codec.EncodeInputBatch(frame),
                        cancellationToken);
                }
                await WaitForInputCloseAsync(inputClient, inputStream, cancellationToken);
                Console.WriteLine(
                    "SIMULATOR PASS scenario=timeout framesSent=5 contactDuringPause=1 neutralRecovery=1");
                return 0;
            }

            var framesToSend = ApplyScenario(frames, options.Scenario);
            foreach (var frame in framesToSend)
            {
                await FramedStream.WriteAsync(
                    inputStream,
                    ProtocolV1Codec.EncodeInputBatch(frame),
                    cancellationToken);
            }

            await WaitForInputCloseAsync(inputClient, inputStream, cancellationToken);

            Console.WriteLine(
                $"SIMULATOR PASS scenario={ScenarioName(options.Scenario)} " +
                $"session={ack.SessionId:X16} framesSent={framesToSend.Count} " +
                $"samplesGenerated={samples.Count} down=1 move={options.MoveCount} up={(includeUp ? 1 : 0)}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SIMULATOR FAIL {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static List<InputBatchFrame> CreateFrames(
        ulong sessionId,
        IReadOnlyList<PenSample> samples,
        ulong startUs)
    {
        var frames = new List<InputBatchFrame>();
        for (var offset = 0; offset < samples.Count; offset += DeskInkConstants.MaximumSamplesPerBatch)
        {
            var count = Math.Min(DeskInkConstants.MaximumSamplesPerBatch, samples.Count - offset);
            var batchSamples = new PenSample[count];
            for (var index = 0; index < count; index++)
            {
                batchSamples[index] = samples[offset + index] with
                {
                    DeltaTimeUs = checked((uint)index * SampleIntervalUs),
                };
            }
            frames.Add(new InputBatchFrame(
                sessionId,
                checked((uint)frames.Count),
                startUs + checked((ulong)offset * SampleIntervalUs),
                ToolKind.Stylus,
                PointerId,
                batchSamples));
        }
        return frames;
    }

    private static InputBatchFrame CreateSingleSampleFrame(
        ulong sessionId,
        uint sequence,
        ulong baseTimeUs,
        PenSample sample) => new(
            sessionId,
            sequence,
            baseTimeUs,
            ToolKind.Stylus,
            PointerId,
            [sample with { DeltaTimeUs = 0 }]);

    private static List<InputBatchFrame> ApplyScenario(
        IReadOnlyList<InputBatchFrame> frames,
        SimulatorScenario scenario)
    {
        var output = new List<InputBatchFrame>(frames.Count + 1);
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = scenario == SimulatorScenario.Gap && index >= frames.Count / 2
                ? frames[index] with { Sequence = checked(frames[index].Sequence + 1) }
                : frames[index];
            output.Add(frame);
            if (scenario == SimulatorScenario.Duplicate && index == frames.Count / 2)
                output.Add(frame);
        }
        if (scenario == SimulatorScenario.Reorder && frames.Count > 1)
            output.Add(frames[^2]);
        return output;
    }

    private static List<PenSample> CreateStroke(int moveCount, bool includeUp)
    {
        var samples = new List<PenSample>(moveCount + 2);
        for (var index = 0; index < moveCount + 1; index++)
        {
            var progress = index / (double)Math.Max(1, moveCount);
            samples.Add(new PenSample(
                0,
                1,
                Normalize(0.1 + 0.8 * progress),
                Normalize(0.2 + 0.6 * progress),
                Normalize(0.25 + 0.5 * progress),
                0,
                0,
                0,
                0,
                PenSampleState.InRange | PenSampleState.Contact | PenSampleState.PressureValid));
        }
        if (includeUp)
        {
            samples.Add(new PenSample(
                0,
                2,
                Normalize(0.9),
                Normalize(0.8),
                0,
                0,
                0,
                0,
                0,
                PenSampleState.InRange | PenSampleState.PressureValid));
        }
        return samples;
    }

    private static ushort Normalize(double value) =>
        checked((ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue));

    private static string ScenarioName(SimulatorScenario scenario) => scenario switch
    {
        SimulatorScenario.MissingUp => "missing-up",
        SimulatorScenario.BadBind => "bad-bind",
        SimulatorScenario.WrongSession => "wrong-session",
        SimulatorScenario.BadVersion => "bad-version",
        SimulatorScenario.InvalidSampleCount => "invalid-sample-count",
        _ => scenario.ToString().ToLowerInvariant(),
    };

    private static bool IsMalformedFrameScenario(SimulatorScenario scenario) => scenario is
        SimulatorScenario.WrongSession or
        SimulatorScenario.BadVersion or
        SimulatorScenario.Truncated or
        SimulatorScenario.InvalidSampleCount;

    private static byte[] Mutate(byte[] encoded, Action<byte[]> mutation)
    {
        mutation(encoded);
        return encoded;
    }

    private static async Task WaitForInputCloseAsync(
        TcpClient inputClient,
        NetworkStream inputStream,
        CancellationToken cancellationToken)
    {
        inputClient.Client.Shutdown(SocketShutdown.Send);
        var eofProbe = new byte[1];
        if (await inputStream.ReadAsync(eofProbe, cancellationToken) != 0)
            throw new ProtocolException("Unexpected data on the input channel");
    }

    private static ulong CreateNonce()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        ulong nonce;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            nonce = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        } while (nonce == 0);
        return nonce;
    }

    private static ulong MonotonicTimeUs() =>
        checked((ulong)(Stopwatch.GetTimestamp() * (1_000_000d / Stopwatch.Frequency)));
}

internal sealed record SimulatorOptions(
    IPAddress Host,
    int ControlPort,
    int InputPort,
    int MoveCount,
    SimulatorScenario Scenario,
    int PauseMilliseconds,
    TimeSpan Timeout)
{
    public static SimulatorOptions Parse(string[] args)
    {
        var host = IPAddress.Loopback;
        var controlPort = DeskInkConstants.ControlPort;
        var inputPort = DeskInkConstants.InputPort;
        var moveCount = 1000;
        var scenario = SimulatorScenario.Normal;
        var pauseMilliseconds = 1200;
        var timeoutSeconds = 10;

        for (var index = 0; index < args.Length; index++)
        {
            string Value() => index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"{args[index]} requires a value");

            switch (args[index].ToLowerInvariant())
            {
                case "--host":
                    if (!IPAddress.TryParse(Value(), out host))
                        throw new ArgumentException("--host requires an IP address");
                    break;
                case "--control-port":
                    controlPort = ParsePort(Value(), "--control-port");
                    break;
                case "--input-port":
                    inputPort = ParsePort(Value(), "--input-port");
                    break;
                case "--moves":
                    if (!int.TryParse(Value(), out moveCount) || moveCount is < 1 or > 1_000_000)
                        throw new ArgumentException("--moves must be between 1 and 1000000");
                    break;
                case "--scenario":
                    if (!Enum.TryParse<SimulatorScenario>(
                            Value().Replace("-", string.Empty, StringComparison.Ordinal),
                            ignoreCase: true,
                            out scenario))
                        throw new ArgumentException(
                            "--scenario is not recognized");
                    break;
                case "--timeout-seconds":
                    if (!int.TryParse(Value(), out timeoutSeconds) || timeoutSeconds is < 1 or > 300)
                        throw new ArgumentException("--timeout-seconds must be between 1 and 300");
                    break;
                case "--pause-ms":
                    if (!int.TryParse(Value(), out pauseMilliseconds) ||
                        pauseMilliseconds is < 100 or > 30_000)
                        throw new ArgumentException("--pause-ms must be between 100 and 30000");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }
        if (controlPort == inputPort) throw new ArgumentException("Control and input ports must differ");
        return new SimulatorOptions(
            host,
            controlPort,
            inputPort,
            moveCount,
            scenario,
            pauseMilliseconds,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static int ParsePort(string value, string argument)
    {
        if (!int.TryParse(value, out var port) || !DeskInkConstants.IsValidUserPort(port))
            throw new ArgumentException($"{argument} requires a port between 1025 and 65535");
        return port;
    }
}

internal enum SimulatorScenario
{
    Normal,
    Duplicate,
    Reorder,
    Gap,
    MissingUp,
    BadBind,
    WrongSession,
    BadVersion,
    Truncated,
    InvalidSampleCount,
    Timeout,
}
