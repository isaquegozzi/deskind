using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskInk.Core.Input;
using System.Windows.Forms;

namespace DeskInk.WindowsHost;

public sealed class MouseInputController
{
    private DesktopCoordinateMapper _mapper;
    private long _moves;
    private long _leftDown;
    private long _leftUp;
    private long _rightDown;
    private long _rightUp;
    private long _wheel;

    private MouseInputController(DesktopCoordinateMapper mapper, string monitorDescription)
    {
        _mapper = mapper;
        MonitorDescription = monitorDescription;
    }

    public string MonitorDescription { get; private set; }
    public int MonitorIndex { get; private set; }
    public int MonitorCount => Screen.AllScreens.Length;

    public string MetricsText =>
        $"move={Interlocked.Read(ref _moves)} " +
        $"left={Interlocked.Read(ref _leftDown)}/{Interlocked.Read(ref _leftUp)} " +
        $"right={Interlocked.Read(ref _rightDown)}/{Interlocked.Read(ref _rightUp)} " +
        $"wheel={Interlocked.Read(ref _wheel)}";

    public static MouseInputController Create(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monitorIndex),
                $"Monitor index must be between 0 and {screens.Length - 1}");
        }
        var screen = screens[monitorIndex];
        var target = new DesktopRectangle(
            screen.Bounds.Left,
            screen.Bounds.Top,
            screen.Bounds.Width,
            screen.Bounds.Height);
        var virtualBounds = SystemInformation.VirtualScreen;
        var virtualDesktop = new DesktopRectangle(
            virtualBounds.Left,
            virtualBounds.Top,
            virtualBounds.Width,
            virtualBounds.Height);
        var description = $"{monitorIndex}: {screen.DeviceName} " +
            $"[{screen.Bounds.Left},{screen.Bounds.Top} {screen.Bounds.Width}x{screen.Bounds.Height}] " +
            $"primary={screen.Primary}";
        return new MouseInputController(new DesktopCoordinateMapper(target, virtualDesktop), description)
        {
            MonitorIndex = monitorIndex,
        };
    }

    public void SetMonitor(int monitorIndex)
    {
        var replacement = Create(monitorIndex);
        Volatile.Write(ref _mapper, replacement._mapper);
        MonitorDescription = replacement.MonitorDescription;
        MonitorIndex = monitorIndex;
    }

    public static IReadOnlyList<string> DescribeMonitors() => Screen.AllScreens
        .Select((screen, index) =>
            $"{index}: {screen.DeviceName} " +
            $"[{screen.Bounds.Left},{screen.Bounds.Top} {screen.Bounds.Width}x{screen.Bounds.Height}] " +
            $"primary={screen.Primary}")
        .ToArray();

    public void Apply(MouseAction action)
    {
        var input = action.Kind switch
        {
            MouseActionKind.MoveAbsolute => AbsoluteMove(action.XNormalized, action.YNormalized),
            MouseActionKind.LeftDown => Button(MouseEventFlags.LeftDown),
            MouseActionKind.LeftUp => Button(MouseEventFlags.LeftUp),
            MouseActionKind.RightDown => Button(MouseEventFlags.RightDown),
            MouseActionKind.RightUp => Button(MouseEventFlags.RightUp),
            MouseActionKind.Wheel => Wheel(action.WheelDelta),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed");
        }
        switch (action.Kind)
        {
            case MouseActionKind.MoveAbsolute: Interlocked.Increment(ref _moves); break;
            case MouseActionKind.LeftDown: Interlocked.Increment(ref _leftDown); break;
            case MouseActionKind.LeftUp: Interlocked.Increment(ref _leftUp); break;
            case MouseActionKind.RightDown: Interlocked.Increment(ref _rightDown); break;
            case MouseActionKind.RightUp: Interlocked.Increment(ref _rightUp); break;
            case MouseActionKind.Wheel: Interlocked.Add(ref _wheel, action.WheelDelta); break;
        }
    }

    private Input AbsoluteMove(ushort x, ushort y)
    {
        var mapped = Volatile.Read(ref _mapper).Map(x, y);
        return Mouse(
            mapped.X,
            mapped.Y,
            0,
            MouseEventFlags.Move | MouseEventFlags.Absolute | MouseEventFlags.VirtualDesk);
    }

    private static Input Button(MouseEventFlags flags) => Mouse(0, 0, 0, flags);

    private static Input Wheel(int delta) => Mouse(0, 0, unchecked((uint)delta), MouseEventFlags.Wheel);

    private static Input Mouse(int dx, int dy, uint data, MouseEventFlags flags) => new()
    {
        Type = InputMouse,
        Union = new InputUnion
        {
            Mouse = new MouseInput
            {
                Dx = dx,
                Dy = dy,
                MouseData = data,
                Flags = flags,
            },
        },
    };

    private const uint InputMouse = 0;

    [Flags]
    private enum MouseEventFlags : uint
    {
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        Wheel = 0x0800,
        VirtualDesk = 0x4000,
        Absolute = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public MouseEventFlags Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);
}
