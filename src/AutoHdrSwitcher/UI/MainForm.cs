using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AutoHdrSwitcher.Config;
using AutoHdrSwitcher.Logging;
using AutoHdrSwitcher.Matching;
using AutoHdrSwitcher.Monitoring;

namespace AutoHdrSwitcher.UI;

public sealed class MainForm : Form
{
    private const string AppIconResourceName = "AutoHdrSwitcher.Assets.AppIcon.ico";
    private const int MinPollSeconds = 1;
    private const int MaxPollSeconds = 30;
    private const int MainTopPanelMinSize = 120;
    private const int MainBottomPanelMinSize = 220;
    private const int RuntimeTopPanelMinSize = 60;
    private const int RuntimeBottomPanelMinSize = 60;
    private const int RuntimeMiddlePanelMinSize = 60;
    private const int RuntimeBottomSectionMinSize = 60;
    private const int DisplayRefreshIntervalMs = 1000;
    private const int TraceRecoveryRetrySeconds = 30;
    private const int DefaultEventBurstRefreshCount = 6;
    private const int TraceStartProactiveRefreshCount = 4;
    private const int FullscreenAllEventRefreshThrottleMs = 1000;
    private const int PendingStartEventRetentionSeconds = 180;
    private static readonly Icon? AppIconTemplate = LoadAppIconTemplate();

    private readonly string _configPath;
    private readonly ProcessMonitorService _monitorService = new();
    private readonly ProcessEventMonitor _processEventMonitor = new();
    private readonly WindowPlacementSettings _windowSettings = WindowPlacementSettings.Default;
    private readonly BindingList<ProcessWatchRuleRow> _ruleRows = new();
    private readonly BindingList<ProcessMatchRow> _matchRows = new();
    private readonly BindingList<FullscreenProcessRow> _fullscreenRows = new();
    private readonly BindingList<DisplayHdrRow> _displayRows = new();
    private readonly Dictionary<string, bool> _fullscreenIgnoreMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _displayAutoModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _processTargetDisplayOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, PendingStartEventInfo> _pendingStartEvents = new();
    private readonly System.Windows.Forms.Timer _monitorTimer = new();
    private readonly System.Windows.Forms.Timer _eventBurstTimer = new();
    private readonly System.Windows.Forms.Timer _displayRefreshTimer = new();

    private SplitContainer _mainSplit = null!;
    private SplitContainer _runtimeSplit = null!;
    private SplitContainer _runtimeBottomSplit = null!;
    private DataGridView _ruleGrid = null!;
    private DataGridView _matchGrid = null!;
    private DataGridView _fullscreenGrid = null!;
    private DataGridView _displayGrid = null!;
    private DataGridViewComboBoxColumn _ruleTargetDisplayColumn = null!;
    private DataGridViewComboBoxColumn _matchTargetDisplayColumn = null!;
    private GroupBox _runtimeGroup = null!;
    private GroupBox _fullscreenGroup = null!;
    private GroupBox _displayGroup = null!;
    private Label _pollLabel = null!;
    private NumericUpDown _pollSecondsInput = null!;
    private CheckBox _pollingEnabledCheck = null!;
    private CheckBox _minimizeToTrayCheck = null!;
    private CheckBox _enableLoggingCheck = null!;
    private CheckBox _monitorAllFullscreenCheck = null!;
    private CheckBox _switchAllDisplaysCheck = null!;
    private CheckBox _autoRequestAdminCheck = null!;
    private ToolStripStatusLabel _monitorStateLabel = null!;
    private ToolStripStatusLabel _snapshotLabel = null!;
    private ToolStripStatusLabel _saveStateLabel = null!;
    private ToolStripStatusLabel _configPathLabel = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private Icon _trayBaseIcon = null!;
    private Icon? _trayBadgeIcon;

    private bool _suppressDirtyTracking;
    private bool _suppressFullscreenIgnoreEvents;
    private bool _suppressDisplayHdrToggleEvents;
    private bool _suppressMatchTargetEvents;
    private bool _hasUnsavedChanges;
    private bool _refreshInFlight;
    private bool _snapshotRefreshPending;
    private bool _displayTargetOptionsRefreshPending;
    private string _displayTargetOptionsFingerprint = string.Empty;
    private string _displayTopologyFingerprint = string.Empty;
    private int _eventBurstRemaining;
    private bool _monitoringActive;
    private bool _eventStreamAvailable;
    private bool _exitRequested;
    private bool _isHiddenToTray;
    private bool _ownsTrayBaseIcon;
    private int _lastTrayMatchCount = -1;
    private long _snapshotRequestSequence;
    private int? _loadedMainSplitterDistance;
    private int? _loadedRuntimeTopSplitterDistance;
    private int? _loadedRuntimeBottomSplitterDistance;
    private DateTimeOffset _nextTraceRecoveryAttemptAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextFullscreenAllEventRefreshAt = DateTimeOffset.MinValue;

    public MainForm(string configPath)
    {
        _configPath = Path.GetFullPath(configPath);
        InitializeLayout();
        InitializeTrayIcon();
        WireUpEvents();
        LoadConfigurationFromDisk();
        PopulateDisplayPlaceholders();
        StartMonitoring();
    }

    private void InitializeLayout()
    {
        Text = $"AutoHdrSwitcher v{GetAppVersion()}";
        Width = 1200;
        Height = 760;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        var formIcon = CloneAppIcon();
        if (formIcon is not null)
        {
            Icon = formIcon;
        }

        var topPanel = BuildTopPanel();
        var splitContainer = BuildSplitContainer();
        var statusStrip = BuildStatusStrip();

        Controls.Add(splitContainer);
        Controls.Add(topPanel);
        Controls.Add(statusStrip);
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainForm).Assembly.GetName().Version;
        return version is null
            ? "unknown"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static Icon? CloneAppIcon()
    {
        return AppIconTemplate is null
            ? null
            : (Icon)AppIconTemplate.Clone();
    }

    private static Icon? LoadAppIconTemplate()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(AppIconResourceName);
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    private TableLayoutPanel BuildTopPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var addRuleButton = new Button { Text = "Add Rule", AutoSize = true };
        addRuleButton.Click += (_, _) =>
        {
            CommitPendingRuleEdits();
            _ruleRows.Add(new ProcessWatchRuleRow());
            var newIndex = _ruleRows.Count - 1;
            if (newIndex >= 0 && _ruleGrid.Columns.Count > 0)
            {
                _ruleGrid.CurrentCell = _ruleGrid.Rows[newIndex].Cells[0];
                _ruleGrid.BeginEdit(selectAll: true);
            }
            MarkDirtyAndRefreshRules("rule-added");
        };

        var removeRuleButton = new Button { Text = "Remove Selected", AutoSize = true };
        removeRuleButton.Click += (_, _) =>
        {
            RemoveSelectedRules();
        };

        var saveButton = new Button { Text = "Save Config", AutoSize = true };
        saveButton.Click += (_, _) =>
        {
            SaveConfigurationToDisk(showSuccessMessage: true);
        };

        var reloadButton = new Button { Text = "Reload Config", AutoSize = true };
        reloadButton.Click += (_, _) =>
        {
            LoadConfigurationFromDisk();
            _ = RefreshSnapshotAsync("reload-config");
        };

        _startButton = new Button { Text = "Start Monitor", AutoSize = true };
        _startButton.Click += (_, _) => StartMonitoring();

        _stopButton = new Button { Text = "Stop Monitor", AutoSize = true };
        _stopButton.Click += (_, _) => StopMonitoring();

        var refreshButton = new Button { Text = "Refresh Now", AutoSize = true };
        refreshButton.Click += (_, _) => _ = RefreshSnapshotAsync("manual-refresh-button");

        _pollLabel = new Label
        {
            Text = "Poll (sec):",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 5, 4, 0)
        };

        _pollSecondsInput = new NumericUpDown
        {
            Minimum = MinPollSeconds,
            Maximum = MaxPollSeconds,
            Value = 2,
            Width = 64,
            Margin = new Padding(0, 2, 0, 0)
        };

        _pollingEnabledCheck = new CheckBox
        {
            Text = "Enable polling",
            AutoSize = true,
            Margin = new Padding(8, 4, 0, 0)
        };
        _pollingEnabledCheck.CheckedChanged += (_, _) =>
        {
            ApplyPollingMode();
            MarkDirty();
        };

        _minimizeToTrayCheck = new CheckBox
        {
            Text = "Close to tray",
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 0)
        };
        _minimizeToTrayCheck.CheckedChanged += (_, _) => MarkDirty();

        _enableLoggingCheck = new CheckBox
        {
            Text = "Enable logging",
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 0)
        };
        _enableLoggingCheck.CheckedChanged += (_, _) =>
        {
            AppLogger.SetEnabled(_enableLoggingCheck.Checked);
            MarkDirty();
        };

        _monitorAllFullscreenCheck = new CheckBox
        {
            Text = "Auto monitor all fullscreen processes",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _monitorAllFullscreenCheck.CheckedChanged += (_, _) =>
        {
            MarkDirty();
            _ = RefreshSnapshotAsync("monitor-all-fullscreen-changed");
        };

        _switchAllDisplaysCheck = new CheckBox
        {
            Text = "Switch all displays together",
            AutoSize = true,
            Margin = new Padding(12, 4, 0, 0)
        };
        _switchAllDisplaysCheck.CheckedChanged += (_, _) =>
        {
            MarkDirty();
            _ = RefreshSnapshotAsync("switch-all-displays-changed");
        };

        _autoRequestAdminCheck = new CheckBox
        {
            Text = "Auto request admin for trace (better performance)",
            AutoSize = true,
            Margin = new Padding(12, 4, 0, 0)
        };
        _autoRequestAdminCheck.CheckedChanged += (_, _) =>
        {
            MarkDirty();
        };

        var monitorOptionsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        monitorOptionsRow.Controls.Add(_minimizeToTrayCheck);
        monitorOptionsRow.Controls.Add(_enableLoggingCheck);
        monitorOptionsRow.Controls.Add(_monitorAllFullscreenCheck);
        monitorOptionsRow.Controls.Add(_switchAllDisplaysCheck);
        monitorOptionsRow.Controls.Add(_autoRequestAdminCheck);

        var configRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        configRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        configRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        configRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var pollRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        pollRow.Controls.Add(_pollLabel);
        pollRow.Controls.Add(_pollSecondsInput);
        pollRow.Controls.Add(_pollingEnabledCheck);

        actionsRow.Controls.Add(addRuleButton);
        actionsRow.Controls.Add(removeRuleButton);
        actionsRow.Controls.Add(saveButton);
        actionsRow.Controls.Add(reloadButton);
        actionsRow.Controls.Add(_startButton);
        actionsRow.Controls.Add(_stopButton);
        actionsRow.Controls.Add(refreshButton);

        configRow.Controls.Add(monitorOptionsRow, 0, 0);
        configRow.Controls.Add(pollRow, 1, 0);

        panel.Controls.Add(actionsRow, 0, 0);
        panel.Controls.Add(configRow, 0, 1);
        return panel;
    }

    private SplitContainer BuildSplitContainer()
    {
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        var ruleGroup = new GroupBox
        {
            Text = "Process Watch Rules",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _ruleGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _ruleRows,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ShowCellToolTips = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true
        };
        ConfigureGridStyle(_ruleGrid);
        _ruleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Pattern",
            DataPropertyName = nameof(ProcessWatchRuleRow.Pattern),
            ToolTipText = string.Empty,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Exact Match",
            DataPropertyName = nameof(ProcessWatchRuleRow.ExactMatch),
            ToolTipText = string.Empty,
            Width = 95
        });
        _ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Case Sensitive",
            DataPropertyName = nameof(ProcessWatchRuleRow.CaseSensitive),
            ToolTipText = string.Empty,
            Width = 105
        });
        _ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Regex Mode",
            DataPropertyName = nameof(ProcessWatchRuleRow.RegexMode),
            ToolTipText = string.Empty,
            Width = 90
        });
        _ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Enabled",
            DataPropertyName = nameof(ProcessWatchRuleRow.Enabled),
            ToolTipText = string.Empty,
            Width = 75
        });
        _ruleTargetDisplayColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Target Display",
            DataPropertyName = nameof(ProcessWatchRuleRow.TargetDisplay),
            ToolTipText = string.Empty,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            Width = 220
        };
        _ruleGrid.Columns.Add(_ruleTargetDisplayColumn);
        ruleGroup.Controls.Add(_ruleGrid);

        _runtimeGroup = new GroupBox
        {
            Text = "Runtime Status (Matches: 0)",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _runtimeSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        _runtimeBottomSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        _matchGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _matchRows,
            ReadOnly = false,
            TabStop = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ShowCellToolTips = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        ConfigureGridStyle(_matchGrid);
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "PID",
            DataPropertyName = nameof(ProcessMatchRow.ProcessId),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 85
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Process",
            DataPropertyName = nameof(ProcessMatchRow.ProcessName),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 190
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Display",
            DataPropertyName = nameof(ProcessMatchRow.Display),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 145
        });
        _matchTargetDisplayColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Target Display",
            DataPropertyName = nameof(ProcessMatchRow.TargetDisplay),
            ToolTipText = string.Empty,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            ReadOnly = false,
            Width = 220
        };
        _matchGrid.Columns.Add(_matchTargetDisplayColumn);
        _matchGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Fullscreen",
            DataPropertyName = nameof(ProcessMatchRow.FullscreenLike),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 85
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Rule Pattern",
            DataPropertyName = nameof(ProcessMatchRow.RulePattern),
            ToolTipText = string.Empty,
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Mode",
            DataPropertyName = nameof(ProcessMatchRow.Mode),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 230
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Matched Input",
            DataPropertyName = nameof(ProcessMatchRow.MatchInput),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 230
        });
        _runtimeSplit.Panel1.Controls.Add(_matchGrid);

        _fullscreenGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _fullscreenRows,
            ReadOnly = false,
            TabStop = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ShowCellToolTips = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        ConfigureGridStyle(_fullscreenGrid);
        _fullscreenGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "PID",
            DataPropertyName = nameof(FullscreenProcessRow.ProcessId),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 85
        });
        _fullscreenGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Process",
            DataPropertyName = nameof(FullscreenProcessRow.ProcessName),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 220
        });
        _fullscreenGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Executable",
            DataPropertyName = nameof(FullscreenProcessRow.ExecutablePath),
            ToolTipText = string.Empty,
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _fullscreenGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Display",
            DataPropertyName = nameof(FullscreenProcessRow.Display),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 145
        });
        _fullscreenGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Matched Rule",
            DataPropertyName = nameof(FullscreenProcessRow.MatchedByRule),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 95
        });
        _fullscreenGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Ignore",
            DataPropertyName = nameof(FullscreenProcessRow.Ignore),
            ToolTipText = string.Empty,
            ReadOnly = false,
            Width = 85
        });
        _fullscreenGroup = new GroupBox
        {
            Text = "Detected Fullscreen Processes (Fullscreen: 0)",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        _fullscreenGroup.Controls.Add(_fullscreenGrid);
        _runtimeBottomSplit.Panel1.Controls.Add(_fullscreenGroup);

        _displayGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _displayRows,
            ReadOnly = false,
            TabStop = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ShowCellToolTips = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        ConfigureGridStyle(_displayGrid);
        _displayGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Display",
            DataPropertyName = nameof(DisplayHdrRow.Display),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 145
        });
        _displayGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Monitor",
            DataPropertyName = nameof(DisplayHdrRow.FriendlyName),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 260
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Supported",
            DataPropertyName = nameof(DisplayHdrRow.Supported),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 85
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Primary",
            DataPropertyName = nameof(DisplayHdrRow.Primary),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 75
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Auto",
            DataPropertyName = nameof(DisplayHdrRow.AutoMode),
            ToolTipText = string.Empty,
            ReadOnly = false,
            Width = 65
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "HDR On",
            DataPropertyName = nameof(DisplayHdrRow.HdrEnabled),
            ToolTipText = string.Empty,
            ReadOnly = false,
            Width = 75
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Desired",
            DataPropertyName = nameof(DisplayHdrRow.DesiredHdr),
            ToolTipText = string.Empty,
            ReadOnly = true,
            Width = 75
        });
        _displayGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Action",
            DataPropertyName = nameof(DisplayHdrRow.Action),
            ToolTipText = string.Empty,
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 260
        });
        _displayGroup = new GroupBox
        {
            Text = $"Display HDR Status ({BuildHdrSummary(Array.Empty<HdrDisplayStatus>())})",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        _displayGroup.Controls.Add(_displayGrid);
        _runtimeBottomSplit.Panel2.Controls.Add(_displayGroup);
        _runtimeSplit.Panel2.Controls.Add(_runtimeBottomSplit);
        _runtimeGroup.Controls.Add(_runtimeSplit);

        _mainSplit.Panel1.Controls.Add(ruleGroup);
        _mainSplit.Panel2.Controls.Add(_runtimeGroup);
        return _mainSplit;
    }

    private StatusStrip BuildStatusStrip()
    {
        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            ShowItemToolTips = false
        };

        _monitorStateLabel = new ToolStripStatusLabel("Monitor: starting...");
        _snapshotLabel = new ToolStripStatusLabel("Last scan: n/a");
        _saveStateLabel = new ToolStripStatusLabel("Config: clean");
        _configPathLabel = new ToolStripStatusLabel(_configPath);

        statusStrip.Items.Add(_monitorStateLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_snapshotLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_saveStateLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | Config: "));
        statusStrip.Items.Add(_configPathLabel);
        return statusStrip;
    }

    private void InitializeTrayIcon()
    {
        var clonedIcon = CloneAppIcon();
        _trayBaseIcon = clonedIcon ?? SystemIcons.Application;
        _ownsTrayBaseIcon = clonedIcon is not null;

        _trayMenu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _exitRequested = true;
            Close();
        };
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _trayBaseIcon,
            Text = "AutoHdrSwitcher",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }

            if (_isHiddenToTray)
            {
                RestoreFromTray();
                return;
            }

            MinimizeToTray();
        };

        UpdateTrayMatchIndicator(0);
    }

    private void WireUpEvents()
    {
        _monitorTimer.Interval = 2000;
        _monitorTimer.Tick += async (_, _) => await RefreshSnapshotAsync("poll-timer");
        _displayRefreshTimer.Interval = DisplayRefreshIntervalMs;
        _displayRefreshTimer.Tick += async (_, _) =>
        {
            if (_monitoringActive)
            {
                MaybeRecoverTraceEventStream();
                return;
            }

            await RefreshDisplaySnapshotAsync();
        };
        _displayRefreshTimer.Start();
        _eventBurstTimer.Interval = 180;
        _eventBurstTimer.Tick += (_, _) =>
        {
            if (_eventBurstRemaining <= 0)
            {
                _eventBurstTimer.Stop();
                AppLogger.Info("Event burst timer completed.");
                return;
            }

            _eventBurstRemaining--;
            AppLogger.Info($"Event burst tick. remaining={_eventBurstRemaining}");
            _ = RefreshSnapshotAsync("event-burst-timer");
        };
        _processEventMonitor.ProcessEventReceived += ProcessEventMonitorOnProcessEventReceived;
        _processEventMonitor.StreamModeChanged += ProcessEventMonitorOnStreamModeChanged;

        _pollSecondsInput.ValueChanged += (_, _) =>
        {
            _monitorTimer.Interval = (int)_pollSecondsInput.Value * 1000;
            ApplyPollingMode();
            MarkDirty();
        };

        _ruleGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (!_ruleGrid.IsCurrentCellDirty || _ruleGrid.CurrentCell is null)
            {
                return;
            }

            var column = _ruleGrid.Columns[_ruleGrid.CurrentCell.ColumnIndex];
            // Only commit checkbox/combobox cells immediately.
            // For text cells (Pattern), defer commit until edit ends to avoid partial-input matching churn.
            if (column is not DataGridViewCheckBoxColumn &&
                column is not DataGridViewComboBoxColumn)
            {
                return;
            }

            _ruleGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _ruleGrid.CellValueChanged += (_, e) => HandleRuleGridValueChanged(e.RowIndex, e.ColumnIndex);
        _ruleGrid.CellEndEdit += (_, _) =>
        {
            SaveIfDirtyOnFocusLost();
            TryApplyPendingDisplayTargetDropdownRefresh();
        };
        _ruleGrid.Leave += (_, _) => SaveIfDirtyOnFocusLost();
        _ruleGrid.RowsRemoved += (_, _) => MarkDirty();
        _ruleGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            SetSaveStatus($"Input error: {eventArgs.Exception?.Message ?? "invalid value"}");
        };
        _ruleGrid.CellClick += (_, e) => TryOpenComboBoxOnFirstClick(_ruleGrid, e.RowIndex, e.ColumnIndex);
        _matchGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_matchGrid.IsCurrentCellDirty)
            {
                _matchGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _matchGrid.CellValueChanged += (_, e) => _ = HandleMatchGridValueChangedAsync(e.RowIndex, e.ColumnIndex);
        _matchGrid.CellEndEdit += (_, _) => TryApplyPendingDisplayTargetDropdownRefresh();
        _matchGrid.CellClick += (_, e) => TryOpenComboBoxOnFirstClick(_matchGrid, e.RowIndex, e.ColumnIndex);
        _matchGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            SetMonitorStatus($"Matched process input error: {eventArgs.Exception?.Message ?? "invalid value"}");
        };
        _fullscreenGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_fullscreenGrid.IsCurrentCellDirty)
            {
                _fullscreenGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _fullscreenGrid.CellValueChanged += (_, e) => HandleFullscreenIgnoreChanged(e.RowIndex, e.ColumnIndex);
        _displayGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_displayGrid.IsCurrentCellDirty)
            {
                _displayGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _displayGrid.CellValueChanged += (_, e) => _ = HandleDisplayGridValueChangedAsync(e.RowIndex, e.ColumnIndex);
        _displayGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            SetMonitorStatus($"Display HDR input error: {eventArgs.Exception?.Message ?? "invalid value"}");
        };
        _pollSecondsInput.Leave += (_, _) => SaveIfDirtyOnFocusLost();
        Deactivate += (_, _) =>
        {
            if (!_isHiddenToTray)
            {
                SaveIfDirtyOnFocusLost();
            }
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && !_exitRequested)
            {
                MinimizeToTray();
            }
        };
        Shown += (_, _) =>
        {
            ApplyWindowPlacementFromUserSettings();
            ApplyDefaultOrSavedSplitLayoutSafely();
            ClearAllGridSelections();
        };
        _mainSplit.SplitterMoved += (_, _) =>
        {
            MarkDirty();
        };
        _runtimeSplit.SplitterMoved += (_, _) =>
        {
            MarkDirty();
        };
        _runtimeBottomSplit.SplitterMoved += (_, _) =>
        {
            MarkDirty();
        };

        _matchGrid.SelectionChanged += (_, _) =>
        {
            if (_matchGrid.Focused)
            {
                return;
            }

            ClearPassiveGridSelection(_matchGrid);
        };
        _displayGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_displayGrid);
        _fullscreenGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_fullscreenGrid);

        FormClosing += (_, e) =>
        {
            if (!_exitRequested &&
                _minimizeToTrayCheck.Checked &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        MinimizeToTray();
                    }
                }));
                return;
            }

            _eventBurstTimer.Stop();
            _displayRefreshTimer.Stop();
            _processEventMonitor.Stop();
            _processEventMonitor.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayBadgeIcon?.Dispose();
            _trayBadgeIcon = null;
            if (_ownsTrayBaseIcon)
            {
                _trayBaseIcon.Dispose();
            }
            _trayMenu.Dispose();
            SaveWindowPlacementToUserSettings();
            if (_hasUnsavedChanges)
            {
                SaveConfigurationToDisk(showSuccessMessage: false);
            }
        };
    }

    private void RemoveSelectedRules()
    {
        if (_ruleGrid.SelectedRows.Count == 0)
        {
            return;
        }

        var indexes = _ruleGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(static row => !row.IsNewRow)
            .Select(static row => row.Index)
            .OrderByDescending(static index => index)
            .ToArray();

        foreach (var index in indexes)
        {
            if (index >= 0 && index < _ruleRows.Count)
            {
                _ruleRows.RemoveAt(index);
            }
        }

        MarkDirtyAndRefreshRules("rule-removed");
    }

    private void HandleRuleGridValueChanged(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
        {
            return;
        }

        MarkDirtyAndRefreshRules("rule-grid-value-changed");
    }

    private void MarkDirtyAndRefreshRules(string refreshReason)
    {
        MarkDirty();
        TriggerRuleRefreshIfMonitoring(refreshReason);
    }

    private void TriggerRuleRefreshIfMonitoring(string reason)
    {
        if (!_monitoringActive || _suppressDirtyTracking)
        {
            return;
        }

        _ = RefreshSnapshotAsync(reason);
    }

    private void MarkDirty()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        _hasUnsavedChanges = true;
        SetSaveStatus("Config: unsaved changes");
    }

    private void StartMonitoring()
    {
        AppLogger.Info("Start monitoring requested.");
        _nextFullscreenAllEventRefreshAt = DateTimeOffset.MinValue;
        _eventBurstRemaining = 0;
        _eventStreamAvailable = EnsureProcessEventsStarted();
        _monitoringActive = true;
        ApplyPollingMode();
        AppLogger.Info(
            $"Monitoring started. pollingEnabled={_pollingEnabledCheck.Checked}; pollIntervalMs={_monitorTimer.Interval}; eventMode={_processEventMonitor.CurrentMode}");
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        SetMonitorStatus(GetMonitoringModeLabel());
        _ = RefreshSnapshotAsync("start-monitoring");
    }

    private void StopMonitoring()
    {
        AppLogger.Info("Stop monitoring requested.");
        _monitoringActive = false;
        _monitorTimer.Stop();
        _eventBurstTimer.Stop();
        _eventBurstRemaining = 0;
        _snapshotRefreshPending = false;
        _pendingStartEvents.Clear();
        _nextFullscreenAllEventRefreshAt = DateTimeOffset.MinValue;
        _processEventMonitor.Stop();
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        SetMonitorStatus(GetMonitoringModeLabel());
        _ = RefreshDisplaySnapshotAsync();
    }

    private async Task RefreshSnapshotAsync(string reason, ProcessEventNotification? triggerEvent = null)
    {
        var requestId = Interlocked.Increment(ref _snapshotRequestSequence);
        if (_refreshInFlight)
        {
            _snapshotRefreshPending = true;
            AppLogger.Info(
                $"Snapshot refresh queued. requestId={requestId}; reason={reason}; pending=true; triggerSeq={DescribeTriggerSequence(triggerEvent)}");
            return;
        }

        _refreshInFlight = true;
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var rules = BuildRulesFromUi(commitEdits: false);
            AppLogger.Info(
                $"Snapshot refresh start. requestId={requestId}; reason={reason}; rules={rules.Count}; monitorAllFullscreen={_monitorAllFullscreenCheck.Checked}; switchAllDisplays={_switchAllDisplaysCheck.Checked}; trigger={DescribeTrigger(triggerEvent)}");
            var snapshot = await Task.Run(
                () => _monitorService.Evaluate(
                    rules,
                    _monitorAllFullscreenCheck.Checked,
                    _fullscreenIgnoreMap,
                    _switchAllDisplaysCheck.Checked,
                    _displayAutoModes,
                    _processTargetDisplayOverrides));
            stopwatch.Stop();
            AppLogger.Info(
                $"Snapshot refresh evaluated. requestId={requestId}; reason={reason}; durationMs={stopwatch.Elapsed.TotalMilliseconds:F3}; processes={snapshot.ProcessCount}; matches={snapshot.Matches.Count}; fullscreen={snapshot.FullscreenProcesses.Count}; displays={snapshot.Displays.Count}");
            ApplySnapshot(snapshot, rules.Count);
            LogMatchedStartEventLatencies(snapshot, startedAtUtc);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppLogger.Error(
                $"Snapshot refresh failed. requestId={requestId}; reason={reason}; durationMs={stopwatch.Elapsed.TotalMilliseconds:F3}",
                ex);
            SetMonitorStatus($"Monitor error: {ex.Message}");
        }
        finally
        {
            _refreshInFlight = false;
            if (_snapshotRefreshPending)
            {
                _snapshotRefreshPending = false;
                AppLogger.Info($"Executing queued snapshot refresh after requestId={requestId}.");
                _ = RefreshSnapshotAsync("queued-after-inflight");
            }
        }
    }

    private async Task RefreshDisplaySnapshotAsync()
    {
        if (_refreshInFlight)
        {
            return;
        }

        _refreshInFlight = true;
        try
        {
            var displays = await Task.Run(() => _monitorService.GetLiveDisplayHdrStates(_displayAutoModes));
            ApplyDisplayRows(displays);
            ClearPassiveGridSelection(_displayGrid);
            var hdrSummary = BuildHdrSummary(displays);
            _displayGroup.Text = $"Display HDR Status ({hdrSummary})";
            _snapshotLabel.Text = $"Last display refresh: {DateTimeOffset.Now:HH:mm:ss}";
            SetMonitorStatus($"{GetMonitoringModeLabel()} | {hdrSummary}");
        }
        catch (Exception ex)
        {
            SetMonitorStatus($"Display refresh error: {ex.Message}");
        }
        finally
        {
            _refreshInFlight = false;
            if (_monitoringActive)
            {
                _ = RefreshSnapshotAsync("resume-monitoring-after-display-refresh");
            }
        }
    }

    private List<ProcessWatchRule> BuildRulesFromUi(bool commitEdits)
    {
        if (commitEdits)
        {
            CommitPendingRuleEdits();
        }

        return _ruleRows
            .Select(static row => row.ToRule())
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();
    }

    private void CommitPendingRuleEdits()
    {
        if (_ruleGrid.IsCurrentCellInEditMode)
        {
            _ruleGrid.EndEdit();
        }

        var context = BindingContext;
        if (context is not null && context[_ruleRows] is CurrencyManager manager)
        {
            manager.EndCurrentEdit();
        }
    }

    private void ApplySnapshot(ProcessMonitorSnapshot snapshot, int activeRuleCount)
    {
        _suppressMatchTargetEvents = true;
        try
        {
            _matchRows.Clear();
            foreach (var match in snapshot.Matches)
            {
                _matchRows.Add(new ProcessMatchRow
                {
                    RuleIndex = match.RuleIndex,
                    ProcessTargetKey = match.ProcessTargetKey,
                    HasProcessTargetOverride = match.HasProcessTargetOverride,
                    ProcessId = match.ProcessId,
                    ProcessName = match.ProcessName,
                    Display = match.Display,
                    FullscreenLike = match.IsFullscreenLike,
                    RulePattern = match.RulePattern,
                    Mode = match.Mode,
                    MatchInput = match.MatchInput,
                    TargetDisplay = ProcessWatchRuleRow.NormalizeTargetDisplayValue(match.EffectiveTargetDisplay)
                });
            }
        }
        finally
        {
            _suppressMatchTargetEvents = false;
        }

        _fullscreenRows.Clear();
        _suppressFullscreenIgnoreEvents = true;
        foreach (var fullscreen in snapshot.FullscreenProcesses)
        {
            if (fullscreen.IsDefaultIgnoreApplied &&
                !_fullscreenIgnoreMap.ContainsKey(fullscreen.IgnoreKey))
            {
                _fullscreenIgnoreMap[fullscreen.IgnoreKey] = true;
                MarkDirty();
            }

            _fullscreenRows.Add(new FullscreenProcessRow
            {
                ProcessId = fullscreen.ProcessId,
                ProcessName = fullscreen.ProcessName,
                ExecutablePath = fullscreen.ExecutablePath,
                Display = fullscreen.Display,
                MatchedByRule = fullscreen.MatchedByRule,
                Ignore = ResolveIgnoreFromMap(fullscreen),
                IgnoreKey = fullscreen.IgnoreKey,
                IsDefaultIgnoreApplied = fullscreen.IsDefaultIgnoreApplied
            });
        }
        _suppressFullscreenIgnoreEvents = false;

        ApplyDisplayRows(snapshot.Displays);

        ClearPassiveGridSelection(_matchGrid);
        ClearPassiveGridSelection(_fullscreenGrid);
        ClearPassiveGridSelection(_displayGrid);
        if (!_ruleGrid.IsCurrentCellInEditMode && !_ruleGrid.Focused)
        {
            _ruleGrid.ClearSelection();
        }

        UpdateTrayMatchIndicator(GetTrayIndicatorMatchCount(snapshot));
        _runtimeGroup.Text = $"Runtime Status (Matches: {snapshot.Matches.Count})";
        _fullscreenGroup.Text = $"Detected Fullscreen Processes (Fullscreen: {snapshot.FullscreenProcesses.Count})";
        var hdrSummary = BuildHdrSummary(snapshot.Displays);
        _displayGroup.Text = $"Display HDR Status ({hdrSummary})";
        _snapshotLabel.Text = $"Last scan: {snapshot.CollectedAt:HH:mm:ss} | Processes: {snapshot.ProcessCount}";
        var modeLabel = GetMonitoringModeLabel();
        if (activeRuleCount == 0)
        {
            SetMonitorStatus($"{modeLabel} (no rules configured) | {hdrSummary}");
            return;
        }

        SetMonitorStatus(snapshot.Matches.Count == 0
            ? $"{modeLabel} | {hdrSummary}"
            : $"{modeLabel} ({snapshot.Matches.Count} match(es)) | {hdrSummary}");
    }

    private void ApplyDisplayRows(IReadOnlyList<HdrDisplayStatus> displays)
    {
        _suppressDisplayHdrToggleEvents = true;
        try
        {
            _displayRows.Clear();
            foreach (var display in displays)
            {
                _displayRows.Add(new DisplayHdrRow
                {
                    Display = display.Display,
                    FriendlyName = display.FriendlyName,
                    Primary = display.IsPrimary,
                    Supported = display.IsHdrSupported,
                    AutoMode = GetDisplayAutoMode(display.Display),
                    HdrEnabled = display.IsHdrEnabled,
                    DesiredHdr = display.DesiredHdrEnabled,
                    Action = display.LastAction
                });
            }
        }
        finally
        {
            _suppressDisplayHdrToggleEvents = false;
        }

        RefreshDisplayTargetDropdownOptionsIfDisplayTopologyChanged();
    }

    private void LoadConfigurationFromDisk()
    {
        var loaded = TryLoadConfigurationResilient();
        var addedDefaultIgnoreEntry = false;
        _suppressDirtyTracking = true;
        try
        {
            _ruleRows.Clear();
            foreach (var rule in loaded.ProcessRules)
            {
                _ruleRows.Add(ProcessWatchRuleRow.FromRule(rule));
            }
            RefreshDisplayTargetDropdownOptions();

            var pollSeconds = loaded.PollIntervalSeconds;
            if (pollSeconds < MinPollSeconds || pollSeconds > MaxPollSeconds)
            {
                pollSeconds = 2;
            }

            _pollSecondsInput.Value = pollSeconds;
            _pollingEnabledCheck.Checked = loaded.PollingEnabled;
            _minimizeToTrayCheck.Checked = loaded.MinimizeToTray;
            _enableLoggingCheck.Checked = loaded.EnableLogging;
            _monitorAllFullscreenCheck.Checked = loaded.MonitorAllFullscreenProcesses;
            _switchAllDisplaysCheck.Checked = loaded.SwitchAllDisplaysTogether;
            _autoRequestAdminCheck.Checked = loaded.AutoRequestAdminForTrace;
            _monitorTimer.Interval = pollSeconds * 1000;
            ApplyPollingMode();
            _fullscreenIgnoreMap.Clear();
            foreach (var entry in loaded.FullscreenIgnoreMap)
            {
                _fullscreenIgnoreMap[entry.Key] = entry.Value;
            }
            _displayAutoModes.Clear();
            foreach (var entry in loaded.DisplayAutoModes)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                if (entry.Value)
                {
                    continue;
                }

                _displayAutoModes[entry.Key] = false;
            }
            _processTargetDisplayOverrides.Clear();
            foreach (var entry in loaded.ProcessTargetDisplayOverrides)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) ||
                    string.IsNullOrWhiteSpace(entry.Value))
                {
                    continue;
                }

                var normalizedTarget = ProcessWatchRuleRow.NormalizeTargetDisplayValue(entry.Value);
                if (ProcessWatchRuleRow.IsDefaultTargetDisplayValue(normalizedTarget))
                {
                    continue;
                }

                _processTargetDisplayOverrides[entry.Key.Trim()] = normalizedTarget;
            }
            foreach (var defaultIgnoreKey in ProcessMonitorService.DefaultIgnoreKeys)
            {
                if (_fullscreenIgnoreMap.ContainsKey(defaultIgnoreKey))
                {
                    continue;
                }

                _fullscreenIgnoreMap[defaultIgnoreKey] = true;
                addedDefaultIgnoreEntry = true;
            }
            _loadedMainSplitterDistance = loaded.MainSplitterDistance;
            _loadedRuntimeTopSplitterDistance = loaded.RuntimeTopSplitterDistance;
            _loadedRuntimeBottomSplitterDistance = loaded.RuntimeBottomSplitterDistance;
            if (IsHandleCreated)
            {
                ApplyDefaultOrSavedSplitLayoutSafely();
            }
            _hasUnsavedChanges = addedDefaultIgnoreEntry;
            SetSaveStatus(addedDefaultIgnoreEntry
                ? "Config: loaded (default fullscreen ignores added)"
                : "Config: loaded");
            _ruleGrid.ClearSelection();
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private WatchConfiguration TryLoadConfigurationResilient()
    {
        try
        {
            return WatchConfigurationLoader.LoadOrCreate(_configPath);
        }
        catch (Exception ex)
        {
            var backupPath = TryBackupCorruptConfig();
            var fallback = new WatchConfiguration();
            string persistMessage;
            try
            {
                WatchConfigurationLoader.SaveToFile(_configPath, fallback);
                persistMessage = "A new config file has been created.";
            }
            catch (Exception saveEx)
            {
                persistMessage = $"Failed to write fallback config: {saveEx.Message}";
            }

            var backupInfo = string.IsNullOrWhiteSpace(backupPath)
                ? "No backup file was created."
                : $"Backup: {backupPath}";
            MessageBox.Show(
                $"Config file was invalid and has been reset.\n{backupInfo}\n{persistMessage}\n\nError: {ex.Message}",
                "Config Reset",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return fallback;
        }
    }

    private string? TryBackupCorruptConfig()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        try
        {
            var backupPath = $"{_configPath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Move(_configPath, backupPath, overwrite: true);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    private void SaveConfigurationToDisk(bool showSuccessMessage)
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        try
        {
            var config = new WatchConfiguration
            {
                PollIntervalSeconds = (int)_pollSecondsInput.Value,
                PollingEnabled = _pollingEnabledCheck.Checked,
                MinimizeToTray = _minimizeToTrayCheck.Checked,
                EnableLogging = _enableLoggingCheck.Checked,
                AutoRequestAdminForTrace = _autoRequestAdminCheck.Checked,
                MonitorAllFullscreenProcesses = _monitorAllFullscreenCheck.Checked,
                SwitchAllDisplaysTogether = _switchAllDisplaysCheck.Checked,
                MainSplitterDistance = GetCurrentSplitterDistance(_mainSplit),
                RuntimeTopSplitterDistance = GetCurrentSplitterDistance(_runtimeSplit),
                RuntimeBottomSplitterDistance = GetCurrentSplitterDistance(_runtimeBottomSplit),
                FullscreenIgnoreMap = new Dictionary<string, bool>(_fullscreenIgnoreMap, StringComparer.OrdinalIgnoreCase),
                DisplayAutoModes = BuildDisplayAutoModesConfigMap(),
                ProcessTargetDisplayOverrides = BuildProcessTargetDisplayOverridesConfigMap(),
                ProcessRules = BuildRulesFromUi(commitEdits: true)
            };
            WatchConfigurationLoader.SaveToFile(_configPath, config);
            _hasUnsavedChanges = false;
            if (showSuccessMessage)
            {
                SetSaveStatus($"Config: saved at {DateTime.Now:HH:mm:ss}");
            }
            else
            {
                SetSaveStatus("Config: saved");
            }
        }
        catch (Exception ex)
        {
            SetSaveStatus($"Config save failed: {ex.Message}");
        }
    }

    private void SaveIfDirtyOnFocusLost()
    {
        if (_hasUnsavedChanges)
        {
            SaveConfigurationToDisk(showSuccessMessage: false);
        }
    }

    private void SetMonitorStatus(string text)
    {
        _monitorStateLabel.Text = text;
    }

    private void SetSaveStatus(string text)
    {
        _saveStateLabel.Text = text;
    }

    private string GetEventSourceLabel()
    {
        return _processEventMonitor.CurrentMode switch
        {
            ProcessEventStreamMode.Trace => "trace",
            ProcessEventStreamMode.Instance => "instance",
            _ => "unknown"
        };
    }

    private void MaybeRecoverTraceEventStream()
    {
        if (_processEventMonitor.CurrentMode != ProcessEventStreamMode.Instance)
        {
            return;
        }

        if (_processEventMonitor.IsTraceRetrySuppressed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextTraceRecoveryAttemptAt)
        {
            return;
        }

        _nextTraceRecoveryAttemptAt = now.AddSeconds(TraceRecoveryRetrySeconds);
        AppLogger.Info(
            $"Attempting background trace recovery. nowUtc={now:O}; nextAttemptUtc={_nextTraceRecoveryAttemptAt:O}");
        if (!_processEventMonitor.TrySwitchToTrace(out var error))
        {
            AppLogger.Warn($"Background trace recovery attempt failed: {error}");
            return;
        }

        AppLogger.Info("Background trace recovery succeeded.");
    }

    private void ApplyPollingMode()
    {
        var shouldPoll = _monitoringActive && _pollingEnabledCheck.Checked;
        if (shouldPoll)
        {
            _monitorTimer.Start();
        }
        else
        {
            _monitorTimer.Stop();
        }

        _pollSecondsInput.Enabled = _pollingEnabledCheck.Checked;
        _pollLabel.Enabled = _pollingEnabledCheck.Checked;
    }

    private string GetMonitoringModeLabel()
    {
        if (!_monitoringActive)
        {
            return "Monitor: stopped";
        }

        if (!_eventStreamAvailable)
        {
            return _monitorTimer.Enabled
                ? "Monitor: running (polling fallback; event stream unavailable)"
                : "Monitor: running (event stream unavailable)";
        }

        var eventSource = GetEventSourceLabel();
        return _monitorTimer.Enabled
            ? $"Monitor: running ({eventSource} events + polling)"
            : $"Monitor: running ({eventSource} events)";
    }

    private void ApplyWindowPlacementFromUserSettings()
    {
        if (!_windowSettings.HasBounds)
        {
            return;
        }

        if (_windowSettings.Width <= 0 || _windowSettings.Height <= 0)
        {
            return;
        }

        var loadedBounds = new Rectangle(
            _windowSettings.X,
            _windowSettings.Y,
            _windowSettings.Width,
            _windowSettings.Height);
        var normalizedBounds = NormalizeWindowBoundsToVisibleArea(loadedBounds);
        StartPosition = FormStartPosition.Manual;
        Bounds = normalizedBounds;
        if (_windowSettings.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowPlacementToUserSettings()
    {
        var bounds = GetPersistableWindowBounds();
        if (bounds is null)
        {
            return;
        }

        _windowSettings.HasBounds = true;
        _windowSettings.X = bounds.Value.X;
        _windowSettings.Y = bounds.Value.Y;
        _windowSettings.Width = bounds.Value.Width;
        _windowSettings.Height = bounds.Value.Height;
        _windowSettings.Maximized = WindowState == FormWindowState.Maximized;
        _windowSettings.Save();
    }

    private Rectangle NormalizeWindowBoundsToVisibleArea(Rectangle bounds)
    {
        var primary = Screen.PrimaryScreen?.WorkingArea
            ?? Screen.AllScreens.FirstOrDefault()?.WorkingArea
            ?? new Rectangle(0, 0, 1200, 800);

        var minWidth = Math.Max(MinimumSize.Width, 640);
        var minHeight = Math.Max(MinimumSize.Height, 480);
        var width = Math.Min(Math.Max(bounds.Width, minWidth), primary.Width);
        var height = Math.Min(Math.Max(bounds.Height, minHeight), primary.Height);
        var normalized = new Rectangle(bounds.X, bounds.Y, width, height);
        var intersectsAnyScreen = Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(normalized));
        if (intersectsAnyScreen)
        {
            return normalized;
        }

        var centeredX = primary.Left + Math.Max(0, (primary.Width - width) / 2);
        var centeredY = primary.Top + Math.Max(0, (primary.Height - height) / 2);
        return new Rectangle(centeredX, centeredY, width, height);
    }

    private Rectangle? GetPersistableWindowBounds()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return null;
        }

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        return bounds;
    }

    private void ApplyDefaultOrSavedSplitLayout()
    {
        ApplySplitterDistanceSafe(
            _mainSplit,
            _loadedMainSplitterDistance ?? GetDefaultMainSplitterDistance(),
            MainTopPanelMinSize,
            MainBottomPanelMinSize);
        var defaultBottomPanelHeight = GetTwoRowPanelHeight();
        var defaultTopDistance = GetDefaultTopSplitterDistance(defaultBottomPanelHeight);
        ApplySplitterDistanceSafe(
            _runtimeSplit,
            _loadedRuntimeTopSplitterDistance ?? defaultTopDistance,
            RuntimeTopPanelMinSize,
            RuntimeBottomPanelMinSize);
        ApplySplitterDistanceSafe(
            _runtimeBottomSplit,
            _loadedRuntimeBottomSplitterDistance ?? defaultBottomPanelHeight,
            RuntimeMiddlePanelMinSize,
            RuntimeBottomSectionMinSize);
    }

    private void ApplyDefaultOrSavedSplitLayoutSafely()
    {
        var wasSuppressing = _suppressDirtyTracking;
        _suppressDirtyTracking = true;
        try
        {
            ApplyDefaultOrSavedSplitLayout();
        }
        finally
        {
            _suppressDirtyTracking = wasSuppressing;
        }
    }

    private int GetDefaultTopSplitterDistance(int bottomPanelHeight)
    {
        var desiredBottom = (bottomPanelHeight * 2) + _runtimeBottomSplit.SplitterWidth + 24;
        var fallback = _runtimeSplit.Height - desiredBottom;
        return Math.Max(120, fallback);
    }

    private static int GetDefaultMainSplitterDistance()
    {
        return 260;
    }

    private static int GetTwoRowPanelHeight()
    {
        return 92;
    }

    private static int? GetCurrentSplitterDistance(SplitContainer split)
    {
        if (!split.IsHandleCreated || split.Width <= 0 || split.Height <= 0)
        {
            return null;
        }

        return split.SplitterDistance;
    }

    private static void ApplySplitterDistanceSafe(
        SplitContainer split,
        int distance,
        int panel1MinSize,
        int panel2MinSize)
    {
        var maxDistance = split.Orientation == Orientation.Horizontal
            ? split.Height - panel2MinSize - split.SplitterWidth
            : split.Width - panel2MinSize - split.SplitterWidth;
        var minDistance = panel1MinSize;
        if (maxDistance < minDistance)
        {
            return;
        }

        var clamped = Math.Max(minDistance, Math.Min(distance, maxDistance));
        if (split.SplitterDistance != clamped)
        {
            split.SplitterDistance = clamped;
        }
    }

    private void HandleFullscreenIgnoreChanged(int rowIndex, int columnIndex)
    {
        if (_suppressFullscreenIgnoreEvents || rowIndex < 0 || rowIndex >= _fullscreenRows.Count)
        {
            return;
        }

        var ignoreColumn = _fullscreenGrid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(static c => c.DataPropertyName == nameof(FullscreenProcessRow.Ignore));
        if (ignoreColumn is null || columnIndex != ignoreColumn.Index)
        {
            return;
        }

        var row = _fullscreenRows[rowIndex];
        if (string.IsNullOrWhiteSpace(row.IgnoreKey))
        {
            return;
        }

        _fullscreenIgnoreMap[row.IgnoreKey] = row.Ignore;
        MarkDirty();
        _ = RefreshSnapshotAsync("fullscreen-ignore-changed");
    }

    private async Task HandleDisplayGridValueChangedAsync(int rowIndex, int columnIndex)
    {
        if (_suppressDisplayHdrToggleEvents || rowIndex < 0 || rowIndex >= _displayRows.Count)
        {
            return;
        }

        var autoColumn = _displayGrid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(static c => c.DataPropertyName == nameof(DisplayHdrRow.AutoMode));
        if (autoColumn is not null && columnIndex == autoColumn.Index)
        {
            await HandleDisplayAutoModeChangedAsync(rowIndex);
            return;
        }

        var hdrColumn = _displayGrid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(static c => c.DataPropertyName == nameof(DisplayHdrRow.HdrEnabled));
        if (hdrColumn is null || columnIndex != hdrColumn.Index)
        {
            return;
        }

        await HandleDisplayHdrToggleChangedAsync(rowIndex);
    }

    private async Task HandleMatchGridValueChangedAsync(int rowIndex, int columnIndex)
    {
        if (_suppressMatchTargetEvents || rowIndex < 0 || rowIndex >= _matchRows.Count)
        {
            return;
        }

        if (columnIndex != _matchTargetDisplayColumn.Index)
        {
            return;
        }

        var matchRow = _matchRows[rowIndex];
        if (string.IsNullOrWhiteSpace(matchRow.ProcessTargetKey))
        {
            return;
        }

        var normalizedTarget = ProcessWatchRuleRow.NormalizeTargetDisplayValue(matchRow.TargetDisplay);
        var changed = false;
        if (ProcessWatchRuleRow.IsDefaultTargetDisplayValue(normalizedTarget))
        {
            changed = _processTargetDisplayOverrides.Remove(matchRow.ProcessTargetKey);
        }
        else if (!_processTargetDisplayOverrides.TryGetValue(matchRow.ProcessTargetKey, out var current) ||
                 !string.Equals(current, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            _processTargetDisplayOverrides[matchRow.ProcessTargetKey] = normalizedTarget;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        MarkDirty();
        SaveConfigurationToDisk(showSuccessMessage: false);
        RefreshDisplayTargetDropdownOptions();

        if (_monitoringActive)
        {
            await RefreshSnapshotAsync("match-target-changed");
            return;
        }

        await RefreshDisplaySnapshotAsync();
    }

    private async Task HandleDisplayAutoModeChangedAsync(int rowIndex)
    {
        var row = _displayRows[rowIndex];
        if (SetDisplayAutoMode(row.Display, row.AutoMode))
        {
            MarkDirty();
        }

        if (_monitoringActive)
        {
            await RefreshSnapshotAsync("display-auto-mode-changed");
            return;
        }

        await RefreshDisplaySnapshotAsync();
    }

    private async Task HandleDisplayHdrToggleChangedAsync(int rowIndex)
    {
        var row = _displayRows[rowIndex];
        var targetEnabled = row.HdrEnabled;
        AppLogger.Info(
            $"Manual HDR toggle requested from UI. rowIndex={rowIndex}; display={row.Display}; desired={targetEnabled}; autoModeBefore={row.AutoMode}");
        if (SetDisplayAutoMode(row.Display, isAuto: false))
        {
            MarkDirty();
            AppLogger.Info($"Manual HDR toggle forced display auto mode off. display={row.Display}");
        }

        var success = await Task.Run(() => _monitorService.TrySetDisplayHdr(row.Display, targetEnabled, out var message)
            ? (Ok: true, Message: message)
            : (Ok: false, Message: message));
        var actionPrefix = success.Ok ? "Manual HDR switch succeeded" : "Manual HDR switch failed";
        if (success.Ok)
        {
            AppLogger.Info($"Manual HDR toggle result. display={row.Display}; desired={targetEnabled}; result={success.Message}");
        }
        else
        {
            AppLogger.Warn($"Manual HDR toggle result. display={row.Display}; desired={targetEnabled}; result={success.Message}");
        }
        SetMonitorStatus($"{actionPrefix}: {row.Display} | {success.Message}");

        if (_monitoringActive)
        {
            await RefreshSnapshotAsync("manual-display-hdr-toggle");
            return;
        }

        await RefreshDisplaySnapshotAsync();
    }

    private bool GetDisplayAutoMode(string displayName)
    {
        return !_displayAutoModes.TryGetValue(displayName, out var autoMode) || autoMode;
    }

    private bool SetDisplayAutoMode(string displayName, bool isAuto)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        if (isAuto)
        {
            return _displayAutoModes.Remove(displayName);
        }

        if (_displayAutoModes.TryGetValue(displayName, out var current) && !current)
        {
            return false;
        }

        _displayAutoModes[displayName] = false;
        return true;
    }

    private Dictionary<string, bool> BuildDisplayAutoModesConfigMap()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _displayAutoModes)
        {
            result[entry.Key] = entry.Value;
        }

        return result;
    }

    private Dictionary<string, string> BuildProcessTargetDisplayOverridesConfigMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _processTargetDisplayOverrides)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) ||
                string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            var normalized = ProcessWatchRuleRow.NormalizeTargetDisplayValue(entry.Value);
            if (ProcessWatchRuleRow.IsDefaultTargetDisplayValue(normalized))
            {
                continue;
            }

            result[entry.Key.Trim()] = normalized;
        }

        return result;
    }

    private void RefreshDisplayTargetDropdownOptionsIfDisplayTopologyChanged()
    {
        var topologyFingerprint = BuildDisplayTopologyFingerprint();
        if (string.Equals(_displayTopologyFingerprint, topologyFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _displayTopologyFingerprint = topologyFingerprint;
        RefreshDisplayTargetDropdownOptions();
    }

    private string BuildDisplayTopologyFingerprint()
    {
        var displays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in _displayRows.Select(static row => row.Display))
        {
            if (!string.IsNullOrWhiteSpace(display))
            {
                displays.Add(display);
            }
        }

        foreach (var screen in Screen.AllScreens)
        {
            if (!string.IsNullOrWhiteSpace(screen.DeviceName))
            {
                displays.Add(screen.DeviceName);
            }
        }

        return string.Join(
            "|",
            displays.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
    }

    private void RefreshDisplayTargetDropdownOptions()
    {
        if (_ruleTargetDisplayColumn is null || _matchTargetDisplayColumn is null)
        {
            return;
        }

        var availableDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in _displayRows.Select(static d => d.Display))
        {
            if (!string.IsNullOrWhiteSpace(display))
            {
                availableDisplays.Add(display);
            }
        }

        foreach (var screen in Screen.AllScreens)
        {
            if (!string.IsNullOrWhiteSpace(screen.DeviceName))
            {
                availableDisplays.Add(screen.DeviceName);
            }
        }

        var configuredValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in _ruleRows.Select(static r => r.TargetDisplay))
        {
            var normalized = ProcessWatchRuleRow.NormalizeTargetDisplayValue(value);
            if (!ProcessWatchRuleRow.IsDefaultTargetDisplayValue(normalized) &&
                !string.Equals(
                    normalized,
                    ProcessWatchRuleRow.SwitchAllDisplaysTargetValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                configuredValues.Add(normalized);
            }
        }

        foreach (var value in _matchRows.Select(static r => r.TargetDisplay))
        {
            var normalized = ProcessWatchRuleRow.NormalizeTargetDisplayValue(value);
            if (!ProcessWatchRuleRow.IsDefaultTargetDisplayValue(normalized) &&
                !string.Equals(
                    normalized,
                    ProcessWatchRuleRow.SwitchAllDisplaysTargetValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                configuredValues.Add(normalized);
            }
        }

        var options = new List<DisplayTargetOption>
        {
            new(
                ProcessWatchRuleRow.DefaultTargetDisplayValue,
                "Default"),
            new(
                ProcessWatchRuleRow.SwitchAllDisplaysTargetValue,
                "Switch All Displays")
        };

        foreach (var display in availableDisplays.OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(new DisplayTargetOption(display, display));
        }

        foreach (var display in configuredValues
                     .Where(value => !availableDisplays.Contains(value))
                     .OrderBy(static d => d, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(new DisplayTargetOption(display, $"{display} (Unavailable; using Default)"));
        }

        var fingerprint = string.Join(
            "|",
            options.Select(static option => $"{option.Value}=>{option.Label}"));
        if (!_displayTargetOptionsRefreshPending &&
            string.Equals(_displayTargetOptionsFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        if ((_ruleGrid is not null && _ruleGrid.IsCurrentCellInEditMode) ||
            (_matchGrid is not null && _matchGrid.IsCurrentCellInEditMode))
        {
            _displayTargetOptionsRefreshPending = true;
            return;
        }

        _displayTargetOptionsRefreshPending = false;
        _displayTargetOptionsFingerprint = fingerprint;
        ApplyDisplayTargetOptions(_ruleTargetDisplayColumn, options);
        ApplyDisplayTargetOptions(_matchTargetDisplayColumn, options);
    }

    private static void ApplyDisplayTargetOptions(
        DataGridViewComboBoxColumn column,
        List<DisplayTargetOption> options)
    {
        column.DisplayMember = nameof(DisplayTargetOption.Label);
        column.ValueMember = nameof(DisplayTargetOption.Value);
        column.DataSource = options.ToList();
    }

    private void TryApplyPendingDisplayTargetDropdownRefresh()
    {
        if (!_displayTargetOptionsRefreshPending)
        {
            return;
        }

        if ((_ruleGrid is not null && _ruleGrid.IsCurrentCellInEditMode) ||
            (_matchGrid is not null && _matchGrid.IsCurrentCellInEditMode))
        {
            return;
        }

        RefreshDisplayTargetDropdownOptions();
    }

    private void TryOpenComboBoxOnFirstClick(DataGridView grid, int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
        {
            return;
        }

        if (grid.Columns[columnIndex] is not DataGridViewComboBoxColumn comboColumn ||
            comboColumn.ReadOnly)
        {
            return;
        }

        if (grid.CurrentCell is null ||
            grid.CurrentCell.RowIndex != rowIndex ||
            grid.CurrentCell.ColumnIndex != columnIndex)
        {
            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }

        if (!grid.IsCurrentCellInEditMode)
        {
            _ = grid.BeginEdit(selectAll: true);
        }

        BeginInvoke(new Action(() =>
        {
            if (grid.EditingControl is DataGridViewComboBoxEditingControl comboEditingControl)
            {
                comboEditingControl.DroppedDown = true;
            }
        }));
    }

    private bool ResolveIgnoreFromMap(FullscreenProcessInfo info)
    {
        if (_fullscreenIgnoreMap.TryGetValue(info.IgnoreKey, out var ignored))
        {
            return ignored;
        }

        return info.IsIgnored;
    }

    private void MinimizeToTray()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        _isHiddenToTray = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        _isHiddenToTray = false;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private int GetTrayIndicatorMatchCount(ProcessMonitorSnapshot snapshot)
    {
        var ruleMatchCount = snapshot.Matches.Count;
        if (!_monitorAllFullscreenCheck.Checked)
        {
            return ruleMatchCount;
        }

        var fullscreenAutoMatchCount = snapshot.FullscreenProcesses.Count(static process =>
            !process.IsIgnored &&
            !process.MatchedByRule);
        return ruleMatchCount + fullscreenAutoMatchCount;
    }

    private void UpdateTrayMatchIndicator(int matchCount)
    {
        var normalizedCount = Math.Max(0, matchCount);
        if (normalizedCount == _lastTrayMatchCount)
        {
            return;
        }

        var trayText = normalizedCount == 0
            ? "AutoHdrSwitcher"
            : $"AutoHdrSwitcher ({normalizedCount} match(es))";
        _trayIcon.Text = trayText.Length <= 63
            ? trayText
            : trayText[..63];

        if (normalizedCount == 0)
        {
            _trayIcon.Icon = _trayBaseIcon;
            _trayBadgeIcon?.Dispose();
            _trayBadgeIcon = null;
            _lastTrayMatchCount = normalizedCount;
            return;
        }

        var hadMatches = _lastTrayMatchCount > 0;
        if (hadMatches && _trayBadgeIcon is not null)
        {
            _lastTrayMatchCount = normalizedCount;
            return;
        }

        using var badgeBitmap = BuildTrayMatchDotBitmap();
        var badgeIcon = CreateIconFromBitmap(badgeBitmap);
        if (badgeIcon is null)
        {
            return;
        }

        _trayIcon.Icon = badgeIcon;
        _trayBadgeIcon?.Dispose();
        _trayBadgeIcon = badgeIcon;
        _lastTrayMatchCount = normalizedCount;
    }

    private Bitmap BuildTrayMatchDotBitmap()
    {
        var iconSize = SystemInformation.SmallIconSize;
        var bitmap = new Bitmap(iconSize.Width, iconSize.Height);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawIcon(_trayBaseIcon, new Rectangle(Point.Empty, iconSize));

        var badgeDiameter = Math.Max(6, Math.Min(iconSize.Width, iconSize.Height) / 2 - 1);
        var badgeX = Math.Max(0, iconSize.Width - badgeDiameter - 1);
        var badgeY = Math.Max(0, iconSize.Height - badgeDiameter - 1);
        var badgeRect = new Rectangle(
            badgeX,
            badgeY,
            badgeDiameter,
            badgeDiameter);

        using var badgeBrush = new SolidBrush(Color.FromArgb(240, 46, 204, 113));
        using var badgeOutline = new Pen(Color.White, 1F);
        graphics.FillEllipse(badgeBrush, badgeRect);
        graphics.DrawEllipse(badgeOutline, badgeRect);

        return bitmap;
    }

    private static Icon? CreateIconFromBitmap(Bitmap bitmap)
    {
        nint iconHandle = IntPtr.Zero;
        try
        {
            iconHandle = bitmap.GetHicon();
            using var icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (iconHandle != IntPtr.Zero)
            {
                _ = DestroyIcon(iconHandle);
            }
        }
    }

    private void ClearAllGridSelections()
    {
        _ruleGrid.ClearSelection();
        ClearPassiveGridSelection(_matchGrid);
        ClearPassiveGridSelection(_fullscreenGrid);
        ClearPassiveGridSelection(_displayGrid);
    }

    private static void ClearPassiveGridSelection(DataGridView grid)
    {
        if (grid.IsCurrentCellInEditMode)
        {
            return;
        }

        grid.ClearSelection();
        if (grid.CurrentCell is not null)
        {
            grid.CurrentCell = null;
        }
    }

    private void PopulateDisplayPlaceholders()
    {
        _suppressDisplayHdrToggleEvents = true;
        try
        {
            _displayRows.Clear();
            foreach (var screen in Screen.AllScreens.OrderBy(static s => s.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                _displayRows.Add(new DisplayHdrRow
                {
                    Display = screen.DeviceName,
                    FriendlyName = screen.Primary ? $"{screen.DeviceName} (Primary)" : screen.DeviceName,
                    Primary = screen.Primary,
                    Supported = false,
                    AutoMode = GetDisplayAutoMode(screen.DeviceName),
                    HdrEnabled = false,
                    DesiredHdr = false,
                    Action = "Waiting for first scan"
                });
            }
        }
        finally
        {
            _suppressDisplayHdrToggleEvents = false;
        }

        RefreshDisplayTargetDropdownOptionsIfDisplayTopologyChanged();
    }

    private static void ConfigureGridStyle(DataGridView grid)
    {
        grid.RowHeadersVisible = false;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.GridColor = Color.FromArgb(220, 220, 220);
        grid.BackgroundColor = Color.White;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(33, 37, 41);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 239, 255);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Height = 28;
    }

    private static string BuildHdrSummary(IReadOnlyList<HdrDisplayStatus> displays)
    {
        if (displays.Count == 0)
        {
            return "HDR: no active displays";
        }

        var supported = displays.Count(static d => d.IsHdrSupported);
        var onCount = displays.Count(static d => d.IsHdrEnabled);
        var desiredOn = displays.Count(static d => d.DesiredHdrEnabled);
        var failures = displays.Count(static d => d.LastAction.StartsWith("Set HDR failed", StringComparison.OrdinalIgnoreCase));
        return $"HDR: on {onCount}/{supported}, desired {desiredOn}, failures {failures}";
    }

    private bool EnsureProcessEventsStarted()
    {
        if (_processEventMonitor.Start(out var error))
        {
            AppLogger.Info("Event stream started successfully.");
            return true;
        }

        if (_processEventMonitor.IsTraceRetrySuppressed &&
            _processEventMonitor.CurrentMode == ProcessEventStreamMode.Instance)
        {
            SetSaveStatus($"Trace event access denied; running with fallback stream. {error}");
            AppLogger.Warn($"Trace access denied; fallback stream only. {error}");
            return true;
        }

        AppLogger.Warn($"Event stream unavailable: {error}");
        SetSaveStatus($"Event stream unavailable: {error}");
        return false;
    }

    private void ProcessEventMonitorOnStreamModeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(HandleProcessEventModeChangedOnUiThread));
            return;
        }

        HandleProcessEventModeChangedOnUiThread();
    }

    private void HandleProcessEventModeChangedOnUiThread()
    {
        _eventStreamAvailable = _processEventMonitor.CurrentMode != ProcessEventStreamMode.Unavailable;
        if (_processEventMonitor.CurrentMode == ProcessEventStreamMode.Instance && _monitoringActive)
        {
            _nextTraceRecoveryAttemptAt = DateTimeOffset.UtcNow.AddSeconds(TraceRecoveryRetrySeconds);
        }

        if (_processEventMonitor.CurrentMode != ProcessEventStreamMode.Instance)
        {
            _nextTraceRecoveryAttemptAt = DateTimeOffset.MinValue;
        }

        ApplyPollingMode();
        SetMonitorStatus(GetMonitoringModeLabel());
        AppLogger.Info($"UI observed event stream mode: {_processEventMonitor.CurrentMode}");
    }

    private void ProcessEventMonitorOnProcessEventReceived(object? sender, ProcessEventNotification e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => HandleProcessEventOnUiThread(e)));
            return;
        }

        HandleProcessEventOnUiThread(e);
    }

    private void HandleProcessEventOnUiThread(ProcessEventNotification e)
    {
        var uiReceivedAtUtc = DateTimeOffset.UtcNow;
        var eventToUiMs = Math.Max(0D, (uiReceivedAtUtc - e.ReceivedAtUtc).TotalMilliseconds);
        if (!_monitoringActive)
        {
            AppLogger.Info(
                $"Ignoring process event because monitoring is inactive. seq={e.SequenceId}; type={e.EventType}; pid={e.ProcessId}; name={e.ProcessName}; eventToUiMs={eventToUiMs:F3}");
            return;
        }

        var rules = BuildRulesFromUi(commitEdits: false);
        CleanupStalePendingStartEvents();
        if (ShouldIgnoreProcessEventByDefault(e, rules))
        {
            AppLogger.Info(
                $"Ignoring default-ignored process event. seq={e.SequenceId}; type={e.EventType}; pid={e.ProcessId}; name={e.ProcessName}");
            return;
        }

        TrackStartStopEventForLatency(e);

        var ruleNameMatch = IsRuleNameMatchForProcessEvent(e, rules);
        var isTraceStartEvent =
            _processEventMonitor.CurrentMode == ProcessEventStreamMode.Trace &&
            string.Equals(e.EventType, "start", StringComparison.OrdinalIgnoreCase);
        var proactiveTraceStartRefresh = !ruleNameMatch && isTraceStartEvent && rules.Count > 0;
        var monitorAllFullscreen = _monitorAllFullscreenCheck.Checked;
        var fullscreenFallbackRefresh = monitorAllFullscreen && !ruleNameMatch && !proactiveTraceStartRefresh;
        var shouldReact = ruleNameMatch || proactiveTraceStartRefresh || fullscreenFallbackRefresh;
        AppLogger.Info(
            $"UI process event decision. seq={e.SequenceId}; type={e.EventType}; mode={e.StreamMode}; pid={e.ProcessId}; name={e.ProcessName}; eventClass={e.EventClassName}; deliveryMs={DescribeLatency(e.DeliveryLatencyMs)}; eventToUiMs={eventToUiMs:F3}; ruleNameMatch={ruleNameMatch}; monitorAllFullscreen={monitorAllFullscreen}; fullscreenFallbackRefresh={fullscreenFallbackRefresh}; shouldReact={shouldReact}; proactiveTraceStartRefresh={proactiveTraceStartRefresh}; rules={rules.Count}; refreshInFlight={_refreshInFlight}; pending={_snapshotRefreshPending}; burstRemaining={_eventBurstRemaining}");
        if (!shouldReact)
        {
            return;
        }

        if (fullscreenFallbackRefresh)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            if (nowUtc < _nextFullscreenAllEventRefreshAt)
            {
                var remainingMs = (_nextFullscreenAllEventRefreshAt - nowUtc).TotalMilliseconds;
                AppLogger.Info(
                    $"Skipping fullscreen fallback event refresh due to throttle. seq={e.SequenceId}; type={e.EventType}; pid={e.ProcessId}; name={e.ProcessName}; remainingMs={remainingMs:F0}");
                return;
            }

            _nextFullscreenAllEventRefreshAt = nowUtc.AddMilliseconds(FullscreenAllEventRefreshThrottleMs);
        }

        if (_refreshInFlight && _snapshotRefreshPending)
        {
            AppLogger.Info(
                $"Skipping additional event-triggered refresh scheduling because one refresh is in-flight and one is already queued. seq={e.SequenceId}; type={e.EventType}; pid={e.ProcessId}");
            return;
        }

        if (_refreshInFlight)
        {
            AppLogger.Info(
                $"Queueing a single follow-up snapshot refresh for event while refresh is in-flight. seq={e.SequenceId}; type={e.EventType}; pid={e.ProcessId}");
            _ = RefreshSnapshotAsync($"process-event:{e.EventType}", e);
            return;
        }

        if (fullscreenFallbackRefresh)
        {
            AppLogger.Info(
                $"Scheduling single fullscreen fallback refresh without burst. seq={e.SequenceId}; throttleMs={FullscreenAllEventRefreshThrottleMs}");
            _ = RefreshSnapshotAsync($"process-event:{e.EventType}", e);
            return;
        }

        var previousBurst = _eventBurstRemaining;
        var burstRefreshCount = ruleNameMatch
            ? DefaultEventBurstRefreshCount
            : TraceStartProactiveRefreshCount;
        _eventBurstRemaining = Math.Max(_eventBurstRemaining, burstRefreshCount);
        AppLogger.Info(
            $"Scheduling event burst refresh. seq={e.SequenceId}; previousBurst={previousBurst}; requestedBurst={burstRefreshCount}; nextBurst={_eventBurstRemaining}");
        _eventBurstTimer.Stop();
        _eventBurstTimer.Start();
        _ = RefreshSnapshotAsync($"process-event:{e.EventType}", e);
    }

    private static bool IsRuleNameMatchForProcessEvent(
        ProcessEventNotification e,
        IReadOnlyCollection<ProcessWatchRule> rules)
    {
        if (rules.Count == 0)
        {
            return false;
        }

        var name = e.ProcessName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (ProcessWatchMatcher.IsMatchAny(name, rules))
        {
            return true;
        }

        var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name
            : name + ".exe";
        return ProcessWatchMatcher.IsMatchAny(exeName, rules);
    }

    private static bool ShouldIgnoreProcessEventByDefault(
        ProcessEventNotification e,
        IReadOnlyCollection<ProcessWatchRule> rules)
    {
        var normalized = NormalizeProcessNameForEventFilter(e.ProcessName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!ProcessMonitorService.IsDefaultIgnoredProcessName(normalized))
        {
            return false;
        }

        // If user explicitly writes a rule for this process name, do not suppress event handling.
        if (rules.Count > 0 &&
            (ProcessWatchMatcher.IsMatchAny(e.ProcessName, rules) ||
             ProcessWatchMatcher.IsMatchAny(normalized + ".exe", rules)))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeProcessNameForEventFilter(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim().ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalized = normalized[..^4];
        }

        while (normalized.EndsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private void TrackStartStopEventForLatency(ProcessEventNotification e)
    {
        if (e.ProcessId <= 0)
        {
            return;
        }

        if (string.Equals(e.EventType, "start", StringComparison.OrdinalIgnoreCase))
        {
            _pendingStartEvents[e.ProcessId] = new PendingStartEventInfo(
                e.SequenceId,
                e.StreamMode,
                e.ProcessId,
                e.ProcessName,
                e.ReceivedAtUtc,
                e.EventCreatedAtUtc);
            AppLogger.Info(
                $"Tracked start event for latency. seq={e.SequenceId}; pid={e.ProcessId}; name={e.ProcessName}; mode={e.StreamMode}; receivedUtc={e.ReceivedAtUtc:O}; createdUtc={DescribeUtc(e.EventCreatedAtUtc)}");
            return;
        }

        if (string.Equals(e.EventType, "stop", StringComparison.OrdinalIgnoreCase) &&
            _pendingStartEvents.Remove(e.ProcessId, out var pending))
        {
            AppLogger.Info(
                $"Removed pending start event due to stop before match. startSeq={pending.SequenceId}; stopSeq={e.SequenceId}; pid={e.ProcessId}; name={pending.ProcessName}");
        }
    }

    private void LogMatchedStartEventLatencies(ProcessMonitorSnapshot snapshot, DateTimeOffset refreshStartedAtUtc)
    {
        if (_pendingStartEvents.Count == 0 || snapshot.Matches.Count == 0)
        {
            return;
        }

        var matchedPids = new HashSet<int>(snapshot.Matches.Select(static match => match.ProcessId));
        foreach (var pid in matchedPids)
        {
            if (!_pendingStartEvents.Remove(pid, out var pending))
            {
                continue;
            }

            var fromReceiveMs = Math.Max(0D, (refreshStartedAtUtc - pending.EventReceivedAtUtc).TotalMilliseconds);
            var fromCreatedMs = pending.EventCreatedAtUtc.HasValue
                ? Math.Max(0D, (refreshStartedAtUtc - pending.EventCreatedAtUtc.Value).TotalMilliseconds)
                : (double?)null;
            AppLogger.Info(
                $"Match detected for tracked start event. startSeq={pending.SequenceId}; pid={pending.ProcessId}; name={pending.ProcessName}; eventMode={pending.StreamMode}; refreshStartedUtc={refreshStartedAtUtc:O}; eventReceivedUtc={pending.EventReceivedAtUtc:O}; eventCreatedUtc={DescribeUtc(pending.EventCreatedAtUtc)}; eventToRefreshMs={fromReceiveMs:F3}; createdToRefreshMs={DescribeLatency(fromCreatedMs)}");
        }
    }

    private void CleanupStalePendingStartEvents()
    {
        if (_pendingStartEvents.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var stalePids = _pendingStartEvents
            .Where(pair => (now - pair.Value.EventReceivedAtUtc).TotalSeconds >= PendingStartEventRetentionSeconds)
            .Select(static pair => pair.Key)
            .ToArray();
        if (stalePids.Length == 0)
        {
            return;
        }

        foreach (var stalePid in stalePids)
        {
            _pendingStartEvents.Remove(stalePid);
        }

        AppLogger.Info($"Cleared stale tracked start events. count={stalePids.Length}");
    }

    private static string DescribeTrigger(ProcessEventNotification? triggerEvent)
    {
        if (triggerEvent is null)
        {
            return "none";
        }

        return $"seq={triggerEvent.SequenceId};type={triggerEvent.EventType};pid={triggerEvent.ProcessId};name={triggerEvent.ProcessName};mode={triggerEvent.StreamMode};deliveryMs={DescribeLatency(triggerEvent.DeliveryLatencyMs)}";
    }

    private static string DescribeTriggerSequence(ProcessEventNotification? triggerEvent)
    {
        return triggerEvent is null ? "n/a" : triggerEvent.SequenceId.ToString();
    }

    private static string DescribeLatency(double? valueMs)
    {
        return valueMs.HasValue ? valueMs.Value.ToString("F3") : "n/a";
    }

    private static string DescribeUtc(DateTimeOffset? timestamp)
    {
        return timestamp.HasValue ? timestamp.Value.ToString("O") : "n/a";
    }

    private sealed record PendingStartEventInfo(
        long SequenceId,
        ProcessEventStreamMode StreamMode,
        int ProcessId,
        string ProcessName,
        DateTimeOffset EventReceivedAtUtc,
        DateTimeOffset? EventCreatedAtUtc);

    private sealed record DisplayTargetOption(string Value, string Label);
}
