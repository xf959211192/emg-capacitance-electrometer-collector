using System.Drawing.Drawing2D;
using EmgCollector.Protocol;

namespace EmgCollector.Controls;

public enum WaveformDisplayMode
{
    Separated,
    Overlay
}

public sealed class WaveformDataBuffer
{
    private const int MaximumChannels = 40;
    private readonly object _sync = new();
    private readonly Queue<double>[] _channels = Enumerable.Range(0, MaximumChannels).Select(_ => new Queue<double>()).ToArray();
    private int _channelCount = 8;
    private int _capacity = 5000;

    public void Reset(int channelCount)
    {
        if (channelCount is <= 0 or > MaximumChannels)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        }

        lock (_sync)
        {
            _channelCount = channelCount;
            foreach (Queue<double> channel in _channels)
            {
                channel.Clear();
            }
        }
    }

    public void Configure(int sampleRate, int windowSeconds)
    {
        lock (_sync)
        {
            _capacity = Math.Clamp(windowSeconds * Math.Max(1, sampleRate), 128, 200_000);
            TrimQueues();
        }
    }

    public void Append(EmgPacket packet)
    {
        lock (_sync)
        {
            for (int channel = 0; channel < Math.Min(_channelCount, packet.ChannelCount); channel++)
            {
                Queue<double> target = _channels[channel];
                foreach (short rawValue in packet.RawChannels[channel])
                {
                    target.Enqueue(rawValue / EmgPacket.AdcCountsPerVolt);
                }
            }
            TrimQueues();
        }
    }

    public void AppendElectrostatic(EmgPacket packet, ElectrostaticCurrentRange currentRange)
    {
        lock (_sync)
        {
            for (int channel = 0; channel < Math.Min(_channelCount, packet.ChannelCount); channel++)
            {
                Queue<double> target = _channels[channel];
                for (int sample = 0; sample < packet.SampleCount; sample++)
                {
                    target.Enqueue(ElectrostaticMeasurement.GetValue(packet, channel, sample, currentRange));
                }
            }
            TrimQueues();
        }
    }

    public void Append(CapacitancePacket packet)
    {
        lock (_sync)
        {
            for (int channel = 0; channel < Math.Min(_channelCount, packet.ChannelCount); channel++)
            {
                Queue<double> target = _channels[channel];
                for (int sample = 0; sample < packet.SampleCount; sample++)
                {
                    target.Enqueue(packet.GetCapacitancePf(channel, sample));
                }
            }
            TrimQueues();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (Queue<double> channel in _channels)
            {
                channel.Clear();
            }
        }
    }

    public double[][] Snapshot(int channelCount)
    {
        lock (_sync)
        {
            return _channels.Take(Math.Min(channelCount, _channelCount)).Select(queue => queue.ToArray()).ToArray();
        }
    }

    private void TrimQueues()
    {
        for (int channel = 0; channel < _channelCount; channel++)
        {
            Queue<double> queue = _channels[channel];
            while (queue.Count > _capacity)
            {
                queue.Dequeue();
            }
        }
    }
}

public sealed class WaveformControl : Control
{
    private readonly WaveformDataBuffer _data;
    private bool[] _visibleChannels = Enumerable.Repeat(true, 8).ToArray();
    private bool[] _referenceChannels = new bool[8];
    private string[] _channelLabels = Enumerable.Range(1, 8).Select(channel => $"CH{channel}").ToArray();
    private string[] _channelUnits = Enumerable.Repeat("V", 8).ToArray();
    private string _overlayUnitLabel = "V";
    private bool _centerOnZero = true;

    public WaveformControl(WaveformDataBuffer data, WaveformDisplayMode displayMode)
    {
        _data = data;
        DisplayMode = displayMode;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(248, 250, 252);
        ForeColor = Color.FromArgb(30, 41, 59);
        ResizeRedraw = true;
    }

    public WaveformDisplayMode DisplayMode { get; }
    public int ChannelCount => _channelLabels.Length;

    public void ConfigureChannels(
        IReadOnlyList<string> labels,
        string unitLabel,
        bool centerOnZero,
        IReadOnlyList<bool>? referenceChannels = null)
    {
        ConfigureChannels(
            labels,
            Enumerable.Repeat(unitLabel, labels.Count).ToArray(),
            unitLabel,
            centerOnZero,
            referenceChannels);
    }

    public void ConfigureChannels(
        IReadOnlyList<string> labels,
        IReadOnlyList<string> channelUnits,
        string overlayUnitLabel,
        bool centerOnZero,
        IReadOnlyList<bool>? referenceChannels = null)
    {
        if (channelUnits.Count != labels.Count)
        {
            throw new ArgumentException("通道单位数量必须与通道数量一致", nameof(channelUnits));
        }

        _channelLabels = labels.ToArray();
        _channelUnits = channelUnits.ToArray();
        _overlayUnitLabel = overlayUnitLabel;
        _centerOnZero = centerOnZero;
        _visibleChannels = Enumerable.Repeat(true, labels.Count).ToArray();
        _referenceChannels = referenceChannels?.ToArray() ?? new bool[labels.Count];
        if (_referenceChannels.Length != labels.Count)
        {
            throw new ArgumentException("参考通道标记数量必须与通道数量一致", nameof(referenceChannels));
        }
        Invalidate();
    }

    public void SetChannelVisible(int channel, bool visible)
    {
        if (channel is < 0 || channel >= _visibleChannels.Length)
        {
            return;
        }

        _visibleChannels[channel] = visible;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        double[][] snapshot = _data.Snapshot(ChannelCount);

        if (DisplayMode == WaveformDisplayMode.Overlay)
        {
            DrawOverlay(e.Graphics, snapshot);
        }
        else
        {
            DrawSeparated(e.Graphics, snapshot);
        }
    }

    private void DrawSeparated(Graphics graphics, double[][] snapshot)
    {
        const int labelWidth = 128;
        const int rightPadding = 12;
        int[] visible = Enumerable.Range(0, ChannelCount).Where(channel => _visibleChannels[channel]).ToArray();
        if (visible.Length == 0)
        {
            using var emptyBrush = new SolidBrush(Color.FromArgb(100, 116, 139));
            graphics.DrawString("请选择至少一个通道", Font, emptyBrush, 16, 16);
            return;
        }

        int laneHeight = Math.Max(1, ClientSize.Height / visible.Length);
        int plotWidth = Math.Max(2, ClientSize.Width - labelWidth - rightPadding);

        using var gridPen = new Pen(Color.FromArgb(226, 232, 240));
        using var axisPen = new Pen(Color.FromArgb(148, 163, 184));
        using var labelBrush = new SolidBrush(ForeColor);
        using var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139));
        using var referenceBrush = new SolidBrush(Color.FromArgb(146, 64, 14));
        using var labelFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        using var smallFont = new Font(Font.FontFamily, 7.5f);

        for (int visibleIndex = 0; visibleIndex < visible.Length; visibleIndex++)
        {
            int channel = visible[visibleIndex];
            int top = visibleIndex * laneHeight;
            int center = top + laneHeight / 2;
            float plotTop = center - laneHeight * 0.38f;
            float plotBottom = center + laneHeight * 0.38f;
            graphics.DrawLine(gridPen, 0, top, ClientSize.Width, top);
            graphics.DrawString(_channelLabels[channel], labelFont, _referenceChannels[channel] ? referenceBrush : labelBrush, 8, top + 4);
            graphics.DrawString(_channelUnits[channel], smallFont, mutedBrush, 8, top + 22);

            double[] values = snapshot[channel];
            if (values.Length < 2)
            {
                graphics.DrawLine(axisPen, labelWidth, plotTop, labelWidth, plotBottom);
                continue;
            }

            Func<double, float> mapY;
            double minimum;
            double maximum;
            if (_centerOnZero)
            {
                double maxAbs = Math.Max(values.Max(value => Math.Abs(value)), 0.01);
                double verticalScale = laneHeight * 0.38 / maxAbs;
                mapY = value => (float)(center - value * verticalScale);
                minimum = -maxAbs;
                maximum = maxAbs;
            }
            else
            {
                (minimum, maximum) = GetRange(values);
                double scale = laneHeight * 0.76 / (maximum - minimum);
                mapY = value => (float)(plotTop + (maximum - value) * scale);
            }

            graphics.DrawLine(axisPen, labelWidth, plotTop, labelWidth, plotBottom);
            for (int tick = 0; tick < 3; tick++)
            {
                float y = plotTop + tick * (plotBottom - plotTop) / 2f;
                double tickValue = maximum - tick * (maximum - minimum) / 2.0;
                graphics.DrawLine(gridPen, labelWidth, y, ClientSize.Width - rightPadding, y);
                DrawRightAligned(graphics, FormatAxisNumber(tickValue, _channelUnits[channel]), smallFont, mutedBrush, labelWidth - 5, y - smallFont.Height / 2f);
            }

            PointF[] points = BuildPoints(values, plotWidth, labelWidth, mapY);
            using var wavePen = CreateChannelPen(channel, 1.25f);
            graphics.DrawLines(wavePen, points);
        }

        graphics.DrawLine(gridPen, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
    }

    private void DrawOverlay(Graphics graphics, double[][] snapshot)
    {
        const int leftPadding = 94;
        const int rightPadding = 14;
        const int bottomPadding = 26;
        int plotWidth = Math.Max(2, ClientSize.Width - leftPadding - rightPadding);
        int[] visible = Enumerable.Range(0, ChannelCount).Where(channel => _visibleChannels[channel]).ToArray();
        bool hasMixedUnits = _channelUnits.Distinct(StringComparer.Ordinal).Skip(1).Any();
        int legendItemWidth = hasMixedUnits ? 114 : ChannelCount > 8 ? 68 : 64;
        int legendColumns = Math.Max(1, plotWidth / legendItemWidth);
        int legendRows = Math.Max(1, (int)Math.Ceiling(visible.Length / (double)legendColumns));
        int topPadding = 14 + legendRows * 20;
        int plotHeight = Math.Max(2, ClientSize.Height - topPadding - bottomPadding);

        using var gridPen = new Pen(Color.FromArgb(226, 232, 240));
        using var axisPen = new Pen(Color.FromArgb(148, 163, 184));
        using var labelBrush = new SolidBrush(ForeColor);
        using var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139));
        using var smallFont = new Font(Font.FontFamily, 8);
        using var legendFont = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);

        if (visible.Length == 0)
        {
            graphics.DrawString("请选择至少一个通道", Font, mutedBrush, leftPadding + 12, topPadding + 12);
            return;
        }

        bool hasValues = false;
        double minimum = double.MaxValue;
        double maximum = double.MinValue;
        foreach (int channel in visible)
        {
            foreach (double value in snapshot[channel])
            {
                hasValues = true;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        if (!hasValues)
        {
            minimum = 0;
            maximum = 1;
        }
        if (_centerOnZero)
        {
            double maxAbs = Math.Max(Math.Max(Math.Abs(minimum), Math.Abs(maximum)), 0.01);
            minimum = -maxAbs;
            maximum = maxAbs;
        }
        else
        {
            (minimum, maximum) = ExpandRange(minimum, maximum);
        }

        for (int line = 0; line <= 4; line++)
        {
            float y = topPadding + line * plotHeight / 4f;
            graphics.DrawLine(gridPen, leftPadding, y, ClientSize.Width - rightPadding, y);
            double tickValue = maximum - line * (maximum - minimum) / 4.0;
            DrawRightAligned(graphics, FormatAxisNumber(tickValue, _overlayUnitLabel), smallFont, mutedBrush, leftPadding - 6, y - smallFont.Height / 2f);
        }
        graphics.DrawLine(axisPen, leftPadding, topPadding, leftPadding, topPadding + plotHeight);
        graphics.DrawLine(axisPen, leftPadding, topPadding + plotHeight, ClientSize.Width - rightPadding, topPadding + plotHeight);
        graphics.DrawString(_overlayUnitLabel, smallFont, mutedBrush, 5, topPadding - smallFont.Height - 3);

        for (int visibleIndex = 0; visibleIndex < visible.Length; visibleIndex++)
        {
            int channel = visible[visibleIndex];
            int legendX = leftPadding + visibleIndex % legendColumns * legendItemWidth;
            int legendY = 8 + visibleIndex / legendColumns * 20;
            using var legendPen = CreateChannelPen(channel, 3f);
            graphics.DrawLine(legendPen, legendX, legendY + 7, legendX + 12, legendY + 7);
            string legendText = hasMixedUnits
                ? $"{_channelLabels[channel]}({_channelUnits[channel]})"
                : _channelLabels[channel];
            graphics.DrawString(legendText, legendFont, labelBrush, legendX + 16, legendY);

            double[] values = snapshot[channel];
            if (values.Length < 2)
            {
                continue;
            }

            double scale = plotHeight / (maximum - minimum);
            PointF[] points = BuildPoints(
                values,
                plotWidth,
                leftPadding,
                value => (float)(topPadding + (maximum - value) * scale));
            using var wavePen = CreateChannelPen(channel, _referenceChannels[channel] ? 1.8f : 1.05f);
            graphics.DrawLines(wavePen, points);
        }
    }

    private Pen CreateChannelPen(int channel, float width)
    {
        var pen = new Pen(GetChannelColor(channel), width);
        if (_referenceChannels[channel])
        {
            pen.DashStyle = DashStyle.Dash;
        }
        return pen;
    }

    private Color GetChannelColor(int channel)
    {
        double hue = channel * 137.508 % 360;
        double saturation = _referenceChannels[channel] ? 0.85 : 0.68;
        double lightness = _referenceChannels[channel] ? 0.38 : 0.46;
        return ColorFromHsl(hue, saturation, lightness);
    }

    private static string FormatAxisNumber(double value, string unitLabel) =>
        unitLabel == "pF" ? value.ToString("N3") : value.ToString("F3");

    private static void DrawRightAligned(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        float right,
        float y)
    {
        SizeF size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, brush, right - size.Width, y);
    }

    private static (double Minimum, double Maximum) GetRange(double[] values) => ExpandRange(values.Min(), values.Max());

    private static (double Minimum, double Maximum) ExpandRange(double minimum, double maximum)
    {
        if (Math.Abs(maximum - minimum) < 1)
        {
            minimum -= 1;
            maximum += 1;
        }
        double padding = (maximum - minimum) * 0.08;
        return (minimum - padding, maximum + padding);
    }

    private static PointF[] BuildPoints(double[] values, int plotWidth, int left, Func<double, float> mapY)
    {
        int displayedPoints = Math.Min(values.Length, plotWidth * 2);
        int sourceStart = values.Length - displayedPoints;
        int pointCount = Math.Max(2, Math.Min(plotWidth, displayedPoints));
        var points = new PointF[pointCount];
        for (int pixel = 0; pixel < pointCount; pixel++)
        {
            int sourceIndex = sourceStart + (int)((long)pixel * (displayedPoints - 1) / Math.Max(1, pointCount - 1));
            float x = left + pixel * (plotWidth - 1f) / Math.Max(1, pointCount - 1);
            points[pixel] = new PointF(x, mapY(values[sourceIndex]));
        }
        return points;
    }

    private static Color ColorFromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double segment = hue / 60;
        double x = chroma * (1 - Math.Abs(segment % 2 - 1));
        (double r, double g, double b) = segment switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        double match = lightness - chroma / 2;
        return Color.FromArgb(
            (int)Math.Round((r + match) * 255),
            (int)Math.Round((g + match) * 255),
            (int)Math.Round((b + match) * 255));
    }
}
