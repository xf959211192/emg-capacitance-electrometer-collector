using System.Net;
using System.Text;
using EmgCollector.Acquisition;
using EmgCollector.Security;

namespace EmgCollector;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 1 && string.Equals(args[0], FirewallPermissionService.ConfigureArgument, StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = FirewallPermissionService.ConfigureElevated();
            return;
        }

        if (args.Length >= 4 && string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase))
        {
            RunProbeAsync(args).GetAwaiter().GetResult();
            return;
        }

        Application.Run(new MainForm());
    }

    private static async Task RunProbeAsync(string[] args)
    {
        int port = int.Parse(args[1]);
        int seconds = int.Parse(args[2]);
        string outputPath = Path.GetFullPath(args[3]);
        var lines = new List<string>();
        var sync = new object();
        long validPackets = 0;
        long invalidPackets = 0;

        void AddLine(string text)
        {
            lock (sync)
            {
                lines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {text}");
            }
        }

        await using var receiver = new UdpEmgReceiver();
        receiver.PacketReceived += packet =>
        {
            long count = Interlocked.Increment(ref validPackets);
            if (count <= 3)
            {
                AddLine($"有效包 {count}：计数={packet.Counter}，通道={packet.ChannelCount}，每通道采样={packet.SampleCount}");
            }
        };
        receiver.ReceiveError += error =>
        {
            Interlocked.Increment(ref invalidPackets);
            AddLine($"接收错误：{error}");
        };

        try
        {
            receiver.Start(IPAddress.Any, port);
            AddLine($"探针已监听 0.0.0.0:{port}，持续 {seconds} 秒");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            AddLine($"探针异常：{ex}");
        }
        finally
        {
            await receiver.StopAsync();
            StatisticsSnapshot stats = receiver.Statistics.Snapshot();
            AddLine($"汇总：有效包={validPackets}，无效包={invalidPackets}，统计有效包={stats.ReceivedPackets}，丢包={stats.LostPackets}");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllLinesAsync(outputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
    }
}
