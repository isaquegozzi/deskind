using DeskInk.Core.Protocol;

namespace DeskInk.Core.Input;

public enum MouseActionKind
{
    MoveAbsolute,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    Wheel,
}

public readonly record struct MouseAction(
    MouseActionKind Kind,
    ushort XNormalized = 0,
    ushort YNormalized = 0,
    int WheelDelta = 0);

public sealed class MouseInputTranslator
{
    private const ushort StylusButtonMask = 0x0060;
    private const double WheelUnitsPerSurface = 1_440.0;
    private const int HighResolutionWheelQuantum = 8;
    private readonly Dictionary<ushort, FingerScrollState> _fingerScroll = [];
    private bool _leftDown;
    private bool _rightDown;
    private bool _stylusInRange;
    private ushort? _stylusScrollLastY;
    private double _stylusScrollUnits;

    public MouseMode Mode { get; private set; } = MouseMode.ClickDrag;

    public void SetMode(MouseMode mode, Action<MouseAction> emit)
    {
        if (Mode == mode) return;
        ReleaseButtons(emit);
        _stylusScrollLastY = null;
        _stylusScrollUnits = 0;
        Mode = mode;
    }

    public void Apply(InputBatchFrame frame, Action<MouseAction> emit)
    {
        foreach (var sample in frame.Samples)
        {
            switch (frame.ToolKind)
            {
                case ToolKind.Stylus:
                case ToolKind.Eraser:
                    ApplyStylus(sample, emit);
                    break;
                case ToolKind.Finger:
                    ApplyFinger(frame.PointerId, sample, emit);
                    break;
            }
        }
    }

    public void ReleaseAll(Action<MouseAction> emit)
    {
        ReleaseButtons(emit);
        _fingerScroll.Clear();
        _stylusInRange = false;
        _stylusScrollLastY = null;
        _stylusScrollUnits = 0;
    }

    private void ApplyStylus(PenSample sample, Action<MouseAction> emit)
    {
        if (sample.State.HasFlag(PenSampleState.Canceled))
        {
            ReleaseButtons(emit);
            _stylusInRange = false;
            _stylusScrollLastY = null;
            _stylusScrollUnits = 0;
            return;
        }

        var inRange = sample.State.HasFlag(PenSampleState.InRange);
        _stylusInRange = inRange;
        var contact = sample.State.HasFlag(PenSampleState.Contact);
        if (Mode == MouseMode.PenScroll)
        {
            SetButton(false, ref _leftDown, MouseActionKind.LeftDown, MouseActionKind.LeftUp, emit);
            if (contact)
            {
                if (_stylusScrollLastY is ushort lastY)
                {
                    _stylusScrollUnits = EmitScroll(
                        _stylusScrollUnits +
                            (sample.YNormalized - lastY) * WheelUnitsPerSurface / ushort.MaxValue,
                        emit);
                }
                _stylusScrollLastY = sample.YNormalized;
            }
            else
            {
                _stylusScrollLastY = null;
                _stylusScrollUnits = 0;
                if (inRange)
                {
                    emit(new MouseAction(
                        MouseActionKind.MoveAbsolute,
                        sample.XNormalized,
                        sample.YNormalized));
                }
            }
            var scrollModeRightPressed = inRange && (sample.Buttons & StylusButtonMask) != 0;
            SetButton(
                scrollModeRightPressed,
                ref _rightDown,
                MouseActionKind.RightDown,
                MouseActionKind.RightUp,
                emit);
            if (!inRange) ReleaseButtons(emit);
            return;
        }

        if (inRange)
        {
            emit(new MouseAction(
                MouseActionKind.MoveAbsolute,
                sample.XNormalized,
                sample.YNormalized));
        }

        SetButton(contact, ref _leftDown, MouseActionKind.LeftDown, MouseActionKind.LeftUp, emit);
        var rightPressed = inRange && (sample.Buttons & StylusButtonMask) != 0;
        SetButton(rightPressed, ref _rightDown, MouseActionKind.RightDown, MouseActionKind.RightUp, emit);
        if (!inRange) ReleaseButtons(emit);
    }

    private void ApplyFinger(ushort pointerId, PenSample sample, Action<MouseAction> emit)
    {
        if (_stylusInRange)
        {
            _fingerScroll.Remove(pointerId);
            return;
        }
        var contact = sample.State.HasFlag(PenSampleState.Contact);
        if (sample.State.HasFlag(PenSampleState.Canceled) || !contact)
        {
            _fingerScroll.Remove(pointerId);
            return;
        }

        if (!_fingerScroll.TryGetValue(pointerId, out var state))
        {
            _fingerScroll[pointerId] = new FingerScrollState(sample.YNormalized, 0);
            return;
        }

        var accumulated = state.AccumulatedWheelUnits +
            (sample.YNormalized - state.LastY) * WheelUnitsPerSurface / ushort.MaxValue;
        accumulated = EmitScroll(accumulated, emit);
        _fingerScroll[pointerId] = new FingerScrollState(sample.YNormalized, accumulated);
    }

    private static double EmitScroll(double accumulated, Action<MouseAction> emit)
    {
        while (Math.Abs(accumulated) >= HighResolutionWheelQuantum)
        {
            var delta = accumulated > 0 ? HighResolutionWheelQuantum : -HighResolutionWheelQuantum;
            emit(new MouseAction(MouseActionKind.Wheel, WheelDelta: delta));
            accumulated -= delta;
        }
        return accumulated;
    }

    private void ReleaseButtons(Action<MouseAction> emit)
    {
        SetButton(false, ref _leftDown, MouseActionKind.LeftDown, MouseActionKind.LeftUp, emit);
        SetButton(false, ref _rightDown, MouseActionKind.RightDown, MouseActionKind.RightUp, emit);
    }

    private static void SetButton(
        bool pressed,
        ref bool prior,
        MouseActionKind down,
        MouseActionKind up,
        Action<MouseAction> emit)
    {
        if (pressed == prior) return;
        prior = pressed;
        emit(new MouseAction(pressed ? down : up));
    }

    private sealed record FingerScrollState(ushort LastY, double AccumulatedWheelUnits);
}
