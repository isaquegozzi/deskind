using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DeskInk.Core.Input;

namespace DeskInk.WindowsHost;

public sealed class SyntheticPenController : IDisposable
{
    private DesktopCoordinateMapper _mapper;
    private readonly IntPtr _device;
    private readonly object _injectionGate = new();
    private readonly PointerTypeInfo[] _pointerBuffer = new PointerTypeInfo[1];
    private long _injected;
    private long _down;
    private long _up;
    private int _disposed;
    private bool _pointerInRange;

    private SyntheticPenController(DesktopCoordinateMapper mapper, string monitorDescription, IntPtr device)
    {
        _mapper = mapper;
        MonitorDescription = monitorDescription;
        _device = device;
    }

    public string MonitorDescription { get; private set; }
    public int MonitorIndex { get; private set; }
    public int MonitorCount => Screen.AllScreens.Length;
    public string MetricsText =>
        $"events={Interlocked.Read(ref _injected)} " +
        $"down={Interlocked.Read(ref _down)} up={Interlocked.Read(ref _up)}";

    public static SyntheticPenController Create(int monitorIndex)
    {
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Synthetic pen host requires the win-x64 runtime");
        if (Marshal.SizeOf<PointerInfo>() != 96 || Marshal.SizeOf<PointerPenInfo>() != 120 ||
            Marshal.SizeOf<PointerTypeInfo>() != 128)
            throw new PlatformNotSupportedException("Unexpected Win32 synthetic pointer structure layout");

        var (mapper, description) = CreateMonitorMapping(monitorIndex);

        var device = CreateSyntheticPointerDevice(PointerInputType.Pen, 1, PointerFeedbackMode.Default);
        if (device == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateSyntheticPointerDevice failed");
        return new SyntheticPenController(mapper, description, device)
        {
            MonitorIndex = monitorIndex,
        };
    }

    public void SetMonitor(int monitorIndex)
    {
        var (mapper, description) = CreateMonitorMapping(monitorIndex);
        Volatile.Write(ref _mapper, mapper);
        MonitorDescription = description;
        MonitorIndex = monitorIndex;
    }

    private static (DesktopCoordinateMapper Mapper, string Description) CreateMonitorMapping(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index must be between 0 and {screens.Length - 1}");
        var screen = screens[monitorIndex];
        var target = new DesktopRectangle(
            screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
        var virtualBounds = SystemInformation.VirtualScreen;
        var virtualDesktop = new DesktopRectangle(
            virtualBounds.Left, virtualBounds.Top, virtualBounds.Width, virtualBounds.Height);
        var description = $"{monitorIndex}: {screen.DeviceName} " +
            $"[{screen.Bounds.Left},{screen.Bounds.Top} {screen.Bounds.Width}x{screen.Bounds.Height}] " +
            $"primary={screen.Primary}";
        return (new DesktopCoordinateMapper(target, virtualDesktop), description);
    }

    public void Apply(SyntheticPenAction action)
    {
        lock (_injectionGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var pointerFlags = action.PointerFlags;
            if (pointerFlags.HasFlag(SyntheticPointerFlags.InRange) &&
                !_pointerInRange &&
                !pointerFlags.HasFlag(SyntheticPointerFlags.New))
            {
                pointerFlags |= SyntheticPointerFlags.New;
            }
            if (!TryInject(action, pointerFlags, out var errorCode) &&
                IsHoverUpdate(pointerFlags))
            {
                pointerFlags |= SyntheticPointerFlags.New;
                if (!TryInject(action, pointerFlags, out errorCode))
                {
                    throw InjectionError(pointerFlags, errorCode);
                }
            }
            else if (errorCode != 0)
            {
                throw InjectionError(pointerFlags, errorCode);
            }

            _pointerInRange = pointerFlags.HasFlag(SyntheticPointerFlags.InRange) &&
                !pointerFlags.HasFlag(SyntheticPointerFlags.Canceled);
            Interlocked.Increment(ref _injected);
            if (pointerFlags.HasFlag(SyntheticPointerFlags.Down)) Interlocked.Increment(ref _down);
            if (pointerFlags.HasFlag(SyntheticPointerFlags.Up)) Interlocked.Increment(ref _up);
        }
    }

    private bool TryInject(
        SyntheticPenAction action,
        SyntheticPointerFlags pointerFlags,
        out int errorCode)
    {
        var point = Volatile.Read(ref _mapper).MapToVirtualPixel(action.XNormalized, action.YNormalized);
        var input = new PointerTypeInfo
        {
            Type = PointerInputType.Pen,
            PenInfo = new PointerPenInfo
            {
                PointerInfo = new PointerInfo
                {
                    PointerType = PointerInputType.Pen,
                    PointerId = 1,
                    PointerFlags = pointerFlags,
                    PixelLocation = new Point(point.X, point.Y),
                    RawPixelLocation = new Point(point.X, point.Y),
                },
                PenFlags = action.PenFlags,
                PenMask = action.PenMask,
                Pressure = action.Pressure,
                TiltX = action.TiltX,
                TiltY = action.TiltY,
            },
        };
        _pointerBuffer[0] = input;
        if (InjectSyntheticPointerInput(_device, _pointerBuffer, 1))
        {
            errorCode = 0;
            return true;
        }
        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    private static bool IsHoverUpdate(SyntheticPointerFlags flags) =>
        flags.HasFlag(SyntheticPointerFlags.InRange | SyntheticPointerFlags.Update) &&
        !flags.HasFlag(
            SyntheticPointerFlags.InContact |
            SyntheticPointerFlags.Down |
            SyntheticPointerFlags.Up);

    private static Win32Exception InjectionError(SyntheticPointerFlags flags, int errorCode) =>
        new(errorCode, $"InjectSyntheticPointerInput failed flags={flags}");

    public void Dispose()
    {
        lock (_injectionGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            DestroySyntheticPointerDevice(_device);
        }
    }

    private enum PointerInputType : uint { Pen = 3 }
    private enum PointerFeedbackMode : uint { Default = 1 }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Point(int x, int y) { X = x; Y = y; }
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public PointerInputType PointerType;
        public uint PointerId;
        public uint FrameId;
        public SyntheticPointerFlags PointerFlags;
        public IntPtr SourceDevice;
        public IntPtr WindowTarget;
        public Point PixelLocation;
        public Point HimetricLocation;
        public Point RawPixelLocation;
        public Point RawHimetricLocation;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerPenInfo
    {
        public PointerInfo PointerInfo;
        public SyntheticPenFlags PenFlags;
        public SyntheticPenMask PenMask;
        public uint Pressure;
        public uint Rotation;
        public int TiltX;
        public int TiltY;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PointerTypeInfo
    {
        [FieldOffset(0)] public PointerInputType Type;
        [FieldOffset(8)] public PointerPenInfo PenInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateSyntheticPointerDevice(
        PointerInputType pointerType, uint maximumCount, PointerFeedbackMode feedbackMode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InjectSyntheticPointerInput(
        IntPtr device, [In] PointerTypeInfo[] pointerInfo, uint count);

    [DllImport("user32.dll")]
    private static extern void DestroySyntheticPointerDevice(IntPtr device);
}
