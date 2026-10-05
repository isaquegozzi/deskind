namespace DeskInk.Core;

public static class DeskInkConstants
{
    public const byte ProtocolMajor = 1;
    public const byte ProtocolMinor = 0;
    public const uint ProtocolMagic = 0x494B5344;

    public const int ControlPort = 27183;
    public const int InputPort = 27184;
    public const int LanControlPort = 27185;
    public const int LanInputPort = 27186;
    public const int LanDiscoveryPort = 27187;

    public const int MaximumInputFrameBytes = 1200;
    public const int MaximumControlFrameBytes = ushort.MaxValue;
    public const int MaximumSamplesPerBatch = 32;

    public static bool IsValidUserPort(int port) => port is > 1024 and <= 65_535;

    public static bool HasIsolatedChannels(int controlPort, int inputPort) =>
        IsValidUserPort(controlPort) &&
        IsValidUserPort(inputPort) &&
        controlPort != inputPort;

    public static bool HasValidFrameLimits(int inputLimit, int controlLimit) =>
        inputLimit > 0 && controlLimit > 0 && inputLimit <= controlLimit;
}
