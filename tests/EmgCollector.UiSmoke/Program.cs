using System.Drawing.Imaging;
using EmgCollector;
using EmgCollector.Connectivity;
using EmgCollector.Controls;

ApplicationConfiguration.Initialize();
if (WifiConnectionService.IdentifyBoardWifi(WifiConnectionService.EmgSsid) != BoardWifiKind.Emg ||
    WifiConnectionService.IdentifyBoardWifi(WifiConnectionService.CapacitanceSsid) != BoardWifiKind.Capacitance ||
    WifiConnectionService.IdentifyBoardWifi(WifiConnectionService.ElectrostaticSsid) != BoardWifiKind.Electrostatic ||
    WifiConnectionService.IdentifyBoardWifi("普通路由器") != BoardWifiKind.None)
{
    throw new InvalidOperationException("板卡 Wi-Fi 自动识别映射错误");
}
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try
    {
        await WifiConnectionService.ConnectBoardAsync(WifiConnectionService.EmgSsid, cancellation.Token);
        throw new InvalidOperationException("暂停连接没有终止 Wi-Fi 连接流程");
    }
    catch (OperationCanceledException)
    {
        // 预先暂停时必须在任何网络切换操作前结束。
    }
}
using var form = new MainForm();
form.Show();
Application.DoEvents();

Button pauseWifi = FindControl<Button>(form, "暂停连接");
if (pauseWifi.Enabled)
{
    throw new InvalidOperationException("未连接时暂停按钮不应启用");
}

CheckBox demo = FindControl<CheckBox>(form, "演示信号");
Button start = FindControl<Button>(form, "开始采集");
demo.Checked = true;
start.PerformClick();

DateTime deadline = DateTime.Now.AddSeconds(1.2);
while (DateTime.Now < deadline)
{
    Application.DoEvents();
    Thread.Sleep(25);
}

if (!form.Text.Contains("EMG", StringComparison.Ordinal))
{
    throw new InvalidOperationException("窗口标题错误");
}

string outputDirectory = Path.Combine(Environment.CurrentDirectory, "output", "verification");
Directory.CreateDirectory(outputDirectory);
string outputPath = Path.Combine(outputDirectory, "emg-ui-separated.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(outputPath, ImageFormat.Png);
}

TabControl tabs = Descendants(form).OfType<TabControl>().Single();
if (tabs.TabPages.Count != 2 || tabs.TabPages[0].Text != "分通道波形" || tabs.TabPages[1].Text != "全部通道叠加图")
{
    throw new InvalidOperationException("波形页签配置错误");
}
if (Descendants(form).OfType<CheckBox>().Count(checkBox => checkBox.Text.StartsWith("CH", StringComparison.Ordinal)) != 8)
{
    throw new InvalidOperationException("独立通道开关数量错误");
}

tabs.SelectedIndex = 1;
Application.DoEvents();
string overlayOutputPath = Path.Combine(outputDirectory, "emg-ui-overlay.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(overlayOutputPath, ImageFormat.Png);
}

FindControl<Button>(form, "停止采集").PerformClick();
deadline = DateTime.Now.AddSeconds(0.5);
while (DateTime.Now < deadline)
{
    Application.DoEvents();
    Thread.Sleep(20);
}

ComboBox mode = Descendants(form).OfType<ComboBox>().Single(box => box.Items.Count == 3 && box.Items[0]?.ToString()?.Contains("肌电") == true);
mode.SelectedIndex = 1;
Application.DoEvents();
TextBox sampleRate = Descendants(form).OfType<TextBox>().Single(box => box.PlaceholderText == "请输入");
if (sampleRate.Text != "100")
{
    throw new InvalidOperationException($"电容默认采样率错误：{sampleRate.Text}");
}
tabs.SelectedIndex = 0;
start = FindControl<Button>(form, "开始采集");
start.PerformClick();

deadline = DateTime.Now.AddSeconds(1.2);
while (DateTime.Now < deadline)
{
    Application.DoEvents();
    Thread.Sleep(25);
}

CheckBox[] capacitanceChannels = Descendants(form).OfType<CheckBox>()
    .Where(checkBox => checkBox.Text.StartsWith("REF", StringComparison.Ordinal) || checkBox.Text.StartsWith("PC", StringComparison.Ordinal))
    .ToArray();
if (capacitanceChannels.Length != 40)
{
    throw new InvalidOperationException($"电容独立通道开关数量错误：{capacitanceChannels.Length}");
}
if (!Descendants(form).OfType<Label>().Any(label => label.Text.Contains("原始值 ÷ 1000", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("电容 pF 换算说明缺失");
}

Button selectNone = FindControl<Button>(form, "全不选");
Button invertSelection = FindControl<Button>(form, "反选");
Button selectAll = FindControl<Button>(form, "全选");
selectNone.PerformClick();
Application.DoEvents();
if (capacitanceChannels.Any(checkBox => checkBox.Checked))
{
    throw new InvalidOperationException("全不选功能错误");
}
invertSelection.PerformClick();
Application.DoEvents();
if (capacitanceChannels.Any(checkBox => !checkBox.Checked))
{
    throw new InvalidOperationException("反选功能错误");
}
selectNone.PerformClick();
capacitanceChannels[0].Checked = true;
capacitanceChannels[1].Checked = true;
Application.DoEvents();
WaveformControl separatedWaveform = Descendants(form).OfType<WaveformControl>()
    .Single(waveform => waveform.DisplayMode == WaveformDisplayMode.Separated);
if (separatedWaveform.Height is < 108 or > 360)
{
    throw new InvalidOperationException($"少通道自适应高度超出范围：{separatedWaveform.Height}");
}

string fewChannelsOutputPath = Path.Combine(outputDirectory, "capacitance-ui-few-channels.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(fewChannelsOutputPath, ImageFormat.Png);
}
selectAll.PerformClick();
Application.DoEvents();

string capacitanceOutputPath = Path.Combine(outputDirectory, "capacitance-ui-separated.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(capacitanceOutputPath, ImageFormat.Png);
}

tabs.SelectedIndex = 1;
Application.DoEvents();
string capacitanceOverlayOutputPath = Path.Combine(outputDirectory, "capacitance-ui-overlay.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(capacitanceOverlayOutputPath, ImageFormat.Png);
}

FindControl<Button>(form, "停止采集").PerformClick();
deadline = DateTime.Now.AddSeconds(0.5);
while (DateTime.Now < deadline)
{
    Application.DoEvents();
    Thread.Sleep(20);
}

mode.SelectedIndex = 2;
Application.DoEvents();
if (sampleRate.Text != "1000")
{
    throw new InvalidOperationException($"静电计默认采样率错误：{sampleRate.Text}");
}
ComboBox range = Descendants(form).OfType<ComboBox>()
    .Single(box => box.Items.Count == 3 && box.Items[0]?.ToString() == "nA");
if (!range.Visible || range.SelectedIndex != 0)
{
    throw new InvalidOperationException("静电计 CH1 档位选择器错误");
}
NumericUpDown port = Descendants(form).OfType<NumericUpDown>()
    .Single(box => box.Maximum == 65535);
if (!port.Enabled || port.Value != 8060)
{
    throw new InvalidOperationException("静电计 UDP 端口选择器错误");
}
CheckBox[] electrostaticChannels = Descendants(form).OfType<CheckBox>()
    .Where(checkBox => checkBox.Text.StartsWith("CH", StringComparison.Ordinal))
    .ToArray();
if (electrostaticChannels.Length != 8 || !electrostaticChannels.Any(checkBox => checkBox.Text.Contains("电荷", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("静电计 8 通道名称或开关数量错误");
}
if (!Descendants(form).OfType<Label>().Any(label => label.Text.StartsWith("CH1 档位必须与板上拨码一致", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("静电计档位提示缺失");
}

start = FindControl<Button>(form, "开始采集");
start.PerformClick();
deadline = DateTime.Now.AddSeconds(1.2);
while (DateTime.Now < deadline)
{
    Application.DoEvents();
    Thread.Sleep(25);
}

tabs.SelectedIndex = 0;
Application.DoEvents();
string electrostaticOutputPath = Path.Combine(outputDirectory, "electrostatic-ui-separated.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(electrostaticOutputPath, ImageFormat.Png);
}

tabs.SelectedIndex = 1;
Application.DoEvents();
string electrostaticOverlayOutputPath = Path.Combine(outputDirectory, "electrostatic-ui-overlay.png");
using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
{
    form.DrawToBitmap(bitmap, form.ClientRectangle);
    bitmap.Save(electrostaticOverlayOutputPath, ImageFormat.Png);
}

FindControl<Button>(form, "停止采集").PerformClick();
deadline = DateTime.Now.AddSeconds(0.5);
while (DateTime.Now < deadline)
{
    Application.DoEvents();
    Thread.Sleep(20);
}

Console.WriteLine($"界面冒烟检查通过：{outputPath}；{overlayOutputPath}；{fewChannelsOutputPath}；{capacitanceOutputPath}；{capacitanceOverlayOutputPath}；{electrostaticOutputPath}；{electrostaticOverlayOutputPath}");
form.Close();
Application.DoEvents();
return 0;

static T FindControl<T>(Control root, string text) where T : Control
{
    foreach (Control control in Descendants(root))
    {
        if (control is T typed && string.Equals(control.Text, text, StringComparison.Ordinal))
        {
            return typed;
        }
    }

    throw new InvalidOperationException($"未找到控件：{text}");
}

static IEnumerable<Control> Descendants(Control root)
{
    foreach (Control child in root.Controls)
    {
        yield return child;
        foreach (Control descendant in Descendants(child))
        {
            yield return descendant;
        }
    }
}
