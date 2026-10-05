using DeskInk.Core.Protocol;

namespace DeskInk.WindowsHost;

public sealed class DeskInkStatusForm : Form
{
    private static readonly Color Background = Color.FromArgb(17, 20, 24);
    private static readonly Color Surface = Color.FromArgb(26, 31, 36);
    private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
    private static readonly Color TextMuted = Color.FromArgb(156, 167, 178);
    private static readonly Color Connected = Color.FromArgb(74, 222, 128);
    private static readonly Color Disconnected = Color.FromArgb(148, 163, 184);

    private readonly DeskInkHost _host;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 250 };
    private readonly NotifyIcon _trayIcon;
    private readonly Label _status = ValueLabel(16);
    private readonly Label _transport = ValueLabel();
    private readonly Label _session = ValueLabel();
    private readonly Label _monitor = ValueLabel();
    private readonly Label _output = ValueLabel();
    private readonly Label _activity = ValueLabel();
    private readonly Label _metrics = ValueLabel();
    private bool _exitRequested;

    public DeskInkStatusForm(DeskInkHost host)
    {
        _host = host;
        Text = "DeskInk";
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 10F);
        ClientSize = new Size(480, 520);
        MinimumSize = new Size(440, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;

        Controls.Add(BuildLayout());

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Abrir DeskInk", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Sair", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DeskInk",
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();
        RefreshStatus();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            _refreshTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            trayMenu.Dispose();
        };
    }

    public void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = Background,
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Text = "DeskInk",
            Font = new Font("Segoe UI Semibold", 24F),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0),
        });
        _status.Location = new Point(2, 50);
        heading.Controls.Add(_status);
        root.Controls.Add(heading, 0, 0);

        root.Controls.Add(BuildSection("CONEXÃO", ("Transporte", _transport), ("Sessão", _session)), 0, 1);
        root.Controls.Add(BuildSection("SAÍDA", ("Monitor", _monitor), ("Modo ativo", _output)), 0, 2);
        root.Controls.Add(BuildSection("DIAGNÓSTICO", ("Última entrada", _activity), ("Tráfego", _metrics)), 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        actions.Controls.Add(ActionButton("Sair", (_, _) => ExitApplication()));
        actions.Controls.Add(ActionButton("Ocultar", (_, _) => HideToTray()));
        actions.Controls.Add(ActionButton("Copiar diagnóstico", (_, _) => CopyDiagnostics()));
        root.Controls.Add(actions, 0, 4);
        return root;
    }

    private static Control BuildSection(string title, params (string Name, Label Value)[] rows)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(16, 12, 16, 12),
            ColumnCount = 2,
            RowCount = rows.Length + 1,
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI Semibold", 9F),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 0)!, 2);
        for (var index = 0; index < rows.Length; index++)
        {
            panel.Controls.Add(new Label
            {
                Text = rows[index].Name,
                ForeColor = TextMuted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, index + 1);
            rows[index].Value.Dock = DockStyle.Fill;
            rows[index].Value.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(rows[index].Value, 1, index + 1);
        }
        return panel;
    }

    private static Label ValueLabel(float size = 10F) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", size),
        ForeColor = TextPrimary,
    };

    private static Button ActionButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = TextPrimary,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 4, 0, 4),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(55, 65, 75);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(37, 44, 51);
        button.Click += onClick;
        return button;
    }

    private void RefreshStatus()
    {
        var snapshot = _host.GetStatusSnapshot();
        _status.Text = snapshot.Connected ? "●  Conectado" : "●  Aguardando conexão";
        _status.ForeColor = snapshot.Connected ? Connected : Disconnected;
        _transport.Text = snapshot.Transport switch
        {
            HostTransportKind.UsbAdb => "USB / ADB",
            HostTransportKind.Lan => "LAN autenticada",
            _ => "Nenhum",
        };
        _session.Text = snapshot.SessionId is { } session ? session.ToString("X16") : "—";
        var monitors = MouseInputController.DescribeMonitors();
        _monitor.Text = snapshot.MonitorIndex < monitors.Count
            ? monitors[snapshot.MonitorIndex]
            : $"Monitor {snapshot.MonitorIndex}";
        _output.Text = snapshot.OutputMode switch
        {
            DeskInkOutputMode.Mouse => "Cursor / scroll",
            DeskInkOutputMode.SyntheticPen => "Caneta Windows",
            DeskInkOutputMode.Overlay => "Overlay",
            _ => snapshot.OutputMode.ToString(),
        };
        _activity.Text = snapshot.LastInputMillisecondsAgo is { } elapsed
            ? elapsed < 1000 ? $"há {elapsed:0} ms" : $"há {elapsed / 1000:0.0} s"
            : "Nenhuma amostra recebida";
        _metrics.Text = $"{snapshot.Frames:N0} frames  •  {snapshot.Samples:N0} amostras";
        _trayIcon.Text = snapshot.Connected ? "DeskInk — Conectado" : "DeskInk — Aguardando conexão";
    }

    private void CopyDiagnostics()
    {
        var snapshot = _host.GetStatusSnapshot();
        Clipboard.SetText(
            $"DeskInk | connected={snapshot.Connected} transport={snapshot.Transport} " +
            $"session={snapshot.SessionId?.ToString("X16") ?? "none"} monitor={snapshot.MonitorIndex} " +
            $"output={snapshot.OutputMode} frames={snapshot.Frames} samples={snapshot.Samples}");
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_exitRequested || eventArgs.CloseReason == CloseReason.WindowsShutDown) return;
        eventArgs.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
}
