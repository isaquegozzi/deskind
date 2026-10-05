using DeskInk.Core.Protocol;

namespace DeskInk.Core.Input;

[Flags]
public enum SyntheticPointerFlags : uint
{
    None = 0,
    New = 0x00000001,
    InRange = 0x00000002,
    InContact = 0x00000004,
    FirstButton = 0x00000010,
    Primary = 0x00002000,
    Canceled = 0x00008000,
    Down = 0x00010000,
    Update = 0x00020000,
    Up = 0x00040000,
}

[Flags]
public enum SyntheticPenFlags : uint
{
    None = 0,
    Barrel = 0x00000001,
    Inverted = 0x00000002,
    Eraser = 0x00000004,
}

[Flags]
public enum SyntheticPenMask : uint
{
    None = 0,
    Pressure = 0x00000001,
    TiltX = 0x00000004,
    TiltY = 0x00000008,
}

public readonly record struct SyntheticPenAction(
    SyntheticPointerFlags PointerFlags,
    SyntheticPenFlags PenFlags,
    SyntheticPenMask PenMask,
    ushort XNormalized,
    ushort YNormalized,
    uint Pressure,
    int TiltX,
    int TiltY);

public sealed class PenInputTranslator
{
    private const ushort StylusButtonMask = 0x0060;
    private readonly PenStateMachine _stateMachine = new();
    private PenSample? _lastSample;
    private ToolKind _lastTool = ToolKind.Stylus;

    public PenLifecycleState State => _stateMachine.State;

    public void Apply(InputBatchFrame frame, Action<SyntheticPenAction> emit)
    {
        if (frame.ToolKind is not (ToolKind.Stylus or ToolKind.Eraser)) return;

        foreach (var sample in frame.Samples)
        {
            var prior = _stateMachine.State;
            var transition = _stateMachine.Apply(sample);
            _lastSample = sample;
            _lastTool = frame.ToolKind;

            var pointerFlags = transition switch
            {
                PenTransition.Hover => SyntheticPointerFlags.InRange |
                    SyntheticPointerFlags.Update |
                    SyntheticPointerFlags.Primary |
                    (prior == PenLifecycleState.OutOfRange ? SyntheticPointerFlags.New : 0),
                PenTransition.Down => SyntheticPointerFlags.InRange |
                    SyntheticPointerFlags.InContact |
                    SyntheticPointerFlags.FirstButton |
                    SyntheticPointerFlags.Down |
                    SyntheticPointerFlags.Primary |
                    (prior == PenLifecycleState.OutOfRange ? SyntheticPointerFlags.New : 0),
                PenTransition.Update => SyntheticPointerFlags.InRange |
                    SyntheticPointerFlags.InContact |
                    SyntheticPointerFlags.FirstButton |
                    SyntheticPointerFlags.Update |
                    SyntheticPointerFlags.Primary,
                PenTransition.Up => SyntheticPointerFlags.Up |
                    SyntheticPointerFlags.Primary |
                    (sample.State.HasFlag(PenSampleState.InRange) ? SyntheticPointerFlags.InRange : 0) |
                    (sample.State.HasFlag(PenSampleState.Canceled) ? SyntheticPointerFlags.Canceled : 0),
                PenTransition.Suppressed when prior == PenLifecycleState.Hover =>
                    SyntheticPointerFlags.Update |
                    SyntheticPointerFlags.Canceled |
                    SyntheticPointerFlags.Primary,
                PenTransition.None when prior == PenLifecycleState.Hover &&
                    _stateMachine.State == PenLifecycleState.OutOfRange =>
                    SyntheticPointerFlags.Update | SyntheticPointerFlags.Primary,
                _ => SyntheticPointerFlags.None,
            };

            if (pointerFlags != SyntheticPointerFlags.None)
            {
                emit(CreateAction(sample, frame.ToolKind, pointerFlags));
            }
        }
    }

    public void ReleaseAll(Action<SyntheticPenAction> emit)
    {
        var prior = _stateMachine.State;
        var transition = _stateMachine.ReleaseAll();
        if (_lastSample is not { } sample) return;
        if (transition == PenTransition.Up)
        {
            emit(CreateAction(
                sample,
                _lastTool,
                SyntheticPointerFlags.Up | SyntheticPointerFlags.Canceled | SyntheticPointerFlags.Primary));
        }
        else if (prior == PenLifecycleState.Hover)
        {
            emit(CreateAction(
                sample,
                _lastTool,
                SyntheticPointerFlags.Update | SyntheticPointerFlags.Canceled | SyntheticPointerFlags.Primary));
        }
    }

    public static uint NormalizePressure(ushort pressureNormalized) =>
        (uint)Math.Round(pressureNormalized / (double)ushort.MaxValue * 1024);

    public static (int TiltX, int TiltY) ResolveTilt(
        ushort tiltCentidegrees,
        short orientationCentidegrees)
    {
        var tiltDegrees = Math.Clamp(tiltCentidegrees / 100.0, 0, 90);
        var orientationRadians = orientationCentidegrees / 100.0 * Math.PI / 180.0;
        return (
            Math.Clamp((int)Math.Round(Math.Sin(orientationRadians) * tiltDegrees), -90, 90),
            Math.Clamp((int)Math.Round(-Math.Cos(orientationRadians) * tiltDegrees), -90, 90));
    }

    private static SyntheticPenAction CreateAction(
        PenSample sample,
        ToolKind tool,
        SyntheticPointerFlags pointerFlags)
    {
        var penFlags = (sample.Buttons & StylusButtonMask) != 0
            ? SyntheticPenFlags.Barrel
            : SyntheticPenFlags.None;
        if (tool == ToolKind.Eraser)
        {
            penFlags |= SyntheticPenFlags.Inverted | SyntheticPenFlags.Eraser;
        }

        var penMask = SyntheticPenMask.None;
        uint pressure = 0;
        if (sample.State.HasFlag(PenSampleState.PressureValid))
        {
            penMask |= SyntheticPenMask.Pressure;
            pressure = NormalizePressure(sample.PressureNormalized);
        }

        var tiltX = 0;
        var tiltY = 0;
        if (sample.State.HasFlag(PenSampleState.TiltValid | PenSampleState.OrientationValid))
        {
            penMask |= SyntheticPenMask.TiltX | SyntheticPenMask.TiltY;
            (tiltX, tiltY) = ResolveTilt(sample.TiltCentidegrees, sample.OrientationCentidegrees);
        }

        return new SyntheticPenAction(
            pointerFlags,
            penFlags,
            penMask,
            sample.XNormalized,
            sample.YNormalized,
            pressure,
            tiltX,
            tiltY);
    }
}
