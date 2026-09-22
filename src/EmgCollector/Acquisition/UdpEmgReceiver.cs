using System.Net;
using System.Net.Sockets;
using EmgCollector.Protocol;

namespace EmgCollector.Acquisition;

public sealed class UdpEmgReceiver : IAsyncDisposable
{
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public event Action<EmgPacket>? PacketReceived;
    public event Action<string>? ReceiveError;

    public PacketStatistics Statistics { get; } = new();
    public bool IsRunning => _receiveTask is { IsCompleted: false };

    public void Start(IPAddress localAddress, int port)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("采集已经启动");
        }

        Statistics.Reset();
        _cts = new CancellationTokenSource();
        _client = new UdpClient(new IPEndPoint(localAddress, port));
        _client.Client.ReceiveBufferSize = 4 * 1024 * 1024;
        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _client!.ReceiveAsync(cancellationToken);
                if (EmgPacketParser.TryParse(
                    result.Buffer,
                    DateTimeOffset.Now,
                    out EmgPacket? packet,
                    out string error))
                {
                    Statistics.OnValid(packet!.Counter, packet.SampleCount);
                    PacketReceived?.Invoke(packet);
                }
                else
                {
                    Statistics.OnInvalid(error);
                    ReceiveError?.Invoke(error);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Statistics.OnInvalid(ex.Message);
                ReceiveError?.Invoke(ex.Message);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _client?.Dispose();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
        }

        _client = null;
        _receiveTask = null;
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
