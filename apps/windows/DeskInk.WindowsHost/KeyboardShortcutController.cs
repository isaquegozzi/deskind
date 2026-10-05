using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskInk.Core.Protocol;

namespace DeskInk.WindowsHost;

public sealed class KeyboardShortcutController
{
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyC = 0x43;
    private const ushort VirtualKeyV = 0x56;
    private const ushort VirtualKeyF = 0x46;
    private const ushort VirtualKeyPageUp = 0x21;
    private const ushort VirtualKeyPageDown = 0x22;
    private const ushort VirtualKeyMediaPlayPause = 0xB3;
    private const ushort VirtualKeyVolumeDown = 0xAE;
    private const ushort VirtualKeyVolumeUp = 0xAF;
    private const uint KeyboardInput = 1;
    private const uint KeyUp = 0x0002;

    public void Execute(ShortcutCommand command)
    {
        var inputs = command switch
        {
            ShortcutCommand.Copy => Chord(VirtualKeyControl, VirtualKeyC),
            ShortcutCommand.Paste => Chord(VirtualKeyControl, VirtualKeyV),
            ShortcutCommand.Find => Chord(VirtualKeyControl, VirtualKeyF),
            ShortcutCommand.PageUp => Press(VirtualKeyPageUp),
            ShortcutCommand.PageDown => Press(VirtualKeyPageDown),
            ShortcutCommand.MediaPlayPause => Press(VirtualKeyMediaPlayPause),
            ShortcutCommand.VolumeDown => Press(VirtualKeyVolumeDown),
            ShortcutCommand.VolumeUp => Press(VirtualKeyVolumeUp),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput failed for {command}");
    }

    internal static ushort[] KeysForTests(ShortcutCommand command) => command switch
    {
        ShortcutCommand.Copy => [VirtualKeyControl, VirtualKeyC],
        ShortcutCommand.Paste => [VirtualKeyControl, VirtualKeyV],
        ShortcutCommand.Find => [VirtualKeyControl, VirtualKeyF],
        ShortcutCommand.PageUp => [VirtualKeyPageUp],
        ShortcutCommand.PageDown => [VirtualKeyPageDown],
        ShortcutCommand.MediaPlayPause => [VirtualKeyMediaPlayPause],
        ShortcutCommand.VolumeDown => [VirtualKeyVolumeDown],
        ShortcutCommand.VolumeUp => [VirtualKeyVolumeUp],
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private static Input[] Chord(ushort modifier, ushort key) =>
        [Down(modifier), Down(key), Up(key), Up(modifier)];

    private static Input[] Press(ushort key) => [Down(key), Up(key)];

    private static Input Down(ushort key) => new()
    {
        Type = KeyboardInput,
        Union = new InputUnion { Keyboard = new KeyboardInputData { VirtualKey = key } },
    };

    private static Input Up(ushort key) => new()
    {
        Type = KeyboardInput,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInputData { VirtualKey = key, Flags = KeyUp },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        // INPUT.cbSize is the size of the complete native union, whose largest
        // member is MOUSEINPUT even when this particular event is a keyboard event.
        [FieldOffset(0)] public MouseInputLayout MouseLayout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputLayout
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
