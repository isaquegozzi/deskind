using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DeskInk.Core.Overlay;

namespace DeskInk.Overlay;

internal sealed class OverlayWindow : Form
{
    private readonly Dictionary<int, OverlayStrokeDocument> _documents = [];
    private OverlayStrokeDocument _document;
    private int _monitorIndex;
    private bool _clickThrough = true;
    private bool _overlayVisible = true;

    public OverlayWindow(int monitorIndex)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        _document = GetDocument(monitorIndex);
        SetMonitor(monitorIndex);
    }

    protected override bool ShowWithoutActivation => true;
    public int StrokeCount => _document.Strokes.Count;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate;
            if (_clickThrough) parameters.ExStyle |= WsExTransparent;
            return parameters;
        }
    }

    public void Apply(OverlayInputAction action)
    {
        switch (action.Kind)
        {
            case OverlayInputActionKind.BeginStroke:
                _document.BeginStroke(
                    action.Tool,
                    action.Point,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                break;
            case OverlayInputActionKind.AppendPoint:
                _document.AppendPoint(action.Point);
                break;
            case OverlayInputActionKind.EndStroke:
                _document.EndStroke();
                break;
            case OverlayInputActionKind.CancelStroke:
                _document.CancelStroke();
                break;
            case OverlayInputActionKind.EraseAt:
                _document.EraseAt(action.Point);
                break;
        }
    }

    public void Undo()
    {
        if (_document.Undo()) RenderDocument();
    }

    public void Redo()
    {
        if (_document.Redo()) RenderDocument();
    }

    public void ClearDocument()
    {
        if (_document.Clear()) RenderDocument();
    }

    public void SetMonitor(int monitorIndex)
    {
        var screens = Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        _monitorIndex = monitorIndex;
        _document = GetDocument(monitorIndex);
        Bounds = screens[monitorIndex].Bounds;
        RenderDocument();
    }

    public void SetClickThrough(bool clickThrough)
    {
        if (_clickThrough == clickThrough) return;
        _clickThrough = clickThrough;
        var style = GetWindowLongPtr(Handle, GwlExStyle).ToInt64();
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(Handle, GwlExStyle, new IntPtr(style));
        SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    public void SetOverlayVisible(bool visible)
    {
        _overlayVisible = visible;
        if (visible)
        {
            if (!Visible) Show();
            SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        RenderDocument();
    }

    public void RenderDocument()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        if (_overlayVisible)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            foreach (var stroke in _document.Strokes) DrawStroke(graphics, stroke);
            if (_document.SnapshotActiveStroke() is { } active) DrawStroke(graphics, active);
        }
        Present(bitmap);
    }

    private OverlayStrokeDocument GetDocument(int monitorIndex)
    {
        if (!_documents.TryGetValue(monitorIndex, out var document))
        {
            document = new OverlayStrokeDocument();
            _documents[monitorIndex] = document;
        }
        return document;
    }

    private void DrawStroke(Graphics graphics, OverlayStroke stroke)
    {
        if (stroke.Points.Count == 0) return;
        var color = Color.FromArgb(
            stroke.Brush.Opacity,
            (int)((stroke.Brush.Rgb >> 16) & 0xff),
            (int)((stroke.Brush.Rgb >> 8) & 0xff),
            (int)(stroke.Brush.Rgb & 0xff));
        if (stroke.Points.Count == 1)
        {
            var point = Map(stroke.Points[0]);
            var width = ResolveWidth(stroke.Brush, stroke.Points[0]);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, point.X - width / 2, point.Y - width / 2, width, width);
            return;
        }
        if (!stroke.Brush.PressureAffectsWidth)
        {
            using var highlighterPen = new Pen(color, stroke.Brush.BaseWidthPixels)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            graphics.DrawLines(highlighterPen, stroke.Points.Select(Map).ToArray());
            return;
        }
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var start = Map(stroke.Points[index - 1]);
            var end = Map(stroke.Points[index]);
            var width = (ResolveWidth(stroke.Brush, stroke.Points[index - 1]) +
                         ResolveWidth(stroke.Brush, stroke.Points[index])) / 2;
            using var pen = new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            graphics.DrawLine(pen, start, end);
        }
    }

    private PointF Map(OverlayPoint point) => new(
        point.XNormalized / (float)ushort.MaxValue * Math.Max(0, ClientSize.Width - 1),
        point.YNormalized / (float)ushort.MaxValue * Math.Max(0, ClientSize.Height - 1));

    private static float ResolveWidth(OverlayBrush brush, OverlayPoint point)
    {
        if (!brush.PressureAffectsWidth) return brush.BaseWidthPixels;
        var pressure = point.PressureNormalized / (float)ushort.MaxValue;
        return brush.BaseWidthPixels * (0.35f + 0.65f * pressure);
    }

    private void Present(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var destination = new NativePoint(Left, Top);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha,
            };
            if (!UpdateLayeredWindow(
                Handle, screenDc, ref destination, ref size, memoryDc, ref source,
                0, ref blend, UlwAlpha))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const uint UlwAlpha = 2;

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public NativePoint(int x, int y) { X = x; Y = y; } public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public NativeSize(int cx, int cy) { Cx = cx; Cy = cy; } public int Cx; public int Cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref NativePoint destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
