using System.Collections.Concurrent;
using DeskInk.Core.Overlay;

namespace DeskInk.Overlay;

public sealed class OverlayWindowController : IDisposable
{
    private readonly ConcurrentQueue<OverlayInputAction> _inputQueue = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _uiThread;
    private OverlayWindow? _window;
    private int _drainScheduled;
    private int _disposed;
    private long _actions;
    private long _renders;
    private int _strokeCount;
    private string? _lastError;
    private bool _visible = true;

    public OverlayWindowController(int monitorIndex)
    {
        MonitorIndex = monitorIndex;
        MonitorCount = Screen.AllScreens.Length;
        _uiThread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var window = new OverlayWindow(monitorIndex);
            _window = window;
            window.Shown += (_, _) => _ready.Set();
            Application.Run(window);
        })
        {
            IsBackground = true,
            Name = "DeskInk Overlay UI",
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Overlay window did not initialize");
    }

    public int MonitorIndex { get; private set; }
    public int MonitorCount { get; }
    public string MetricsText =>
        $"actions={Interlocked.Read(ref _actions)} renders={Interlocked.Read(ref _renders)} " +
        $"strokes={Volatile.Read(ref _strokeCount)} visible={_visible} " +
        $"error={_lastError ?? "none"}";

    public void Apply(OverlayInputAction action)
    {
        ThrowIfDisposed();
        _inputQueue.Enqueue(action);
        Interlocked.Increment(ref _actions);
        ScheduleDrain();
    }

    public void Undo() => Post(window => window.Undo());
    public void Redo() => Post(window => window.Redo());
    public void Clear() => Post(window => window.ClearDocument());
    public void SetMonitor(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= MonitorCount)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        MonitorIndex = monitorIndex;
        Post(window => window.SetMonitor(monitorIndex));
    }
    public void SetClickThrough(bool clickThrough) => Post(window => window.SetClickThrough(clickThrough));
    public void SetOverlayVisible(bool visible)
    {
        _visible = visible;
        Post(window => window.SetOverlayVisible(visible));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_window is { IsDisposed: false } window)
        {
            try { window.BeginInvoke(window.Close); } catch (InvalidOperationException) { }
        }
        _uiThread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    private void ScheduleDrain()
    {
        if (Interlocked.Exchange(ref _drainScheduled, 1) != 0) return;
        Post(window =>
        {
            try
            {
                while (_inputQueue.TryDequeue(out var action)) window.Apply(action);
                window.RenderDocument();
                Interlocked.Increment(ref _renders);
                Volatile.Write(ref _strokeCount, window.StrokeCount);
                _lastError = null;
            }
            catch (Exception exception)
            {
                _lastError = $"{exception.GetType().Name}:{exception.Message}";
            }
            Interlocked.Exchange(ref _drainScheduled, 0);
            if (!_inputQueue.IsEmpty) ScheduleDrain();
        });
    }

    private void Post(Action<OverlayWindow> action)
    {
        ThrowIfDisposed();
        var window = _window ?? throw new InvalidOperationException("Overlay window is unavailable");
        try { window.BeginInvoke(() => action(window)); }
        catch (InvalidOperationException) when (Volatile.Read(ref _disposed) != 0) { }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
