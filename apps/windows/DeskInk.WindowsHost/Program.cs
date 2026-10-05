using DeskInk.Overlay;

namespace DeskInk.WindowsHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = HasArgument(args, "--headless");
        var showUi = !headless && (args.Length == 0 || HasArgument(args, "--ui"));
        var appMode = showUi;
        var enableMouse = HasArgument(args, "--enable-mouse");
        var enablePen = HasArgument(args, "--enable-pen");
        var enableOverlay = HasArgument(args, "--enable-overlay");
        var enableAll = appMode || HasArgument(args, "--enable-all");
        var enableLan = appMode || HasArgument(args, "--enable-lan");
        if (!enableAll && new[] { enableMouse, enablePen, enableOverlay }.Count(enabled => enabled) > 1)
            throw new ArgumentException("Choose only one output: --enable-mouse, --enable-pen or --enable-overlay");

        var monitorIndex = ParseMonitorIndex(args);
        if (showUi) ApplicationConfiguration.Initialize();
        Mutex? singleInstance = null;
        if (showUi)
        {
            singleInstance = new Mutex(
                initiallyOwned: true,
                "Local\\DeskInk.WindowsApplication",
                out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "O DeskInk já está aberto. Procure o ícone na bandeja do Windows.",
                    "DeskInk",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                singleInstance.Dispose();
                return 0;
            }
        }
        MouseInputController? mouseInput = null;
        SyntheticPenController? penInput = null;
        OverlayWindowController? overlay = null;
        KeyboardShortcutController? shortcutInput = enableAll ? new KeyboardShortcutController() : null;

        try
        {
            Console.WriteLine("DeskInk Windows Host - USB/ADB + optional authenticated LAN");
            foreach (var monitor in MouseInputController.DescribeMonitors())
                Console.WriteLine($"Monitor {monitor}");

            if (enableAll)
            {
                mouseInput = MouseInputController.Create(monitorIndex);
                penInput = SyntheticPenController.Create(monitorIndex);
                overlay = new OverlayWindowController(monitorIndex);
                Console.WriteLine($"ALL OUTPUTS ENABLED monitor={monitorIndex}");
            }
            else if (enableMouse)
            {
                mouseInput = MouseInputController.Create(monitorIndex);
                Console.WriteLine($"MOUSE INJECTION ENABLED monitor={mouseInput.MonitorDescription}");
            }
            else if (enablePen)
            {
                penInput = SyntheticPenController.Create(monitorIndex);
                Console.WriteLine($"SYNTHETIC PEN ENABLED monitor={penInput.MonitorDescription}");
            }
            else if (enableOverlay)
            {
                overlay = new OverlayWindowController(monitorIndex);
                Console.WriteLine($"OVERLAY ENABLED monitor={monitorIndex}");
            }
            else
            {
                Console.WriteLine(
                    "Output disabled; pass --enable-mouse, --enable-pen or --enable-overlay [--monitor N].");
            }

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

            var host = new DeskInkHost(mouseInput, penInput, overlay, shortcutInput, enableLan);
            try
            {
                var hostTask = host.RunAsync(shutdown.Token);
                if (appMode)
                {
                    _ = Task.Run(() => Console.WriteLine(AdbBootstrapper.ConfigureUsbReverse()));
                }
                if (showUi)
                {
                    using var window = new DeskInkStatusForm(host);
                    _ = hostTask.ContinueWith(
                        _ => window.BeginInvoke(window.ExitApplication),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                    Application.Run(window);
                    shutdown.Cancel();
                }
                hostTask.GetAwaiter().GetResult();
                return 0;
            }
            finally
            {
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            penInput?.Dispose();
            overlay?.Dispose();
            singleInstance?.Dispose();
        }
    }

    private static bool HasArgument(string[] arguments, string expected) =>
        arguments.Contains(expected, StringComparer.OrdinalIgnoreCase);

    private static int ParseMonitorIndex(string[] arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!arguments[index].Equals("--monitor", StringComparison.OrdinalIgnoreCase)) continue;
            if (index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out var monitorIndex))
                throw new ArgumentException("--monitor requires a numeric index");
            return monitorIndex;
        }
        return 0;
    }
}
