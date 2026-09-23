namespace EmgCollector;

internal sealed class ElectrostaticWiringDialog : Form
{
    private const string ResourceName = "EmgCollector.Assets.ElectrostaticChannelWiring.png";
    private readonly PictureBox _pictureBox = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        SizeMode = PictureBoxSizeMode.Zoom,
        TabStop = false,
        AccessibleName = "静电计八通道与CH1档位图"
    };

    public ElectrostaticWiringDialog()
    {
        Text = "静电计通道与档位图";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 560);
        Size = new Size(1100, 760);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowIcon = false;
        MinimizeBox = false;

        var title = new Label
        {
            Text = "静电计通道与档位图",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5)
        };
        var explanation = new Label
        {
            Text = "蓝色 IN1～IN8 为内侧信号输入，红色 OUT1～OUT8 为外侧信号 GND/返回端；CH1 拨码从上到下为 mA、μA、nA，必须与软件档位一致。",
            AutoSize = true,
            MaximumSize = new Size(1020, 0),
            ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(0, 0, 0, 8)
        };
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 12, 14, 0)
        };
        header.Controls.Add(title);
        header.Controls.Add(explanation);

        var closeButton = new Button
        {
            Text = "关闭",
            AutoSize = true,
            Padding = new Padding(18, 4, 18, 4),
            DialogResult = DialogResult.OK,
            Margin = new Padding(0)
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(14, 8, 14, 12)
        };
        footer.Controls.Add(closeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_pictureBox, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);

        AcceptButton = closeButton;
        CancelButton = closeButton;
        LoadEmbeddedImage();
    }

    private void LoadEmbeddedImage()
    {
        using Stream? stream = typeof(ElectrostaticWiringDialog).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException("未找到内置的静电计接线图资源。");
        }

        using Image source = Image.FromStream(stream);
        _pictureBox.Image = new Bitmap(source);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pictureBox.Image?.Dispose();
            _pictureBox.Dispose();
        }
        base.Dispose(disposing);
    }
}
