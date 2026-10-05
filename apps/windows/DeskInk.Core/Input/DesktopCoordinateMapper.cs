namespace DeskInk.Core.Input;

public readonly record struct DesktopRectangle(int Left, int Top, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public readonly record struct AbsolutePoint(int X, int Y);

public sealed class DesktopCoordinateMapper(
    DesktopRectangle targetMonitor,
    DesktopRectangle virtualDesktop)
{
    public AbsolutePoint MapToPixel(ushort xNormalized, ushort yNormalized)
    {
        Validate();
        return new AbsolutePoint(
            targetMonitor.Left + Scale(xNormalized, targetMonitor.Width),
            targetMonitor.Top + Scale(yNormalized, targetMonitor.Height));
    }

    public AbsolutePoint MapToVirtualPixel(ushort xNormalized, ushort yNormalized)
    {
        var pixel = MapToPixel(xNormalized, yNormalized);
        return new AbsolutePoint(
            pixel.X - virtualDesktop.Left,
            pixel.Y - virtualDesktop.Top);
    }

    public AbsolutePoint Map(ushort xNormalized, ushort yNormalized)
    {
        Validate();
        var pixel = MapToPixel(xNormalized, yNormalized);
        return new AbsolutePoint(
            Normalize(pixel.X - virtualDesktop.Left, virtualDesktop.Width),
            Normalize(pixel.Y - virtualDesktop.Top, virtualDesktop.Height));
    }

    private void Validate()
    {
        if (!targetMonitor.IsValid || !virtualDesktop.IsValid)
        {
            throw new ArgumentException("Desktop rectangles must have positive dimensions");
        }
    }

    private static int Scale(ushort normalized, int extent) =>
        extent <= 1 ? 0 : (int)Math.Round(normalized / (double)ushort.MaxValue * (extent - 1));

    private static int Normalize(int offset, int extent) =>
        extent <= 1
            ? 0
            : Math.Clamp(
                (int)Math.Round(offset / (double)(extent - 1) * ushort.MaxValue),
                0,
                ushort.MaxValue);
}
