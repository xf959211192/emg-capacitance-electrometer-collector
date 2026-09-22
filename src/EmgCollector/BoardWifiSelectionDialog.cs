using EmgCollector.Connectivity;

namespace EmgCollector;

internal sealed class BoardWifiSelectionDialog : Form
{
    public string? SelectedSsid { get; private set; }

    public BoardWifiSelectionDialog()
    {
        Text = "选择采集板卡";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);
        Font = new Font("Microsoft YaHei UI", 10F);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.Controls.Add(new Label
        {
            Text = "同时搜索到两块在线板卡，请选择要连接的设备：",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        });
        layout.Controls.Add(CreateBoardButton(
            $"EMG 肌电板卡    Wi-Fi：{WifiConnectionService.EmgSsid}",
            WifiConnectionService.EmgSsid));
        layout.Controls.Add(CreateBoardButton(
            $"电容采集板卡    Wi-Fi：{WifiConnectionService.CapacitanceSsid}",
            WifiConnectionService.CapacitanceSsid));

        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
            DialogResult = DialogResult.Cancel
        };
        layout.Controls.Add(cancelButton);
        CancelButton = cancelButton;
        Controls.Add(layout);
    }

    private Button CreateBoardButton(string text, string ssid)
    {
        var button = new Button
        {
            Text = text,
            Width = 420,
            Height = 48,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 0, 4)
        };
        button.Click += (_, _) =>
        {
            SelectedSsid = ssid;
            DialogResult = DialogResult.OK;
            Close();
        };
        return button;
    }
}
