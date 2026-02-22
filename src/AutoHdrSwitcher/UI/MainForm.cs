using System.ComponentModel;
using AutoHdrSwitcher.Config;
using AutoHdrSwitcher.Matching;
using AutoHdrSwitcher.Monitoring;

namespace AutoHdrSwitcher.UI;

public sealed class MainForm : Form
{
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
    private Label _pollLabel = null!;
    private NumericUpDown _pollSecondsInput = null!;
    private CheckBox _pollingEnabledCheck = null!;
    private CheckBox _minimizeToTrayCheck = null!;
    private CheckBox _monitorAllFullscreenCheck = null!;
    private CheckBox _switchAllDisplaysCheck = null!;
    private ToolStripStatusLabel _monitorStateLabel = null!;
    private ToolStripStatusLabel _snapshotLabel = null!;
    private ToolStripStatusLabel _saveStateLabel = null!;
    private ToolStripStatusLabel _configPathLabel = null!;
    private ToolStripStatusLabel _eventSourceLabel = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;

    private bool _suppressDirtyTracking;
    private bool _suppressFullscreenIgnoreEvents;
    private bool _suppressDisplayHdrToggleEvents;
    private bool _hasUnsavedChanges;
    private bool _refreshInFlight;
    private int _eventBurstRemaining;
    private bool _monitoringActive;
    private bool _eventStreamAvailable;
    private bool _exitRequested;
    private int? _loadedMainSplitterDistance;
    private int? _loadedRuntimeTopSplitterDistance;
    private int? _loadedRuntimeBottomSplitterDistance;
    private DateTimeOffset _nextTraceRecoveryAttemptAt = DateTimeOffset.MinValue;

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
            MarkDirty();
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
            _ = RefreshSnapshotAsync();
        };

        _startButton = new Button { Text = "Start Monitor", AutoSize = true };
        _startButton.Click += (_, _) => StartMonitoring();

        _stopButton = new Button { Text = "Stop Monitor", AutoSize = true };
        _stopButton.Click += (_, _) => StopMonitoring();

        var refreshButton = new Button { Text = "Refresh Now", AutoSize = true };
        refreshButton.Click += (_, _) => _ = RefreshSnapshotAsync();

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
            Text = "Close to tray (exit from tray menu)",
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 0)
        };
        _minimizeToTrayCheck.CheckedChanged += (_, _) => MarkDirty();

        _monitorAllFullscreenCheck = new CheckBox
        {
            Text = "Auto monitor all fullscreen processes",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _monitorAllFullscreenCheck.CheckedChanged += (_, _) =>
        {
            MarkDirty();
            _ = RefreshSnapshotAsync();
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
            _ = RefreshSnapshotAsync();
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
        monitorOptionsRow.Controls.Add(_monitorAllFullscreenCheck);
        monitorOptionsRow.Controls.Add(_switchAllDisplaysCheck);

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
        ruleGroup.Controls.Add(_ruleGrid);

        var runtimeGroup = new GroupBox
        {
            Text = "Runtime Status",
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
            ReadOnly = true,
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
            Width = 85
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Process",
            DataPropertyName = nameof(ProcessMatchRow.ProcessName),
            ToolTipText = string.Empty,
            Width = 190
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Display",
            DataPropertyName = nameof(ProcessMatchRow.Display),
            ToolTipText = string.Empty,
            Width = 145
        });
        _matchGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Fullscreen",
            DataPropertyName = nameof(ProcessMatchRow.FullscreenLike),
            ToolTipText = string.Empty,
            Width = 85
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Rule Pattern",
            DataPropertyName = nameof(ProcessMatchRow.RulePattern),
            ToolTipText = string.Empty,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Mode",
            DataPropertyName = nameof(ProcessMatchRow.Mode),
            ToolTipText = string.Empty,
            Width = 230
        });
        _matchGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Matched Input",
            DataPropertyName = nameof(ProcessMatchRow.MatchInput),
            ToolTipText = string.Empty,
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
        var fullscreenGroup = new GroupBox
        {
            Text = "Detected Fullscreen Processes",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        fullscreenGroup.Controls.Add(_fullscreenGrid);
        _runtimeBottomSplit.Panel1.Controls.Add(fullscreenGroup);

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
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
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
            Width = 180
        });
        var displayGroup = new GroupBox
        {
            Text = "Display HDR Status",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        displayGroup.Controls.Add(_displayGrid);
        _runtimeBottomSplit.Panel2.Controls.Add(displayGroup);
        _runtimeSplit.Panel2.Controls.Add(_runtimeBottomSplit);
        runtimeGroup.Controls.Add(_runtimeSplit);

        _mainSplit.Panel1.Controls.Add(ruleGroup);
        _mainSplit.Panel2.Controls.Add(runtimeGroup);
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
        _eventSourceLabel = new ToolStripStatusLabel("Event source: n/a");
        _configPathLabel = new ToolStripStatusLabel(_configPath);

        statusStrip.Items.Add(_monitorStateLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_snapshotLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_saveStateLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        statusStrip.Items.Add(_eventSourceLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel(" | Config: "));
        statusStrip.Items.Add(_configPathLabel);
        return statusStrip;
    }

    private void InitializeTrayIcon()
    {
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
            Icon = SystemIcons.Application,
            Text = "AutoHdrSwitcher",
            Visible = false,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void WireUpEvents()
    {
        _monitorTimer.Interval = 2000;
        _monitorTimer.Tick += async (_, _) => await RefreshSnapshotAsync();
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
                return;
            }

            _eventBurstRemaining--;
            _ = RefreshSnapshotAsync();
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
            if (_ruleGrid.IsCurrentCellDirty)
            {
                _ruleGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _ruleGrid.CellValueChanged += (_, _) => MarkDirty();
        _ruleGrid.CellEndEdit += (_, _) => SaveIfDirtyOnFocusLost();
        _ruleGrid.Leave += (_, _) => SaveIfDirtyOnFocusLost();
        _ruleGrid.RowsRemoved += (_, _) => MarkDirty();
        _ruleGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            SetSaveStatus($"Input error: {eventArgs.Exception?.Message ?? "invalid value"}");
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
        Deactivate += (_, _) => SaveIfDirtyOnFocusLost();
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

        _matchGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_matchGrid);
        _displayGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_displayGrid);
        _fullscreenGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_fullscreenGrid);

        FormClosing += (sender, e) =>
        {
            if (!_exitRequested &&
                _minimizeToTrayCheck.Checked &&
                e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            _eventBurstTimer.Stop();
            _displayRefreshTimer.Stop();
            _processEventMonitor.Stop();
            _processEventMonitor.Dispose();
            _monitorService.FlushPredictionCache();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
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

        MarkDirty();
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
        _eventStreamAvailable = EnsureProcessEventsStarted();
        _monitoringActive = true;
        ApplyPollingMode();
        UpdateEventSourceLabel();
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        SetMonitorStatus(GetMonitoringModeLabel());
        _ = RefreshSnapshotAsync();
    }

    private void StopMonitoring()
    {
        _monitoringActive = false;
        _monitorTimer.Stop();
        _eventBurstTimer.Stop();
        _processEventMonitor.Stop();
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        UpdateEventSourceLabel();
        SetMonitorStatus("Monitor: stopped");
        _ = RefreshDisplaySnapshotAsync();
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_refreshInFlight)
        {
            return;
        }

        _refreshInFlight = true;
        try
        {
            var rules = BuildRulesFromUi(commitEdits: false);
            var snapshot = await Task.Run(
                () => _monitorService.Evaluate(
                    rules,
                    _monitorAllFullscreenCheck.Checked,
                    _fullscreenIgnoreMap,
                    _switchAllDisplaysCheck.Checked,
                    _displayAutoModes));
            ApplySnapshot(snapshot, rules.Count);
        }
        catch (Exception ex)
        {
            SetMonitorStatus($"Monitor error: {ex.Message}");
        }
        finally
        {
            _refreshInFlight = false;
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
            _snapshotLabel.Text = $"Last display refresh: {DateTimeOffset.Now:HH:mm:ss}";
            SetMonitorStatus($"{GetMonitoringModeLabel()} | {BuildHdrSummary(displays)}");
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
                _ = RefreshSnapshotAsync();
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
        _matchRows.Clear();
        foreach (var match in snapshot.Matches)
        {
            _matchRows.Add(new ProcessMatchRow
            {
                ProcessId = match.ProcessId,
                ProcessName = match.ProcessName,
                Display = match.Display,
                FullscreenLike = match.IsFullscreenLike,
                RulePattern = match.RulePattern,
                Mode = match.Mode,
                MatchInput = match.MatchInput
            });
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

        _snapshotLabel.Text =
            $"Last scan: {snapshot.CollectedAt:HH:mm:ss} | Processes: {snapshot.ProcessCount} | Matches: {snapshot.Matches.Count} | Fullscreen: {snapshot.FullscreenProcesses.Count}";
        var hdrSummary = BuildHdrSummary(snapshot.Displays);
        var modeLabel = GetMonitoringModeLabel();
        if (activeRuleCount == 0)
        {
            SetMonitorStatus($"{modeLabel} (no rules configured) | {hdrSummary}");
            return;
        }

        if (snapshot.Matches.Count == 0)
        {
            SetMonitorStatus($"{modeLabel} (no matched processes) | {hdrSummary}");
            return;
        }

        SetMonitorStatus($"{modeLabel} ({snapshot.Matches.Count} match(es)) | {hdrSummary}");
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

            var pollSeconds = loaded.PollIntervalSeconds;
            if (pollSeconds < MinPollSeconds || pollSeconds > MaxPollSeconds)
            {
                pollSeconds = 2;
            }

            _pollSecondsInput.Value = pollSeconds;
            _pollingEnabledCheck.Checked = loaded.PollingEnabled;
            _minimizeToTrayCheck.Checked = loaded.MinimizeToTray;
            _monitorAllFullscreenCheck.Checked = loaded.MonitorAllFullscreenProcesses;
            _switchAllDisplaysCheck.Checked = loaded.SwitchAllDisplaysTogether;
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
                MonitorAllFullscreenProcesses = _monitorAllFullscreenCheck.Checked,
                SwitchAllDisplaysTogether = _switchAllDisplaysCheck.Checked,
                MainSplitterDistance = GetCurrentSplitterDistance(_mainSplit),
                RuntimeTopSplitterDistance = GetCurrentSplitterDistance(_runtimeSplit),
                RuntimeBottomSplitterDistance = GetCurrentSplitterDistance(_runtimeBottomSplit),
                FullscreenIgnoreMap = new Dictionary<string, bool>(_fullscreenIgnoreMap, StringComparer.OrdinalIgnoreCase),
                DisplayAutoModes = BuildDisplayAutoModesConfigMap(),
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

    private void UpdateEventSourceLabel()
    {
        _eventSourceLabel.Text = _processEventMonitor.CurrentMode switch
        {
            ProcessEventStreamMode.Trace => "Event source: trace",
            ProcessEventStreamMode.Instance => "Event source: instance (fallback)",
            _ => _monitoringActive ? "Event source: unavailable" : "Event source: stopped"
        };
    }

    private void MaybeRecoverTraceEventStream()
    {
        if (_processEventMonitor.CurrentMode != ProcessEventStreamMode.Instance)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextTraceRecoveryAttemptAt)
        {
            return;
        }

        _nextTraceRecoveryAttemptAt = now.AddSeconds(TraceRecoveryRetrySeconds);
        _processEventMonitor.TrySwitchToTrace(out _);
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

        return _monitorTimer.Enabled
            ? "Monitor: running (events + polling)"
            : "Monitor: running (events only)";
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
        _ = RefreshSnapshotAsync();
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

    private async Task HandleDisplayAutoModeChangedAsync(int rowIndex)
    {
        var row = _displayRows[rowIndex];
        if (SetDisplayAutoMode(row.Display, row.AutoMode))
        {
            MarkDirty();
        }

        if (_monitoringActive)
        {
            await RefreshSnapshotAsync();
            return;
        }

        await RefreshDisplaySnapshotAsync();
    }

    private async Task HandleDisplayHdrToggleChangedAsync(int rowIndex)
    {
        var row = _displayRows[rowIndex];
        var targetEnabled = row.HdrEnabled;
        if (SetDisplayAutoMode(row.Display, isAuto: false))
        {
            MarkDirty();
        }

        var success = await Task.Run(() => _monitorService.TrySetDisplayHdr(row.Display, targetEnabled, out var message)
            ? (Ok: true, Message: message)
            : (Ok: false, Message: message));
        var actionPrefix = success.Ok ? "Manual HDR switch succeeded" : "Manual HDR switch failed";
        SetMonitorStatus($"{actionPrefix}: {row.Display} | {success.Message}");

        if (_monitoringActive)
        {
            await RefreshSnapshotAsync();
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
        if (_trayIcon.Visible)
        {
            return;
        }

        _trayIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
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
            return true;
        }

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
        UpdateEventSourceLabel();
        SetMonitorStatus(GetMonitoringModeLabel());
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
        if (!_monitoringActive || !ShouldReactToProcessEvent(e))
        {
            return;
        }

        _eventBurstRemaining = 6;
        _eventBurstTimer.Stop();
        _eventBurstTimer.Start();
        _ = RefreshSnapshotAsync();
    }

    private bool ShouldReactToProcessEvent(ProcessEventNotification e)
    {
        if (_monitorAllFullscreenCheck.Checked)
        {
            return true;
        }

        var rules = BuildRulesFromUi(commitEdits: false);
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
}
