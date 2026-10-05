using DeskInk.Core.Protocol;

namespace DeskInk.WindowsHost;

public enum HostTransportKind
{
    None,
    UsbAdb,
    Lan,
}

public readonly record struct HostStatusSnapshot(
    bool ControlConnected,
    bool InputConnected,
    HostTransportKind Transport,
    ulong? SessionId,
    byte MonitorIndex,
    DeskInkOutputMode OutputMode,
    long Frames,
    long Samples,
    double? LastInputMillisecondsAgo)
{
    public bool Connected => ControlConnected && InputConnected;
}
