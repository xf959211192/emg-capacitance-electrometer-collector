using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EmgCollector.Acquisition;
using EmgCollector.Controls;
using EmgCollector.Connectivity;
using EmgCollector.Protocol;
using EmgCollector.Recording;
using EmgCollector.Security;

namespace EmgCollector;

internal enum SignalMode
{
    Emg,
    Capacitance,
    Electrostatic
}

public sealed class MainForm : Form
{
    private const int SeparatedMinimumLaneHeight = 54;
    private const int SeparatedMaximumLaneHeight = 180;
    private readonly ComboBox _modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ComboBox _addressBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly NumericUpDown _portBox = new() { Minimum = 1, Maximum = 65535, Value = 8060, Width = 72, Enabled = false };
    private readonly TextBox _sampleRateBox = new() { Width = 74, Text = "1000", PlaceholderText = "请输入" };
    private readonly ComboBox _electrostaticRangeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 74, Visible = false };
    private readonly Label _electrostaticRangeLabel = new() { Text = "CH1档位", AutoSize = true, Margin = new Padding(0, 8, 5, 0), Visible = false };
    private readonly NumericUpDown _windowBox = new() { Minimum = 1, Maximum = 20, Value = 5, Width = 58 };
    private readonly CheckBox _demoBox = new() { Text = "演示信号", AutoSize = true };
    private readonly Button _startButton = new() { Text = "开始采集", AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
    private readonly Button _recordButton = new() { Text = "开始录制", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Enabled = false };
    private readonly Button _clearButton = new() { Text = "清空波形", AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
    private readonly Button _firewallButton = new() { Text = "配置防火墙", AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
    private readonly Button _electrostaticWiringButton = new() { Text = "通道与档位图", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Visible = false };
    private readonly Button _connectWifiButton = new() { Text = "连接板卡 Wi-Fi", AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
    private readonly Button _pauseWifiButton = new() { Text = "暂停连接", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Enabled = false };
    private readonly Button _restoreWifiButton = new() { Text = "恢复原 Wi-Fi", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Enabled = false };
    private readonly Label _boardWifiLabel = new() { AutoSize = true, Margin = new Padding(0, 8, 10, 0) };
    private readonly Label _wifiStatusLabel = new() { Text = "正在检查 Wi-Fi…", AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(8, 8, 0, 0) };
    private readonly Label _stateLabel = new() { Text = "未开始", AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105) };
    private readonly Label _deviceStatusLabel = new() { Text = "设备：待采集", AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(10, 8, 0, 0) };
    private readonly Label _modeNoticeLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(180, 83, 9), MaximumSize = new Size(1200, 0) };
    private readonly Label _statisticsLabel = new() { AutoSize = true, Text = "数据包 0  ·  丢包 0  ·  无效包 0  ·  采样点 0" };
    private readonly Label _errorLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(185, 28, 28), MaximumSize = new Size(1200, 0) };
    private readonly FlowLayoutPanel _channelPanel = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
    private readonly Panel _separatedHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
    private readonly TabControl _waveformTabs = new() { Dock = DockStyle.Fill };
    private readonly WaveformDataBuffer _waveformData = new();
    private readonly WaveformControl _separatedWaveform;
    private readonly WaveformControl _overlayWaveform;
    private readonly List<CheckBox> _channelSwitches = [];
    private readonly List<Button> _channelActionButtons = [];
    private readonly bool[] _emgChannelSelection = Enumerable.Repeat(true, 8).ToArray();
    private readonly bool[] _capacitanceChannelSelection = Enumerable.Repeat(true, CapacitancePacket.ExpectedChannelCount).ToArray();
    private readonly bool[] _electrostaticChannelSelection = Enumerable.Repeat(true, ElectrostaticMeasurement.ExpectedChannelCount).ToArray();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _wifiMonitorTimer = new() { Interval = 2000 };
    private readonly UdpEmgReceiver _emgReceiver = new();
    private readonly UdpCapacitanceReceiver _capacitanceReceiver = new();
    private readonly DemoEmgSource _emgDemoSource = new();
    private readonly DemoCapacitanceSource _capacitanceDemoSource = new();
    private readonly CsvRecorder _emgRecorder = new();
    private readonly CapacitanceCsvRecorder _capacitanceRecorder = new();
    private readonly ElectrostaticCsvRecorder _electrostaticRecorder = new();
    private bool _running;
    private bool _allowClose;
    private bool _applyingMode;
    private bool _wifiMonitorBusy;
    private bool _wifiOperationInProgress;
    private bool _isClosing;
    private CancellationTokenSource? _wifiConnectionCancellation;
    private int _activeSampleRate = 1000;
    private SignalMode _activeMode = SignalMode.Emg;
    private ElectrostaticCurrentRange _activeElectrostaticRange = ElectrostaticCurrentRange.Nanoampere;
    private string? _recordingPath;
    private string? _originalWifiProfile;
    private string _emgSampleRateText = "1000";
    private string _capacitanceSampleRateText = "100";
    private string _electrostaticSampleRateText = "1000";
    private int _electrostaticPort = 8060;
    private long _acquisitionStartedTick;
    private long _lastPacketTick;

    public MainForm()
    {
        _separatedWaveform = new WaveformControl(_waveformData, WaveformDisplayMode.Separated)
        {
            MinimumSize = new Size(640, 0),
            Location = Point.Empty
        };
        _overlayWaveform = new WaveformControl(_waveformData, WaveformDisplayMode.Overlay)
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(640, 400)
        };

        Text = "EMG / 电容 / 静电计多通道采集软件";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 760);
        Size = new Size(1420, 900);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        BuildLayout();
        PopulateAddresses();
        _modeBox.Items.AddRange(["肌电 EMG（8通道）", "电容（35测量＋5参考）", "静电计（8通道）"]);
        _electrostaticRangeBox.Items.AddRange(["nA", "μA", "mA"]);
        _electrostaticRangeBox.SelectedIndex = 0;
        _modeBox.SelectedIndex = 0;

        _startButton.Click += async (_, _) => await ToggleAcquisitionAsync();
        _recordButton.Click += (_, _) => ToggleRecording();
        _clearButton.Click += (_, _) => ClearWaveforms();
        _firewallButton.Click += async (_, _) => await ConfigureFirewallAsync();
        _electrostaticWiringButton.Click += (_, _) => ShowElectrostaticWiringDiagram();
        _connectWifiButton.Click += async (_, _) => await ConnectPreferredBoardWifiAsync(showFailureMessage: true);
        _pauseWifiButton.Click += (_, _) => PauseWifiConnection();
        _restoreWifiButton.Click += async (_, _) => await RestoreOriginalWifiAsync();
        _modeBox.SelectedIndexChanged += async (_, _) => await ChangeModeAsync();
        _sampleRateBox.TextChanged += (_, _) => OnSampleRateTextChanged();
        _electrostaticRangeBox.SelectedIndexChanged += (_, _) => OnElectrostaticRangeChanged();
        _portBox.ValueChanged += (_, _) => OnPortChanged();
        _windowBox.ValueChanged += (_, _) => ApplyDisplaySettings();
        _waveformTabs.SelectedIndexChanged += (_, _) => InvalidateActiveWaveform();
        _separatedHost.Resize += (_, _) => LayoutSeparatedWaveform();
        _emgReceiver.PacketReceived += OnEmgPacketReceived;
        _emgReceiver.ReceiveError += ShowReceiveError;
        _capacitanceReceiver.PacketReceived += OnCapacitancePacketReceived;
        _capacitanceReceiver.ReceiveError += ShowReceiveError;
        _emgDemoSource.PacketReceived += OnEmgPacketReceived;
        _capacitanceDemoSource.PacketReceived += OnCapacitancePacketReceived;
        _uiTimer.Tick += (_, _) => RefreshStatistics();
        _wifiMonitorTimer.Tick += async (_, _) =>
        {
            await DetectBoardWifiAndApplyModeAsync();
        };
        FormClosing += OnFormClosing;
        Shown += async (_, _) =>
        {
            ApplyMode();
            await RefreshFirewallButtonAsync();
            await DetectBoardWifiAndApplyModeAsync();
            _wifiMonitorTimer.Start();
        };
        ApplyMode();
    }

    private SignalMode CurrentMode => _modeBox.SelectedIndex switch
    {
        1 => SignalMode.Capacitance,
        2 => SignalMode.Electrostatic,
        _ => SignalMode.Emg
    };
    private bool IsCapacitanceMode => CurrentMode == SignalMode.Capacitance;
    private bool IsElectrostaticMode => CurrentMode == SignalMode.Electrostatic;
    private int CurrentChannelCount => CurrentMode switch
    {
        SignalMode.Capacitance => CapacitancePacket.ExpectedChannelCount,
        SignalMode.Electrostatic => ElectrostaticMeasurement.ExpectedChannelCount,
        _ => 8
    };
    private int CurrentPort => CurrentMode switch
    {
        SignalMode.Capacitance => 8080,
        SignalMode.Electrostatic => _electrostaticPort,
        _ => 8060
    };
    private string CurrentSsid => CurrentMode switch
    {
        SignalMode.Capacitance => WifiConnectionService.CapacitanceSsid,
        SignalMode.Electrostatic => WifiConnectionService.ElectrostaticSsid,
        _ => WifiConnectionService.EmgSsid
    };
    private bool IsRecording => CurrentMode switch
    {
        SignalMode.Capacitance => _capacitanceRecorder.IsRecording,
        SignalMode.Electrostatic => _electrostaticRecorder.IsRecording,
        _ => _emgRecorder.IsRecording
    };
    private ElectrostaticCurrentRange CurrentElectrostaticRange => _electrostaticRangeBox.SelectedIndex switch
    {
        1 => ElectrostaticCurrentRange.Microampere,
        2 => ElectrostaticCurrentRange.Milliampere,
        _ => ElectrostaticCurrentRange.Nanoampere
    };

    private void BuildLayout()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(14),
            BackColor = Color.White
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var wifiPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 7) };
        wifiPanel.Controls.Add(_boardWifiLabel);
        wifiPanel.Controls.Add(_connectWifiButton);
        wifiPanel.Controls.Add(_pauseWifiButton);
        wifiPanel.Controls.Add(_restoreWifiButton);
        wifiPanel.Controls.Add(_wifiStatusLabel);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        AddLabeledControl(toolbar, "采集类型", _modeBox);
        AddLabeledControl(toolbar, "本机地址", _addressBox);
        AddLabeledControl(toolbar, "UDP端口", _portBox);
        AddLabeledControl(toolbar, "采样率 Hz", _sampleRateBox);
        toolbar.Controls.Add(_electrostaticRangeLabel);
        _electrostaticRangeBox.Margin = new Padding(0, 3, 12, 3);
        toolbar.Controls.Add(_electrostaticRangeBox);
        AddLabeledControl(toolbar, "显示窗口 秒", _windowBox);
        toolbar.Controls.Add(_demoBox);
        toolbar.Controls.Add(_startButton);
        toolbar.Controls.Add(_recordButton);
        toolbar.Controls.Add(_clearButton);
        toolbar.Controls.Add(_firewallButton);
        toolbar.Controls.Add(_electrostaticWiringButton);
        toolbar.Controls.Add(_stateLabel);
        toolbar.Controls.Add(_deviceStatusLabel);

        var separatedPage = new TabPage("分通道波形") { BackColor = Color.White, Padding = new Padding(3) };
        _separatedHost.Controls.Add(_separatedWaveform);
        separatedPage.Controls.Add(_separatedHost);
        var overlayPage = new TabPage("全部通道叠加图") { BackColor = Color.White, Padding = new Padding(3) };
        overlayPage.Controls.Add(_overlayWaveform);
        _waveformTabs.TabPages.Add(separatedPage);
        _waveformTabs.TabPages.Add(overlayPage);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        footer.Controls.Add(_statisticsLabel);
        footer.Controls.Add(_errorLabel);

        main.Controls.Add(wifiPanel, 0, 0);
        main.Controls.Add(toolbar, 0, 1);
        main.Controls.Add(_modeNoticeLabel, 0, 2);
        main.Controls.Add(_channelPanel, 0, 3);
        main.Controls.Add(_waveformTabs, 0, 4);
        main.Controls.Add(footer, 0, 5);
        Controls.Add(main);
    }

    private static void AddLabeledControl(FlowLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 5, 0) });
        control.Margin = new Padding(0, 3, 12, 3);
        panel.Controls.Add(control);
    }

    private async Task ChangeModeAsync()
    {
        if (_applyingMode || _running)
        {
            return;
        }
        ApplyMode();
        await RefreshFirewallButtonAsync();
    }

    private void ApplyMode()
    {
        _applyingMode = true;
        try
        {
            _boardWifiLabel.Text = $"板卡 Wi-Fi：{CurrentSsid}";
            _portBox.Value = CurrentPort;
            _portBox.Enabled = IsElectrostaticMode;
            _electrostaticRangeLabel.Visible = IsElectrostaticMode;
            _electrostaticRangeBox.Visible = IsElectrostaticMode;
            _electrostaticWiringButton.Visible = IsElectrostaticMode;
            _sampleRateBox.Text = CurrentMode switch
            {
                SignalMode.Capacitance => _capacitanceSampleRateText,
                SignalMode.Electrostatic => _electrostaticSampleRateText,
                _ => _emgSampleRateText
            };
            _modeNoticeLabel.Text = CurrentMode switch
            {
                SignalMode.Capacitance =>
                    "电容测量范围 0～约 1.5 nF，每通道采样率 100 Hz；依据厂家 pF 曲线和三位小数数据，按原始值 ÷ 1000 显示和导出 pF。",
                SignalMode.Electrostatic =>
                    "CH1 档位必须与板上拨码一致，纵轴和 CSV 单位随档位显示为 nA、μA 或 mA。默认 1 kHz，可选 8060/8080 端口。",
                _ => string.Empty
            };
            _errorLabel.Text = string.Empty;
            _stateLabel.Text = "未开始";
            _deviceStatusLabel.Text = "设备：待采集";
            _deviceStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);

            string[] labels = CurrentMode switch
            {
                SignalMode.Capacitance => Enumerable.Range(0, CapacitancePacket.ExpectedChannelCount).Select(CapacitancePacket.GetChannelName).ToArray(),
                SignalMode.Electrostatic => Enumerable.Range(0, ElectrostaticMeasurement.ExpectedChannelCount).Select(ElectrostaticMeasurement.GetChannelName).ToArray(),
                _ => Enumerable.Range(1, 8).Select(channel => $"CH{channel}").ToArray()
            };
            bool[] references = IsCapacitanceMode
                ? Enumerable.Range(0, CapacitancePacket.ExpectedChannelCount).Select(CapacitancePacket.IsReferenceChannel).ToArray()
                : new bool[labels.Length];
            string[] units = CurrentMode switch
            {
                SignalMode.Capacitance => Enumerable.Repeat("pF", labels.Length).ToArray(),
                SignalMode.Electrostatic => Enumerable.Range(0, labels.Length)
                    .Select(channel => ElectrostaticMeasurement.GetUnit(channel, CurrentElectrostaticRange))
                    .ToArray(),
                _ => Enumerable.Repeat("V", labels.Length).ToArray()
            };
            string overlayUnit = IsElectrostaticMode ? "混合单位" : units[0];
            _waveformData.Reset(labels.Length);
            _separatedWaveform.ConfigureChannels(labels, units, overlayUnit, !IsCapacitanceMode, references);
            _overlayWaveform.ConfigureChannels(labels, units, overlayUnit, !IsCapacitanceMode, references);
            RebuildChannelSwitches(labels, references);
            ApplyDisplaySettings();
            LayoutSeparatedWaveform();
            RefreshStatisticsText(PacketStatisticsEmpty());
        }
        finally
        {
            _applyingMode = false;
        }
    }

    private void ShowElectrostaticWiringDiagram()
    {
        using var dialog = new ElectrostaticWiringDialog();
        dialog.ShowDialog(this);
    }

    private void OnElectrostaticRangeChanged()
    {
        if (_applyingMode || _running || !IsElectrostaticMode)
        {
            return;
        }

        ApplyMode();
    }

    private void RebuildChannelSwitches(IReadOnlyList<string> labels, IReadOnlyList<bool> references)
    {
        _channelPanel.SuspendLayout();
        _channelPanel.Controls.Clear();
        _channelSwitches.Clear();
        _channelActionButtons.Clear();
        _channelPanel.Controls.Add(new Label
        {
            Text = IsCapacitanceMode ? "通道开关（黄色为参考通道，控制显示和导出）：" : "通道开关（显示和导出）：",
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 0)
        });

        AddChannelActionButton("全选", () => SetAllChannelSwitches(true));
        AddChannelActionButton("反选", InvertChannelSwitches);
        AddChannelActionButton("全不选", () => SetAllChannelSwitches(false));

        bool[] savedSelection = GetCurrentChannelSelection();
        for (int channel = 0; channel < labels.Count; channel++)
        {
            int capturedChannel = channel;
            var checkBox = new CheckBox
            {
                Text = labels[channel],
                Checked = savedSelection[channel],
                AutoSize = true,
                BackColor = references[channel] ? Color.FromArgb(254, 249, 195) : Color.Transparent,
                Margin = new Padding(3, 3, 6, 3)
            };
            checkBox.CheckedChanged += (_, _) => SetChannelVisible(capturedChannel, checkBox.Checked);
            _channelSwitches.Add(checkBox);
            _channelPanel.Controls.Add(checkBox);
            SetChannelVisible(channel, checkBox.Checked);
        }
        _channelPanel.ResumeLayout();
        LayoutSeparatedWaveform();
    }

    private void AddChannelActionButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(4, 0, 4, 0),
            Margin = new Padding(2, 0, 4, 2)
        };
        button.Click += (_, _) => action();
        _channelActionButtons.Add(button);
        _channelPanel.Controls.Add(button);
    }

    private void SetAllChannelSwitches(bool isChecked)
    {
        foreach (CheckBox checkBox in _channelSwitches)
        {
            checkBox.Checked = isChecked;
        }
        LayoutSeparatedWaveform();
    }

    private void InvertChannelSwitches()
    {
        foreach (CheckBox checkBox in _channelSwitches)
        {
            checkBox.Checked = !checkBox.Checked;
        }
        LayoutSeparatedWaveform();
    }

    private void SetChannelVisible(int channel, bool visible)
    {
        bool[] selection = GetCurrentChannelSelection();
        if (channel >= 0 && channel < selection.Length)
        {
            selection[channel] = visible;
        }
        _separatedWaveform.SetChannelVisible(channel, visible);
        _overlayWaveform.SetChannelVisible(channel, visible);
        LayoutSeparatedWaveform();
    }

    private void OnSampleRateTextChanged()
    {
        if (_applyingMode)
        {
            return;
        }
        if (IsCapacitanceMode)
        {
            _capacitanceSampleRateText = _sampleRateBox.Text.Trim();
        }
        else if (IsElectrostaticMode)
        {
            _electrostaticSampleRateText = _sampleRateBox.Text.Trim();
        }
        else
        {
            _emgSampleRateText = _sampleRateBox.Text.Trim();
        }
        ApplyDisplaySettings();
    }

    private bool[] GetCurrentChannelSelection() => CurrentMode switch
    {
        SignalMode.Capacitance => _capacitanceChannelSelection,
        SignalMode.Electrostatic => _electrostaticChannelSelection,
        _ => _emgChannelSelection
    };

    private void OnPortChanged()
    {
        if (!_applyingMode && IsElectrostaticMode)
        {
            _electrostaticPort = (int)_portBox.Value;
        }
    }

    private bool TryGetSampleRate(out int sampleRate, bool showError)
    {
        if (int.TryParse(_sampleRateBox.Text.Trim(), out sampleRate) && sampleRate is >= 1 and <= 100_000)
        {
            return true;
        }
        if (showError)
        {
            _errorLabel.Text = "请输入有效采样率（1～100000 Hz）。";
        }
        sampleRate = 1;
        return false;
    }

    private void ApplyDisplaySettings()
    {
        TryGetSampleRate(out int sampleRate, showError: false);
        _waveformData.Configure(sampleRate, (int)_windowBox.Value);
        _separatedWaveform.Invalidate();
        _overlayWaveform.Invalidate();
    }

    private void LayoutSeparatedWaveform()
    {
        int width = Math.Max(640, _separatedHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3);
        int availableHeight = Math.Max(1, _separatedHost.ClientSize.Height - 3);
        int visibleChannelCount = _channelSwitches.Count(checkBox => checkBox.Checked);
        int requiredHeight;
        if (visibleChannelCount == 0)
        {
            requiredHeight = Math.Min(availableHeight, SeparatedMaximumLaneHeight);
        }
        else
        {
            int adaptiveLaneHeight = availableHeight / visibleChannelCount;
            int laneHeight = Math.Clamp(adaptiveLaneHeight, SeparatedMinimumLaneHeight, SeparatedMaximumLaneHeight);
            requiredHeight = laneHeight * visibleChannelCount;
        }
        _separatedWaveform.Size = new Size(width, requiredHeight);
    }

    private void PopulateAddresses()
    {
        _addressBox.Items.Clear();
        _addressBox.Items.Add("0.0.0.0");
        string[] addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(info => info.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(info => info.Address.ToString())
            .Distinct()
            .OrderBy(address => address)
            .ToArray();
        foreach (string address in addresses)
        {
            _addressBox.Items.Add(address);
        }
        int preferred = _addressBox.Items.IndexOf("192.168.4.2");
        _addressBox.SelectedIndex = preferred >= 0 ? preferred : 0;
    }

    private async Task ToggleAcquisitionAsync()
    {
        if (_running)
        {
            await StopAcquisitionAsync();
            return;
        }

        try
        {
            _errorLabel.Text = string.Empty;
            if (!_demoBox.Checked && !await EnsureBoardWifiConnectedAsync())
            {
                return;
            }
            if (!TryGetSampleRate(out _activeSampleRate, showError: true))
            {
                return;
            }
            _activeMode = CurrentMode;
            _activeElectrostaticRange = CurrentElectrostaticRange;
            ClearWaveforms();
            if (!_demoBox.Checked && !await EnsureFirewallPermissionAsync())
            {
                return;
            }

            if (_demoBox.Checked)
            {
                if (IsCapacitanceMode)
                {
                    _capacitanceDemoSource.Start(_activeSampleRate);
                }
                else
                {
                    _emgDemoSource.Start(_activeSampleRate);
                }
                _stateLabel.Text = "正在采集演示信号";
            }
            else
            {
                var address = IPAddress.Parse((string)_addressBox.SelectedItem!);
                if (IsCapacitanceMode)
                {
                    _capacitanceReceiver.Start(address, CurrentPort);
                }
                else
                {
                    _emgReceiver.Start(address, CurrentPort);
                }
                _stateLabel.Text = $"正在监听 {address}:{CurrentPort}";
            }

            _running = true;
            _acquisitionStartedTick = Environment.TickCount64;
            Interlocked.Exchange(ref _lastPacketTick, 0);
            _deviceStatusLabel.Text = _demoBox.Checked ? "设备：演示模式" : "设备：等待数据";
            _deviceStatusLabel.ForeColor = Color.FromArgb(37, 99, 235);
            _startButton.Text = "停止采集";
            _recordButton.Enabled = true;
            SetAcquisitionSettingsEnabled(false);
            _uiTimer.Start();
        }
        catch (SocketException ex)
        {
            _errorLabel.Text = $"无法启动 UDP 监听：{ex.Message}。请确认本机地址和端口未被占用。";
        }
        catch (Exception ex)
        {
            _errorLabel.Text = $"无法启动采集：{ex.Message}";
        }
    }

    private void SetAcquisitionSettingsEnabled(bool enabled)
    {
        _modeBox.Enabled = enabled;
        _addressBox.Enabled = enabled;
        _portBox.Enabled = enabled && IsElectrostaticMode;
        _sampleRateBox.Enabled = enabled;
        _electrostaticRangeBox.Enabled = enabled;
        _demoBox.Enabled = enabled;
        _connectWifiButton.Enabled = enabled;
        _restoreWifiButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_originalWifiProfile);
    }

    private async Task<bool> EnsureBoardWifiConnectedAsync()
    {
        string? currentProfile = await WifiConnectionService.GetCurrentProfileAsync();
        BoardWifiKind connectedBoard = WifiConnectionService.IdentifyBoardWifi(currentProfile);
        if (connectedBoard != BoardWifiKind.None)
        {
            ApplyModeForBoardWifi(currentProfile!);
            _wifiStatusLabel.Text = $"当前：{currentProfile}（已匹配{GetModeName(connectedBoard)}）";
            PopulateAddresses();
            return true;
        }
        return await ConnectPreferredBoardWifiAsync(showFailureMessage: true);
    }

    private async Task<bool> ConnectPreferredBoardWifiAsync(bool showFailureMessage)
    {
        if (_running)
        {
            _errorLabel.Text = "请先停止采集，再切换 Wi-Fi。";
            return false;
        }
        if (_wifiConnectionCancellation is not null)
        {
            _wifiStatusLabel.Text = "板卡连接正在进行中";
            return false;
        }

        var cancellation = new CancellationTokenSource();
        _wifiConnectionCancellation = cancellation;
        _connectWifiButton.Enabled = false;
        _pauseWifiButton.Enabled = true;
        _startButton.Enabled = false;
        _modeBox.Enabled = false;
        _errorLabel.Text = string.Empty;
        string preferredSsid = CurrentSsid;
        string[] candidates =
        [
            preferredSsid,
            .. new[]
            {
                WifiConnectionService.EmgSsid,
                WifiConnectionService.CapacitanceSsid,
                WifiConnectionService.ElectrostaticSsid
            }.Where(ssid => !string.Equals(ssid, preferredSsid, StringComparison.Ordinal))
        ];
        try
        {
            for (int index = 0; index < candidates.Length; index++)
            {
                string candidate = candidates[index];
                cancellation.Token.ThrowIfCancellationRequested();
                _wifiStatusLabel.Text = index == 0
                    ? $"正在连接 {candidate}…"
                    : $"上一板卡连接失败，正在尝试 {candidate}…";
                if (await ConnectBoardWifiAsync(candidate, cancellation.Token))
                {
                    return true;
                }
            }

            string attempted = string.Join("\n", candidates.Select((ssid, index) => $"{index + 1}. {ssid}"));
            string message = $"无法连接板卡。\n\n已依次尝试：\n{attempted}\n\n请确认板卡已通电后重试。";
            _wifiStatusLabel.Text = "三种板卡均连接失败";
            _errorLabel.Text = message.Replace("\n", " ");
            if (showFailureMessage)
            {
                MessageBox.Show(this, message, "板卡连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            if (!_isClosing)
            {
                _wifiStatusLabel.Text = "连接已暂停";
                _errorLabel.Text = string.Empty;
            }
            return false;
        }
        catch (Exception ex)
        {
            _wifiStatusLabel.Text = "板卡连接失败";
            _errorLabel.Text = ex.Message;
            if (showFailureMessage)
            {
                MessageBox.Show(this, ex.Message, "板卡连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
        finally
        {
            if (ReferenceEquals(_wifiConnectionCancellation, cancellation))
            {
                _wifiConnectionCancellation = null;
            }
            cancellation.Dispose();
            _connectWifiButton.Enabled = !_running;
            _pauseWifiButton.Enabled = false;
            _startButton.Enabled = true;
            _modeBox.Enabled = !_running;
        }
    }

    private void PauseWifiConnection()
    {
        if (_wifiConnectionCancellation is null)
        {
            return;
        }

        _pauseWifiButton.Enabled = false;
        _wifiStatusLabel.Text = "正在暂停连接…";
        _wifiConnectionCancellation.Cancel();
    }

    private void ApplyModeForBoardWifi(string ssid)
    {
        int targetModeIndex = WifiConnectionService.IdentifyBoardWifi(ssid) switch
        {
            BoardWifiKind.Capacitance => 1,
            BoardWifiKind.Electrostatic => 2,
            _ => 0
        };
        if (_modeBox.SelectedIndex != targetModeIndex)
        {
            _modeBox.SelectedIndex = targetModeIndex;
        }
    }

    private async Task<bool> ConnectBoardWifiAsync(string targetSsid, CancellationToken cancellationToken)
    {
        _connectWifiButton.Enabled = false;
        _restoreWifiButton.Enabled = false;
        _wifiOperationInProgress = true;
        _wifiStatusLabel.Text = $"正在连接 {targetSsid}，并等待网络地址稳定…";
        _errorLabel.Text = string.Empty;
        try
        {
            WifiConnectionResult result = await WifiConnectionService.ConnectBoardAsync(targetSsid, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.OriginalProfile) &&
                !string.Equals(result.OriginalProfile, targetSsid, StringComparison.Ordinal))
            {
                _originalWifiProfile = result.OriginalProfile;
            }
            _wifiStatusLabel.Text = result.Message;
            if (!result.Success)
            {
                _errorLabel.Text = result.Message;
                return false;
            }

            ApplyModeForBoardWifi(targetSsid);
            PopulateAddresses();
            _portBox.Value = CurrentPort;
            _wifiStatusLabel.Text = $"{result.Message}（已匹配{GetModeName(CurrentMode)}）";
            _restoreWifiButton.Enabled = !string.IsNullOrWhiteSpace(_originalWifiProfile);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _wifiStatusLabel.Text = "板卡 Wi-Fi 连接失败";
            _errorLabel.Text = $"自动连接 Wi-Fi 失败：{ex.Message}";
            return false;
        }
        finally
        {
            _wifiOperationInProgress = false;
        }
    }

    private async Task RestoreOriginalWifiAsync()
    {
        if (_running)
        {
            _errorLabel.Text = "请先停止采集，再恢复原 Wi-Fi。";
            return;
        }
        if (string.IsNullOrWhiteSpace(_originalWifiProfile))
        {
            _errorLabel.Text = "本次运行没有记录原 Wi-Fi，请通过 Windows 网络菜单手动连接。";
            return;
        }

        string profile = _originalWifiProfile;
        _connectWifiButton.Enabled = false;
        _restoreWifiButton.Enabled = false;
        _wifiOperationInProgress = true;
        _wifiStatusLabel.Text = $"正在恢复 {profile}…";
        try
        {
            bool restored = await WifiConnectionService.RestoreProfileAsync(profile);
            _wifiStatusLabel.Text = restored ? $"已恢复 {profile}" : $"恢复 {profile} 失败";
            if (restored)
            {
                _originalWifiProfile = null;
                PopulateAddresses();
            }
        }
        catch (Exception ex)
        {
            _wifiStatusLabel.Text = $"恢复 {profile} 失败";
            _errorLabel.Text = ex.Message;
        }
        finally
        {
            _wifiOperationInProgress = false;
            _connectWifiButton.Enabled = true;
            _restoreWifiButton.Enabled = !string.IsNullOrWhiteSpace(_originalWifiProfile);
        }
    }

    private async Task<BoardWifiKind> DetectBoardWifiAndApplyModeAsync()
    {
        if (_wifiMonitorBusy || _wifiOperationInProgress || _isClosing)
        {
            return BoardWifiKind.None;
        }

        _wifiMonitorBusy = true;
        try
        {
            string? profile = await WifiConnectionService.GetCurrentProfileAsync();
            if (_isClosing)
            {
                return BoardWifiKind.None;
            }

            BoardWifiKind boardKind = WifiConnectionService.IdentifyBoardWifi(profile);
            if (boardKind == BoardWifiKind.None)
            {
                _wifiStatusLabel.Text = profile is null ? "Wi-Fi 未连接" : $"当前：{profile}";
                return BoardWifiKind.None;
            }

            int targetModeIndex = boardKind switch
            {
                BoardWifiKind.Capacitance => 1,
                BoardWifiKind.Electrostatic => 2,
                _ => 0
            };
            string targetModeName = GetModeName(boardKind);
            bool needsModeSwitch = _modeBox.SelectedIndex != targetModeIndex;
            bool stoppedRunningAcquisition = false;

            if (needsModeSwitch && _running)
            {
                await StopAcquisitionAsync();
                stoppedRunningAcquisition = true;
            }

            if (_modeBox.SelectedIndex != targetModeIndex)
            {
                _modeBox.SelectedIndex = targetModeIndex;
            }

            // 板卡刚连接时地址可能稍后才分配，持续检测直到 192.168.4.2 出现。
            if (!_addressBox.Items.Contains("192.168.4.2"))
            {
                PopulateAddresses();
            }

            _wifiStatusLabel.Text = $"当前：{profile}（已匹配{targetModeName}）";
            if (needsModeSwitch)
            {
                _stateLabel.Text = stoppedRunningAcquisition
                    ? $"检测到 {profile}，已停止原采集并切换到{targetModeName}"
                    : $"检测到 {profile}，已自动切换到{targetModeName}";
            }
            return boardKind;
        }
        catch (Exception ex)
        {
            if (_isClosing)
            {
                return BoardWifiKind.None;
            }
            _wifiStatusLabel.Text = "无法读取 Wi-Fi 状态";
            _errorLabel.Text = ex.Message;
            return BoardWifiKind.None;
        }
        finally
        {
            _wifiMonitorBusy = false;
        }
    }

    private static string GetModeName(BoardWifiKind boardKind) => boardKind switch
    {
        BoardWifiKind.Capacitance => "电容模式",
        BoardWifiKind.Electrostatic => "静电计模式",
        BoardWifiKind.Emg => "EMG 模式",
        _ => "未知模式"
    };

    private static string GetModeName(SignalMode mode) => mode switch
    {
        SignalMode.Capacitance => "电容模式",
        SignalMode.Electrostatic => "静电计模式",
        _ => "EMG 模式"
    };

    private async Task<bool> EnsureFirewallPermissionAsync()
    {
        if (await FirewallPermissionService.IsConfiguredAsync())
        {
            _firewallButton.Text = "防火墙已授权";
            return true;
        }

        DialogResult answer = MessageBox.Show(
            this,
            "板卡会主动向本机发送 UDP 数据，需要允许软件接收入站数据。\n\n" +
            "本次将一次配置三种板卡使用的 UDP 8060 和 8080 两个端口，" +
            "同时适用于专用网络和公用网络；来源仅限板卡地址 192.168.4.1。\n\n" +
            "是否现在请求管理员权限？",
            "需要防火墙权限",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            _errorLabel.Text = "未配置防火墙权限，真实板卡数据可能无法进入软件。";
            return false;
        }
        return await ConfigureFirewallAsync();
    }

    private async Task<bool> ConfigureFirewallAsync()
    {
        _firewallButton.Enabled = false;
        try
        {
            bool configured = await FirewallPermissionService.RequestPermissionAsync(this);
            _firewallButton.Text = configured ? "防火墙已授权" : "配置防火墙";
            if (configured)
            {
                _errorLabel.Text = string.Empty;
            }
            return configured;
        }
        finally
        {
            _firewallButton.Enabled = true;
        }
    }

    private async Task RefreshFirewallButtonAsync()
    {
        bool configured = await FirewallPermissionService.IsConfiguredAsync();
        _firewallButton.Text = configured ? "防火墙已授权" : "配置防火墙";
    }

    private async Task StopAcquisitionAsync()
    {
        _emgRecorder.Stop();
        _capacitanceRecorder.Stop();
        _electrostaticRecorder.Stop();
        _recordingPath = null;
        _recordButton.Text = "开始录制";
        await _emgReceiver.StopAsync();
        await _capacitanceReceiver.StopAsync();
        await _emgDemoSource.StopAsync();
        await _capacitanceDemoSource.StopAsync();
        _running = false;
        _uiTimer.Stop();
        _startButton.Text = "开始采集";
        _recordButton.Enabled = false;
        SetAcquisitionSettingsEnabled(true);
        SetChannelSwitchesEnabled(true);
        _stateLabel.Text = "已停止";
        _deviceStatusLabel.Text = "设备：已停止";
        _deviceStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        RefreshStatistics();
    }

    private void ToggleRecording()
    {
        if (IsRecording)
        {
            if (IsCapacitanceMode)
            {
                _capacitanceRecorder.Stop();
            }
            else if (IsElectrostaticMode)
            {
                _electrostaticRecorder.Stop();
            }
            else
            {
                _emgRecorder.Stop();
            }
            _recordButton.Text = "开始录制";
            _stateLabel.Text = $"录制已保存：{_recordingPath}";
            SetChannelSwitchesEnabled(true);
            return;
        }

        bool[] enabledChannels = _channelSwitches.Select(checkBox => checkBox.Checked).ToArray();
        if (!enabledChannels.Any(enabled => enabled))
        {
            _errorLabel.Text = "请至少打开一个通道后再开始录制。";
            return;
        }

        string filePrefix = CurrentMode switch
        {
            SignalMode.Capacitance => "CAP",
            SignalMode.Electrostatic => "EMETER",
            _ => "EMG"
        };
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 数据文件 (*.csv)|*.csv",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "选择数据保存位置"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (IsCapacitanceMode)
            {
                _capacitanceRecorder.Start(dialog.FileName, enabledChannels);
            }
            else if (IsElectrostaticMode)
            {
                _electrostaticRecorder.Start(dialog.FileName, enabledChannels, CurrentElectrostaticRange);
            }
            else
            {
                _emgRecorder.Start(dialog.FileName, enabledChannels);
            }
            _recordingPath = dialog.FileName;
            _recordButton.Text = "停止录制";
            _stateLabel.Text = $"正在录制 {enabledChannels.Count(enabled => enabled)} 个通道：{dialog.FileName}";
            SetChannelSwitchesEnabled(false);
        }
        catch (Exception ex)
        {
            _errorLabel.Text = $"无法开始录制：{ex.Message}";
        }
    }

    private void SetChannelSwitchesEnabled(bool enabled)
    {
        foreach (CheckBox checkBox in _channelSwitches)
        {
            checkBox.Enabled = enabled;
        }
        foreach (Button button in _channelActionButtons)
        {
            button.Enabled = enabled;
        }
    }

    private void OnEmgPacketReceived(EmgPacket packet)
    {
        if (_activeMode == SignalMode.Electrostatic)
        {
            _waveformData.AppendElectrostatic(packet, _activeElectrostaticRange);
            _electrostaticRecorder.Write(packet, _activeSampleRate);
        }
        else
        {
            _waveformData.Append(packet);
            _emgRecorder.Write(packet, _activeSampleRate);
        }
        Interlocked.Exchange(ref _lastPacketTick, Environment.TickCount64);
    }

    private void OnCapacitancePacketReceived(CapacitancePacket packet)
    {
        _waveformData.Append(packet);
        Interlocked.Exchange(ref _lastPacketTick, Environment.TickCount64);
        _capacitanceRecorder.Write(packet, _activeSampleRate);
    }

    private void ShowReceiveError(string error)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        try
        {
            BeginInvoke(() => _errorLabel.Text = error);
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing)
        {
            // 窗口关闭期间忽略迟到的接收错误。
        }
    }

    private StatisticsSnapshot CurrentStatistics() => IsCapacitanceMode
        ? (_demoBox.Checked ? _capacitanceDemoSource.Statistics.Snapshot() : _capacitanceReceiver.Statistics.Snapshot())
        : (_demoBox.Checked ? _emgDemoSource.Statistics.Snapshot() : _emgReceiver.Statistics.Snapshot());

    private static StatisticsSnapshot PacketStatisticsEmpty() => new(0, 0, 0, 0, 0, null, string.Empty);

    private void RefreshStatistics()
    {
        StatisticsSnapshot stats = CurrentStatistics();
        RefreshStatisticsText(stats);
        if (!string.IsNullOrWhiteSpace(stats.LastError))
        {
            _errorLabel.Text = stats.LastError;
        }
        RefreshDeviceStatus(stats);
        InvalidateActiveWaveform();
    }

    private void RefreshStatisticsText(StatisticsSnapshot stats)
    {
        _statisticsLabel.Text =
            $"数据包 {stats.ReceivedPackets:N0}  ·  丢包 {stats.LostPackets:N0}  ·  " +
            $"乱序 {stats.OutOfOrderPackets:N0}  ·  无效包 {stats.InvalidPackets:N0}  ·  " +
            $"每通道采样点 {stats.ReceivedSamples:N0}  ·  最新包计数 {stats.LastCounter?.ToString() ?? "-"}";
    }

    private void RefreshDeviceStatus(StatisticsSnapshot stats)
    {
        if (!_running)
        {
            return;
        }
        if (_demoBox.Checked)
        {
            _deviceStatusLabel.Text = "设备：演示模式";
            _deviceStatusLabel.ForeColor = Color.FromArgb(37, 99, 235);
            return;
        }

        long now = Environment.TickCount64;
        long lastPacket = Interlocked.Read(ref _lastPacketTick);
        if (lastPacket == 0)
        {
            bool timedOut = now - _acquisitionStartedTick > 3000;
            _deviceStatusLabel.Text = timedOut ? "设备：未收到数据" : "设备：等待数据";
            _deviceStatusLabel.ForeColor = timedOut ? Color.FromArgb(185, 28, 28) : Color.FromArgb(37, 99, 235);
            return;
        }
        if (now - lastPacket > 2500)
        {
            _deviceStatusLabel.Text = "设备：数据已中断";
            _deviceStatusLabel.ForeColor = Color.FromArgb(185, 28, 28);
            return;
        }

        bool hasWarning = stats.InvalidPackets > 0 || stats.LostPackets > 0;
        _deviceStatusLabel.Text = hasWarning ? "设备：正常（有数据异常）" : "设备：正常";
        _deviceStatusLabel.ForeColor = hasWarning ? Color.FromArgb(180, 83, 9) : Color.FromArgb(22, 101, 52);
    }

    private void ClearWaveforms()
    {
        _waveformData.Clear();
        _separatedWaveform.Invalidate();
        _overlayWaveform.Invalidate();
    }

    private void InvalidateActiveWaveform()
    {
        if (_waveformTabs.SelectedIndex == 1)
        {
            _overlayWaveform.Invalidate();
        }
        else
        {
            _separatedWaveform.Invalidate();
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        _wifiConnectionCancellation?.Cancel();
        Enabled = false;
        _uiTimer.Stop();
        _wifiMonitorTimer.Stop();
        _emgRecorder.Dispose();
        _capacitanceRecorder.Dispose();
        _electrostaticRecorder.Dispose();
        await _emgReceiver.DisposeAsync();
        await _capacitanceReceiver.DisposeAsync();
        await _emgDemoSource.DisposeAsync();
        await _capacitanceDemoSource.DisposeAsync();
        _allowClose = true;
        Close();
    }
}
