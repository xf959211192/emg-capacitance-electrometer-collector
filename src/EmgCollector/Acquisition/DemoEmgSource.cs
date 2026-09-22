using EmgCollector.Protocol;

namespace EmgCollector.Acquisition;

public sealed class DemoEmgSource : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<EmgPacket>? PacketReceived;
    public PacketStatistics Statistics { get; } = new();

    public void Start(int sampleRate)
    {
        if (_task is { IsCompleted: false })
        {
            throw new InvalidOperationException("演示信号已经启动");
        }

        Statistics.Reset();
        _cts = new CancellationTokenSource();
        _task = GenerateAsync(sampleRate, _cts.Token);
    }

    private async Task GenerateAsync(int sampleRate, CancellationToken cancellationToken)
    {
        const int sampleCount = 128;
        ushort counter = 0;
        long sampleIndex = 0;
        var random = new Random(20260918);
        var period = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var channels = new short[8][];
            for (int channel = 0; channel < channels.Length; channel++)
            {
                channels[channel] = new short[sampleCount];
                double frequency = 18 + channel * 4;
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    double t = (sampleIndex + sample) / (double)sampleRate;
                    double envelope = 0.25 + 0.75 * Math.Pow(Math.Sin(2 * Math.PI * 0.35 * t), 2);
                    double signal = envelope * Math.Sin(2 * Math.PI * frequency * t);
                    double noise = (random.NextDouble() - 0.5) * 0.12;
                    channels[channel][sample] = (short)Math.Clamp((signal + noise) * 1250, short.MinValue, short.MaxValue);
                }
            }

            var packet = new EmgPacket(counter, 8, sampleCount, 0, channels, DateTimeOffset.Now);
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
