using DeskInk.Core.Protocol;

namespace DeskInk.Core.Overlay;

public enum OverlayTool : byte
{
    Pen = 0,
    Highlighter = 1,
    Eraser = 2,
}

public readonly record struct OverlayPoint(
    ushort XNormalized,
    ushort YNormalized,
    ushort PressureNormalized);

public readonly record struct OverlayBrush(
    uint Rgb,
    byte Opacity,
    float BaseWidthPixels,
    bool PressureAffectsWidth)
{
    public static OverlayBrush For(OverlayTool tool) => tool switch
    {
        OverlayTool.Pen => new OverlayBrush(0x00FF3B30, 255, 4f, true),
        OverlayTool.Highlighter => new OverlayBrush(0x00FFD60A, 80, 24f, false),
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    };
}

public sealed record OverlayStroke(
    Guid Id,
    OverlayTool Tool,
    OverlayBrush Brush,
    IReadOnlyList<OverlayPoint> Points,
    long CreatedAtUnixMilliseconds);

public sealed class OverlayStrokeDocument
{
    private readonly List<OverlayStroke> _strokes = [];
    private readonly Stack<DocumentEdit> _undo = [];
    private readonly Stack<DocumentEdit> _redo = [];
    private ActiveStroke? _active;

    public IReadOnlyList<OverlayStroke> Strokes => _strokes;
    public bool HasActiveStroke => _active is not null;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;

    public void BeginStroke(OverlayTool tool, OverlayPoint point, long timestampMilliseconds)
    {
        if (tool == OverlayTool.Eraser) throw new ArgumentException("Eraser does not create strokes");
        _active = new ActiveStroke(tool, OverlayBrush.For(tool), timestampMilliseconds, [point]);
    }

    public void AppendPoint(OverlayPoint point)
    {
        if (_active is null) return;
        if (_active.Points.Count == 0 || _active.Points[^1] != point) _active.Points.Add(point);
    }

    public bool EndStroke()
    {
        if (_active is null) return false;
        var stroke = new OverlayStroke(
            Guid.NewGuid(),
            _active.Tool,
            _active.Brush,
            _active.Points.ToArray(),
            _active.CreatedAtUnixMilliseconds);
        _active = null;
        _strokes.Add(stroke);
        Record(new AddedEdit(stroke));
        return true;
    }

    public void CancelStroke() => _active = null;

    public bool EraseAt(OverlayPoint point, ushort radiusNormalized = 1_200)
    {
        var radiusSquared = (double)radiusNormalized * radiusNormalized;
        var removed = _strokes
            .Select((stroke, index) => new IndexedStroke(index, stroke))
            .Where(item => HitTest(item.Stroke, point, radiusSquared))
            .ToArray();
        if (removed.Length == 0) return false;
        foreach (var item in removed.Reverse()) _strokes.RemoveAt(item.Index);
        Record(new RemovedEdit(removed));
        return true;
    }

    public bool Undo()
    {
        CancelStroke();
        if (!_undo.TryPop(out var edit)) return false;
        ApplyReverse(edit);
        _redo.Push(edit);
        return true;
    }

    public bool Redo()
    {
        CancelStroke();
        if (!_redo.TryPop(out var edit)) return false;
        ApplyForward(edit);
        _undo.Push(edit);
        return true;
    }

    public bool Clear()
    {
        CancelStroke();
        if (_strokes.Count == 0) return false;
        var removed = _strokes
            .Select((stroke, index) => new IndexedStroke(index, stroke))
            .ToArray();
        _strokes.Clear();
        Record(new RemovedEdit(removed));
        return true;
    }

    public OverlayStroke? SnapshotActiveStroke() => _active is null
        ? null
        : new OverlayStroke(
            Guid.Empty,
            _active.Tool,
            _active.Brush,
            _active.Points.ToArray(),
            _active.CreatedAtUnixMilliseconds);

    private void Record(DocumentEdit edit)
    {
        _undo.Push(edit);
        _redo.Clear();
    }

    private void ApplyForward(DocumentEdit edit)
    {
        switch (edit)
        {
            case AddedEdit added:
                _strokes.Add(added.Stroke);
                break;
            case RemovedEdit removed:
                foreach (var item in removed.Strokes.Reverse()) _strokes.RemoveAt(item.Index);
                break;
        }
    }

    private void ApplyReverse(DocumentEdit edit)
    {
        switch (edit)
        {
            case AddedEdit added:
                _strokes.RemoveAll(stroke => stroke.Id == added.Stroke.Id);
                break;
            case RemovedEdit removed:
                foreach (var item in removed.Strokes) _strokes.Insert(item.Index, item.Stroke);
                break;
        }
    }

    private static bool HitTest(OverlayStroke stroke, OverlayPoint point, double radiusSquared)
    {
        if (stroke.Points.Count == 1)
            return DistanceSquared(stroke.Points[0], point) <= radiusSquared;
        for (var index = 1; index < stroke.Points.Count; index++)
        {
            if (SegmentDistanceSquared(stroke.Points[index - 1], stroke.Points[index], point) <= radiusSquared)
                return true;
        }
        return false;
    }

    private static double DistanceSquared(OverlayPoint left, OverlayPoint right)
    {
        var dx = (double)left.XNormalized - right.XNormalized;
        var dy = (double)left.YNormalized - right.YNormalized;
        return dx * dx + dy * dy;
    }

    private static double SegmentDistanceSquared(OverlayPoint start, OverlayPoint end, OverlayPoint point)
    {
        var dx = (double)end.XNormalized - start.XNormalized;
        var dy = (double)end.YNormalized - start.YNormalized;
        if (dx == 0 && dy == 0) return DistanceSquared(start, point);
        var projection = Math.Clamp(
            ((point.XNormalized - start.XNormalized) * dx +
             (point.YNormalized - start.YNormalized) * dy) / (dx * dx + dy * dy),
            0,
            1);
        var nearestX = start.XNormalized + projection * dx;
        var nearestY = start.YNormalized + projection * dy;
        var pointDx = point.XNormalized - nearestX;
        var pointDy = point.YNormalized - nearestY;
        return pointDx * pointDx + pointDy * pointDy;
    }

    private abstract record DocumentEdit;
    private sealed record AddedEdit(OverlayStroke Stroke) : DocumentEdit;
    private sealed record RemovedEdit(IndexedStroke[] Strokes) : DocumentEdit;
    private sealed record IndexedStroke(int Index, OverlayStroke Stroke);
    private sealed record ActiveStroke(
        OverlayTool Tool,
        OverlayBrush Brush,
        long CreatedAtUnixMilliseconds,
        List<OverlayPoint> Points);
}

public enum OverlayInputActionKind
{
    BeginStroke,
    AppendPoint,
    EndStroke,
    CancelStroke,
    EraseAt,
}

public readonly record struct OverlayInputAction(
    OverlayInputActionKind Kind,
    OverlayTool Tool,
    OverlayPoint Point);

public sealed class OverlayInputTranslator
{
    private const ushort StylusButtonMask = 0x0060;
    private bool _drawing;

    public void Apply(
        InputBatchFrame frame,
        OverlayTool selectedTool,
        Action<OverlayInputAction> emit)
    {
        if (frame.ToolKind is not (ToolKind.Stylus or ToolKind.Eraser)) return;
        foreach (var sample in frame.Samples)
        {
            var point = new OverlayPoint(
                sample.XNormalized,
                sample.YNormalized,
                sample.State.HasFlag(PenSampleState.PressureValid)
                    ? sample.PressureNormalized
                    : ushort.MaxValue);
            if (sample.State.HasFlag(PenSampleState.Canceled))
            {
                Cancel(emit, selectedTool, point);
                continue;
            }

            var erasing = selectedTool == OverlayTool.Eraser ||
                frame.ToolKind == ToolKind.Eraser ||
                (sample.Buttons & StylusButtonMask) != 0;
            var contact = sample.State.HasFlag(PenSampleState.Contact);
            if (erasing)
            {
                if (_drawing) Cancel(emit, selectedTool, point);
                if (contact) emit(new OverlayInputAction(OverlayInputActionKind.EraseAt, OverlayTool.Eraser, point));
                continue;
            }

            if (contact && !_drawing)
            {
                _drawing = true;
                emit(new OverlayInputAction(OverlayInputActionKind.BeginStroke, selectedTool, point));
            }
            else if (contact)
            {
                emit(new OverlayInputAction(OverlayInputActionKind.AppendPoint, selectedTool, point));
            }
            else if (_drawing)
            {
                emit(new OverlayInputAction(OverlayInputActionKind.AppendPoint, selectedTool, point));
                emit(new OverlayInputAction(OverlayInputActionKind.EndStroke, selectedTool, point));
                _drawing = false;
            }
        }
    }

    public void ReleaseAll(Action<OverlayInputAction> emit)
    {
        if (!_drawing) return;
        _drawing = false;
        emit(new OverlayInputAction(
            OverlayInputActionKind.CancelStroke,
            OverlayTool.Pen,
            default));
    }

    private void Cancel(Action<OverlayInputAction> emit, OverlayTool tool, OverlayPoint point)
    {
        if (!_drawing) return;
        _drawing = false;
        emit(new OverlayInputAction(OverlayInputActionKind.CancelStroke, tool, point));
    }
}
