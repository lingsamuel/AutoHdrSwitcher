using System.ComponentModel;
using AutoHdrSwitcher.Config;
using AutoHdrSwitcher.Matching;
using AutoHdrSwitcher.Monitoring;

namespace AutoHdrSwitcher.UI;

public sealed class MainForm : Form
{
    private const int MinPollSeconds = 1;
    private const int MaxPollSeconds = 30;

    private readonly string _configPath;
    private readonly ProcessMonitorService _monitorService = new();
    private readonly ProcessEventMonitor _processEventMonitor = new();
    private readonly BindingList<ProcessWatchRuleRow> _ruleRows = new();
    private readonly BindingList<ProcessMatchRow> _matchRows = new();
    private readonly BindingList<FullscreenProcessRow> _fullscreenRows = new();
    private readonly BindingList<DisplayHdrRow> _displayRows = new();
    private readonly System.Windows.Forms.Timer _monitorTimer = new();
    private readonly System.Windows.Forms.Timer _eventBurstTimer = new();

    private DataGridView _ruleGrid = null!;
    private DataGridView _matchGrid = null!;
    private DataGridView _fullscreenGrid = null!;
    private DataGridView _displayGrid = null!;
    private NumericUpDown _pollSecondsInput = null!;
    private CheckBox _monitorAllFullscreenCheck = null!;
    private ToolStripStatusLabel _monitorStateLabel = null!;
    private ToolStripStatusLabel _snapshotLabel = null!;
    private ToolStripStatusLabel _saveStateLabel = null!;
    private ToolStripStatusLabel _configPathLabel = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;

    private bool _suppressDirtyTracking;
    private bool _hasUnsavedChanges;
    private bool _refreshInFlight;
    private int _eventBurstRemaining;
    private bool _exitRequested;

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
        Text = "AutoHdrSwitcher";
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

    private FlowLayoutPanel BuildTopPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8)
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

        var pollLabel = new Label
        {
            Text = "Poll (sec):",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(18, 9, 4, 0)
        };

        _pollSecondsInput = new NumericUpDown
        {
            Minimum = MinPollSeconds,
            Maximum = MaxPollSeconds,
            Value = 2,
            Width = 64,
            Margin = new Padding(0, 6, 0, 0)
        };

        _monitorAllFullscreenCheck = new CheckBox
        {
            Text = "Auto monitor all fullscreen processes",
            AutoSize = true,
            Margin = new Padding(18, 9, 0, 0)
        };
        _monitorAllFullscreenCheck.CheckedChanged += (_, _) =>
        {
            MarkDirty();
            _ = RefreshSnapshotAsync();
        };

        panel.Controls.Add(addRuleButton);
        panel.Controls.Add(removeRuleButton);
        panel.Controls.Add(saveButton);
        panel.Controls.Add(reloadButton);
        panel.Controls.Add(_startButton);
        panel.Controls.Add(_stopButton);
        panel.Controls.Add(refreshButton);
        panel.Controls.Add(pollLabel);
        panel.Controls.Add(_pollSecondsInput);
        panel.Controls.Add(_monitorAllFullscreenCheck);
        return panel;
    }

    private SplitContainer BuildSplitContainer()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 360
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

        var runtimeSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 170
        };

        var runtimeBottomSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 140
        };

        _matchGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _matchRows,
            ReadOnly = true,
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
        runtimeSplit.Panel1.Controls.Add(_matchGrid);

        _fullscreenGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _fullscreenRows,
            ReadOnly = true,
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
            Width = 85
        });
        _fullscreenGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Process",
            DataPropertyName = nameof(FullscreenProcessRow.ProcessName),
            ToolTipText = string.Empty,
            Width = 220
        });
        _fullscreenGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Display",
            DataPropertyName = nameof(FullscreenProcessRow.Display),
            ToolTipText = string.Empty,
            Width = 145
        });
        _fullscreenGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Matched Rule",
            DataPropertyName = nameof(FullscreenProcessRow.MatchedByRule),
            ToolTipText = string.Empty,
            Width = 95
        });
        runtimeBottomSplit.Panel1.Controls.Add(_fullscreenGrid);

        _displayGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _displayRows,
            ReadOnly = true,
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
            Width = 145
        });
        _displayGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Monitor",
            DataPropertyName = nameof(DisplayHdrRow.FriendlyName),
            ToolTipText = string.Empty,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Supported",
            DataPropertyName = nameof(DisplayHdrRow.Supported),
            ToolTipText = string.Empty,
            Width = 85
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "HDR On",
            DataPropertyName = nameof(DisplayHdrRow.HdrEnabled),
            ToolTipText = string.Empty,
            Width = 75
        });
        _displayGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Desired",
            DataPropertyName = nameof(DisplayHdrRow.DesiredHdr),
            ToolTipText = string.Empty,
            Width = 75
        });
        _displayGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Action",
            DataPropertyName = nameof(DisplayHdrRow.Action),
            ToolTipText = string.Empty,
            Width = 180
        });
        runtimeBottomSplit.Panel2.Controls.Add(_displayGrid);
        runtimeBottomSplit.Panel1MinSize = 120;
        runtimeBottomSplit.Panel2MinSize = 160;
        runtimeSplit.Panel2.Controls.Add(runtimeBottomSplit);
        runtimeSplit.Panel2MinSize = 300;
        runtimeGroup.Controls.Add(runtimeSplit);

        split.Panel1.Controls.Add(ruleGroup);
        split.Panel2.Controls.Add(runtimeGroup);
        return split;
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

        _pollSecondsInput.ValueChanged += (_, _) =>
        {
            _monitorTimer.Interval = (int)_pollSecondsInput.Value * 1000;
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
        _pollSecondsInput.Leave += (_, _) => SaveIfDirtyOnFocusLost();
        Deactivate += (_, _) => SaveIfDirtyOnFocusLost();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && !_exitRequested)
            {
                MinimizeToTray();
            }
        };
        Shown += (_, _) => ClearAllGridSelections();

        _matchGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_matchGrid);
        _displayGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_displayGrid);
        _fullscreenGrid.SelectionChanged += (_, _) => ClearPassiveGridSelection(_fullscreenGrid);

        FormClosing += (sender, e) =>
        {
            if (!_exitRequested && WindowState == FormWindowState.Minimized)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            _eventBurstTimer.Stop();
            _processEventMonitor.Stop();
            _processEventMonitor.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
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
        var eventStreamAvailable = EnsureProcessEventsStarted();
        _monitorTimer.Start();
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        SetMonitorStatus(eventStreamAvailable ? "Monitor: running" : "Monitor: running (polling fallback)");
        _ = RefreshSnapshotAsync();
    }

    private void StopMonitoring()
    {
        _monitorTimer.Stop();
        _eventBurstTimer.Stop();
        _processEventMonitor.Stop();
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        SetMonitorStatus("Monitor: stopped");
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
            var snapshot = await Task.Run(() => _monitorService.Evaluate(rules, _monitorAllFullscreenCheck.Checked));
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
        foreach (var fullscreen in snapshot.FullscreenProcesses)
        {
            _fullscreenRows.Add(new FullscreenProcessRow
            {
                ProcessId = fullscreen.ProcessId,
                ProcessName = fullscreen.ProcessName,
                Display = fullscreen.Display,
                MatchedByRule = fullscreen.MatchedByRule
            });
        }

        _displayRows.Clear();
        foreach (var display in snapshot.Displays)
        {
            _displayRows.Add(new DisplayHdrRow
            {
                Display = display.Display,
                FriendlyName = display.FriendlyName,
                Supported = display.IsHdrSupported,
                HdrEnabled = display.IsHdrEnabled,
                DesiredHdr = display.DesiredHdrEnabled,
                Action = display.LastAction
            });
        }

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
        if (activeRuleCount == 0)
        {
            SetMonitorStatus($"Monitor: running (no rules configured) | {hdrSummary}");
            return;
        }

        if (snapshot.Matches.Count == 0)
        {
            SetMonitorStatus($"Monitor: running (no matched processes) | {hdrSummary}");
            return;
        }

        SetMonitorStatus($"Monitor: running ({snapshot.Matches.Count} match(es)) | {hdrSummary}");
    }

    private void LoadConfigurationFromDisk()
    {
        var loaded = TryLoadConfigurationResilient();
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
            _monitorAllFullscreenCheck.Checked = loaded.MonitorAllFullscreenProcesses;
            _monitorTimer.Interval = pollSeconds * 1000;
            _hasUnsavedChanges = false;
            SetSaveStatus("Config: loaded");
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
                MonitorAllFullscreenProcesses = _monitorAllFullscreenCheck.Checked,
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
        _displayRows.Clear();
        foreach (var screen in Screen.AllScreens.OrderBy(static s => s.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            _displayRows.Add(new DisplayHdrRow
            {
                Display = screen.DeviceName,
                FriendlyName = screen.Primary ? $"{screen.DeviceName} (Primary)" : screen.DeviceName,
                Supported = false,
                HdrEnabled = false,
                DesiredHdr = false,
                Action = "Waiting for first scan"
            });
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
        if (!_monitorTimer.Enabled || !ShouldReactToProcessEvent(e))
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
