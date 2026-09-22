using System.Globalization;
using System.Text;
using EmgCollector.Protocol;

namespace EmgCollector.Recording;

public sealed class CapacitanceCsvRecorder : IDisposable
{
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private DateTimeOffset? _startedAt;
    private bool[] _enabledChannels = Enumerable.Repeat(true, CapacitancePacket.ExpectedChannelCount).ToArray();

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _writer is not null;
            }
        }
    }

    public void Start(string path, IReadOnlyList<bool> enabledChannels)
    {
        lock (_sync)
        {
            StopCore();
            if (enabledChannels.Count != CapacitancePacket.ExpectedChannelCount)
            {
                throw new ArgumentException("电容通道选择必须包含 40 个状态", nameof(enabledChannels));
            }
            if (!enabledChannels.Any(enabled => enabled))
            {
                throw new ArgumentException("至少选择一个电容通道", nameof(enabledChannels));
            }

            _enabledChannels = enabledChannels.ToArray();
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1 << 20);
            var header = new StringBuilder("接收时间,相对时间_s");
            for (int channel = 0; channel < _enabledChannels.Length; channel++)
            {
                if (_enabledChannels[channel])
                {
                    header.Append(',').Append(CapacitancePacket.GetChannelName(channel)).Append("电容_pF");
                }
            }
            _writer.WriteLine(header.ToString());
            _startedAt = null;
        }
    }

    public void Write(CapacitancePacket packet, int sampleRate)
    {
        lock (_sync)
        {
            if (_writer is null)
            {
                return;
            }

            _startedAt ??= packet.ReceivedAt - TimeSpan.FromSeconds((packet.SampleCount - 1) / (double)sampleRate);
            var builder = new StringBuilder(packet.SampleCount * 500);
            for (int sample = 0; sample < packet.SampleCount; sample++)
            {
                DateTimeOffset sampleTime = packet.ReceivedAt -
                    TimeSpan.FromSeconds((packet.SampleCount - 1 - sample) / (double)sampleRate);
                double relativeSeconds = (sampleTime - _startedAt.Value).TotalSeconds;
                builder.Append(sampleTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture));
                builder.Append(',').Append(relativeSeconds.ToString("F7", CultureInfo.InvariantCulture));

                for (int channel = 0; channel < packet.ChannelCount; channel++)
                {
                    if (_enabledChannels[channel])
                    {
                        builder.Append(',').Append(packet.GetCapacitancePf(channel, sample).ToString("F3", CultureInfo.InvariantCulture));
                    }
                }
                builder.AppendLine();
            }
            _writer.Write(builder.ToString());
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopCore();
        }
    }

    private void StopCore()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _startedAt = null;
    }

    public void Dispose() => Stop();
}
