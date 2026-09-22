using EmgCollector.Protocol;

namespace EmgCollector.Acquisition;

public sealed class DemoCapacitanceSource : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<CapacitancePacket>? PacketReceived;
    public PacketStatistics Statistics { get; } = new();

    public void Start(int sampleRate)
    {
        if (_task is { IsCompleted: false })
        {
            throw new InvalidOperationException("电容演示信号已经启动");
        }

        Statistics.Reset();
        _cts = new CancellationTokenSource();
        _task = GenerateAsync(sampleRate, _cts.Token);
    }

    private async Task GenerateAsync(int sampleRate, CancellationToken cancellationToken)
    {
        const int sampleCount = CapacitancePacket.ExpectedSampleCount;
        uint counter = 0;
        long sampleIndex = 0;
        var period = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var channels = new uint[CapacitancePacket.ExpectedChannelCount][];
            for (int channel = 0; channel < channels.Length; channel++)
            {
                channels[channel] = new uint[sampleCount];
                double baseValue = CapacitancePacket.IsReferenceChannel(channel) ? 170_000 : 180_000 + channel * 1_200;
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    double t = (sampleIndex + sample) / (double)sampleRate;
                    double signal = 6_000 * Math.Sin(2 * Math.PI * (0.15 + channel * 0.01) * t + channel * 0.3);
                    channels[channel][sample] = (uint)Math.Max(0, Math.Round(baseValue + signal));
                }
            }

            var packet = new CapacitancePacket(counter, channels.Length, sampleCount, channels, DateTimeOffset.Now);
            Statistics.OnValid(counter, sampleCount);
            PacketReceived?.Invoke(packet);
            counter++;
            sampleIndex += sampleCount;
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        if (_task is not null)
        {
            try
            {
                await _task;
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
        }

        _task = null;
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
