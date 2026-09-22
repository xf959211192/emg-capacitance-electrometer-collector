using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EmgCollector.Acquisition;
using EmgCollector.Protocol;
using EmgCollector.Recording;

var tests = new (string Name, Action Run)[]
{
    ("解析 8 通道 128 点及正负值", ParseEightChannels),
    ("拒绝错误帧尾", RejectBadTail),
    ("拒绝长度不一致", RejectBadLength),
    ("包计数回绕不误报丢包", CounterWrap),
    ("统计真实丢包和乱序", PacketGaps),
    ("UDP 回环接收完整数据包", UdpLoopback),
    ("CSV 仅保留时间和 8 通道电压", CsvOutput),
    ("CSV 按独立通道开关导出", CsvSelectedChannels),
    ("解析电容 40 通道 10 次采样及通道顺序", ParseCapacitancePacket),
    ("拒绝错误电容帧格式", RejectBadCapacitancePacket),
    ("电容 32 位包计数回绕不误报丢包", CapacitanceCounterWrap),
    ("UDP 回环接收电容数据包", CapacitanceUdpLoopback),
    ("电容 CSV 仅导出所选 pF 值", CapacitanceCsvSelectedChannels),
    ("静电计 8 通道单位与倍率换算", ElectrostaticConversions),
    ("静电计 CSV 仅导出所选物理量", ElectrostaticCsvSelectedChannels)
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"通过：{name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"失败：{name} - {ex.Message}");
    }
}

Console.WriteLine($"共 {tests.Length} 项，失败 {failures} 项");
return failures == 0 ? 0 : 1;

static byte[] BuildPacket(ushort counter = 0)
{
    const int channels = 8;
    const int samples = 128;
    var data = new byte[12 + channels * samples * 2 + 4];
    data[0] = data[1] = data[2] = data[3] = 0x5A;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), (ushort)((samples << 4) | channels));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), counter);

    int offset = 12;
    for (int groupStart = 0; groupStart < channels; groupStart += 4)
    {
        for (int sample = 0; sample < samples; sample++)
        {
            for (int channelInGroup = 0; channelInGroup < 4; channelInGroup++)
            {
                int channel = groupStart + channelInGroup;
                short value = (short)((channel + 1) * 1000 + sample);
                if (channel == 4 && sample == 7)
                {
                    value = -225;
                }

                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);
                offset += 2;
            }
        }
    }

    data[^4] = 0x0D;
    data[^3] = 0x0A;
    data[^2] = 0x0D;
    data[^1] = 0x0A;
    return data;
}

static byte[] BuildCapacitancePacket(uint counter = 0)
{
    const int channels = 40;
    const int samples = 10;
    var data = new byte[12 + channels * samples * sizeof(uint) + 4];
    data[0] = data[1] = data[2] = data[3] = 0x5A;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), channels);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), samples);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8, 4), counter);

    int offset = 12;
    for (int sample = 0; sample < samples; sample++)
    {
        for (int channel = 0; channel < channels; channel++)
        {
            uint value = (uint)((channel + 1) * 100_000 + sample);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
            offset += 4;
        }
    }

    data[^4] = 0x0D;
    data[^3] = 0x0A;
    data[^2] = 0x0D;
    data[^1] = 0x0A;
    return data;
}

static void ParseEightChannels()
{
    bool ok = EmgPacketParser.TryParse(BuildPacket(882), DateTimeOffset.Now, out EmgPacket? packet, out string error);
    Assert(ok, error);
    Assert(packet!.Counter == 882, "包计数错误");
    Assert(packet.ChannelCount == 8 && packet.SampleCount == 128, "通道数或采样点数错误");
    Assert(packet.RawChannels[2][74] == 3074, "CH3 第 74 点位置解析错误");
    Assert(packet.RawChannels[4][7] == -225, "有符号小端值解析错误");
    Assert(Math.Abs(packet.GetVoltage(4, 7) - (-225 / 3276.8)) < 1e-12, "电压换算错误");
}

static void RejectBadTail()
{
    byte[] data = BuildPacket();
    data[^1] = 0;
    Assert(!EmgPacketParser.TryParse(data, DateTimeOffset.Now, out _, out string error), "错误帧尾被接受");
    Assert(error.Contains("帧尾"), "错误信息不明确");
}

static void RejectBadLength()
{
    byte[] data = BuildPacket()[..^2];
    Assert(!EmgPacketParser.TryParse(data, DateTimeOffset.Now, out _, out string error), "错误长度被接受");
    Assert(error.Contains("长度"), "错误信息不明确");
}

static void CounterWrap()
{
    var stats = new PacketStatistics();
    stats.OnValid(65535, 128);
    stats.OnValid(0, 128);
    StatisticsSnapshot snapshot = stats.Snapshot();
    Assert(snapshot.LostPackets == 0 && snapshot.OutOfOrderPackets == 0, "计数回绕被误报");
}

static void PacketGaps()
{
    var stats = new PacketStatistics();
    stats.OnValid(10, 128);
    stats.OnValid(13, 128);
    stats.OnValid(12, 128);
    stats.OnValid(14, 128);
    StatisticsSnapshot snapshot = stats.Snapshot();
    Assert(snapshot.LostPackets == 2, "丢包数计算错误");
    Assert(snapshot.OutOfOrderPackets == 1, "乱序数计算错误");
    Assert(snapshot.LastCounter == 14, "乱序包不应回退最新计数");
}

static void UdpLoopback()
{
    using var portProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    int port = ((IPEndPoint)portProbe.Client.LocalEndPoint!).Port;
    portProbe.Close();

    var receiver = new UdpEmgReceiver();
    var completion = new TaskCompletionSource<EmgPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
    receiver.PacketReceived += packet => completion.TrySetResult(packet);
    receiver.Start(IPAddress.Loopback, port);

    using var sender = new UdpClient();
    sender.Send(BuildPacket(321), new IPEndPoint(IPAddress.Loopback, port));
    EmgPacket packet = completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    receiver.DisposeAsync().AsTask().GetAwaiter().GetResult();

    Assert(packet.Counter == 321, "UDP 接收后的包计数错误");
    Assert(packet.RawChannels[4][7] == -225, "UDP 接收后的采样值错误");
}

static void CsvOutput()
{
    string outputDirectory = Path.Combine(Environment.CurrentDirectory, "output", "verification");
    Directory.CreateDirectory(outputDirectory);
    string path = Path.Combine(outputDirectory, "csv-recorder-test.csv");
    Assert(EmgPacketParser.TryParse(BuildPacket(99), DateTimeOffset.Now, out EmgPacket? packet, out string error), error);

    using (var recorder = new CsvRecorder())
    {
        recorder.Start(path);
        recorder.Write(packet!, 1000);
    }

    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    Assert(lines.Length == 129, "CSV 行数错误");
    string[] headers = lines[0].TrimStart('\uFEFF').Split(',');
    Assert(headers.Length == 10, "CSV 应为 10 列");
    Assert(headers[0] == "接收时间" && headers[1] == "相对时间_s", "CSV 时间列错误");
    Assert(headers[2] == "CH1电压_V" && headers[9] == "CH8电压_V", "CSV 电压列错误");
    string[] columns = lines[8].Split(',');
    Assert(columns.Length == 10, "CSV 数据行应为 10 列");
    Assert(columns[1] == "0.0070000", "CSV 相对时间错误");
    Assert(Math.Abs(double.Parse(columns[6], System.Globalization.CultureInfo.InvariantCulture) - (-225 / 3276.8)) < 1e-8, "CSV 电压换算错误");
}

static void CsvSelectedChannels()
{
    string outputDirectory = Path.Combine(Environment.CurrentDirectory, "output", "verification");
    Directory.CreateDirectory(outputDirectory);
    string path = Path.Combine(outputDirectory, "csv-selected-channels-test.csv");
    Assert(EmgPacketParser.TryParse(BuildPacket(100), DateTimeOffset.Now, out EmgPacket? packet, out string error), error);
    bool[] enabledChannels = [false, true, false, false, false, false, false, true];

    using (var recorder = new CsvRecorder())
    {
        recorder.Start(path, enabledChannels);
        recorder.Write(packet!, 1000);
    }

    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    string[] headers = lines[0].TrimStart('\uFEFF').Split(',');
    Assert(headers.SequenceEqual(["接收时间", "相对时间_s", "CH2电压_V", "CH8电压_V"]), "CSV 通道表头与开关不一致");
    string[] columns = lines[1].Split(',');
    Assert(columns.Length == 4, "CSV 关闭的通道仍被导出");
    Assert(Math.Abs(double.Parse(columns[2], System.Globalization.CultureInfo.InvariantCulture) - (2000 / 3276.8)) < 1e-8, "CH2 电压错误");
    Assert(Math.Abs(double.Parse(columns[3], System.Globalization.CultureInfo.InvariantCulture) - (8000 / 3276.8)) < 1e-8, "CH8 电压错误");
}

static void ParseCapacitancePacket()
{
    bool ok = CapacitancePacketParser.TryParse(
        BuildCapacitancePacket(0xF1020304),
        DateTimeOffset.Now,
        out CapacitancePacket? packet,
        out string error);
    Assert(ok, error);
    Assert(packet!.Counter == 0xF1020304, "电容包计数解析错误");
    Assert(packet.ChannelCount == 40 && packet.SampleCount == 10, "电容通道数或采样次数错误");
    Assert(packet.RawChannels[0][0] == 100_000, "REF1 数据位置错误");
    Assert(packet.RawChannels[8][3] == 900_003, "REF2 数据位置错误");
    Assert(packet.RawChannels[39][9] == 4_000_009, "PC35 数据位置错误");
    Assert(Math.Abs(packet.GetCapacitancePf(39, 9) - 4000.009) < 1e-12, "电容 pF 换算错误");
    Assert(CapacitancePacket.GetChannelName(0) == "REF1", "REF1 通道名错误");
    Assert(CapacitancePacket.GetChannelName(1) == "PC1", "PC1 通道名错误");
    Assert(CapacitancePacket.GetChannelName(7) == "PC7", "PC7 通道名错误");
    Assert(CapacitancePacket.GetChannelName(8) == "REF2", "REF2 通道名错误");
    Assert(CapacitancePacket.GetChannelName(39) == "PC35", "PC35 通道名错误");
}

static void RejectBadCapacitancePacket()
{
    byte[] badLength = BuildCapacitancePacket()[..^1];
    Assert(!CapacitancePacketParser.TryParse(badLength, DateTimeOffset.Now, out _, out string lengthError), "错误电容长度被接受");
    Assert(lengthError.Contains("长度"), "电容长度错误信息不明确");

    byte[] badTail = BuildCapacitancePacket();
    badTail[^1] = 0;
    Assert(!CapacitancePacketParser.TryParse(badTail, DateTimeOffset.Now, out _, out string tailError), "错误电容帧尾被接受");
    Assert(tailError.Contains("帧尾"), "电容帧尾错误信息不明确");
}

static void CapacitanceCounterWrap()
{
    var stats = new PacketStatistics();
    stats.OnValid(uint.MaxValue, 10);
    stats.OnValid(0u, 10);
    StatisticsSnapshot snapshot = stats.Snapshot();
    Assert(snapshot.LostPackets == 0 && snapshot.OutOfOrderPackets == 0, "32 位包计数回绕被误报");
    Assert(snapshot.LastCounter == 0, "32 位回绕后的最新计数错误");
}

static void CapacitanceUdpLoopback()
{
    using var portProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    int port = ((IPEndPoint)portProbe.Client.LocalEndPoint!).Port;
    portProbe.Close();

    var receiver = new UdpCapacitanceReceiver();
    var completion = new TaskCompletionSource<CapacitancePacket>(TaskCreationOptions.RunContinuationsAsynchronously);
    receiver.PacketReceived += packet => completion.TrySetResult(packet);
    receiver.Start(IPAddress.Loopback, port);

    using var sender = new UdpClient();
    sender.Send(BuildCapacitancePacket(987_654_321), new IPEndPoint(IPAddress.Loopback, port));
    CapacitancePacket packet = completion.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    receiver.DisposeAsync().AsTask().GetAwaiter().GetResult();

    Assert(packet.Counter == 987_654_321, "电容 UDP 接收后的包计数错误");
    Assert(packet.RawChannels[39][9] == 4_000_009, "电容 UDP 接收后的采样值错误");
}

static void CapacitanceCsvSelectedChannels()
{
    string outputDirectory = Path.Combine(Environment.CurrentDirectory, "output", "verification");
    Directory.CreateDirectory(outputDirectory);
    string path = Path.Combine(outputDirectory, "capacitance-selected-channels-test.csv");
    Assert(CapacitancePacketParser.TryParse(BuildCapacitancePacket(123), DateTimeOffset.Now, out CapacitancePacket? packet, out string error), error);
    var enabledChannels = new bool[40];
    enabledChannels[0] = true;
    enabledChannels[39] = true;

    using (var recorder = new CapacitanceCsvRecorder())
    {
        recorder.Start(path, enabledChannels);
        recorder.Write(packet!, 100);
    }

    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    Assert(lines.Length == 11, "电容 CSV 行数错误");
    string[] headers = lines[0].TrimStart('\uFEFF').Split(',');
    Assert(headers.SequenceEqual(["接收时间", "相对时间_s", "REF1电容_pF", "PC35电容_pF"]), "电容 CSV 表头与开关不一致");
    string[] first = lines[1].Split(',');
    string[] last = lines[10].Split(',');
    Assert(first.Length == 4 && first[1] == "0.0000000", "电容 CSV 首行时间或列数错误");
    Assert(last[1] == "0.0900000", "电容 CSV 相对时间错误");
    Assert(first[2] == "100.000" && first[3] == "4000.000", "电容 CSV 首次采样值错误");
    Assert(last[2] == "100.009" && last[3] == "4000.009", "电容 CSV 末次采样值错误");
}

static void ElectrostaticConversions()
{
    Assert(EmgPacketParser.TryParse(BuildPacket(456), DateTimeOffset.Now, out EmgPacket? packet, out string error), error);
    double ch1Base = 1000 / EmgPacket.AdcCountsPerVolt;
    double ch2Base = 2000 / EmgPacket.AdcCountsPerVolt;
    double ch4Base = 4000 / EmgPacket.AdcCountsPerVolt;
    double ch5Base = 5000 / EmgPacket.AdcCountsPerVolt;

    Assert(ElectrostaticMeasurement.GetChannelName(1) == "CH2 电荷", "静电计 CH2 名称错误");
    Assert(ElectrostaticMeasurement.GetUnit(0) == "μA" && ElectrostaticMeasurement.GetUnit(1) == "nC", "静电计通道单位错误");
    Assert(Math.Abs(ElectrostaticMeasurement.GetValue(packet!, 0, 0, ElectrostaticCurrentRange.Nanoampere) - ch1Base) < 1e-12, "CH1 nA 档换算错误");
    Assert(Math.Abs(ElectrostaticMeasurement.GetValue(packet!, 0, 0, ElectrostaticCurrentRange.Microampere) - ch1Base * 1000) < 1e-12, "CH1 μA 档换算错误");
    Assert(Math.Abs(ElectrostaticMeasurement.GetValue(packet!, 0, 0, ElectrostaticCurrentRange.Milliampere) - ch1Base * 1_000_000) < 1e-9, "CH1 mA 档换算错误");
    Assert(Math.Abs(ElectrostaticMeasurement.GetValue(packet!, 1, 0, ElectrostaticCurrentRange.Nanoampere) - ch2Base * 10) < 1e-12, "CH2 电荷换算错误");
    Assert(Math.Abs(ElectrostaticMeasurement.GetValue(packet!, 3, 0, ElectrostaticCurrentRange.Nanoampere) - ch4Base * 0.1) < 1e-12, "CH4 电流换算错误");
    Assert(Math.Abs(ElectrostaticMeasurement.GetValue(packet!, 4, 0, ElectrostaticCurrentRange.Nanoampere) - ch5Base * 0.01) < 1e-12, "CH5 电流换算错误");
}

static void ElectrostaticCsvSelectedChannels()
{
    string outputDirectory = Path.Combine(Environment.CurrentDirectory, "output", "verification");
    Directory.CreateDirectory(outputDirectory);
    string path = Path.Combine(outputDirectory, "electrostatic-selected-channels-test.csv");
    Assert(EmgPacketParser.TryParse(BuildPacket(789), DateTimeOffset.Now, out EmgPacket? packet, out string error), error);
    bool[] enabledChannels = [true, true, false, false, false, true, false, false];

    using (var recorder = new ElectrostaticCsvRecorder())
    {
        recorder.Start(path, enabledChannels, ElectrostaticCurrentRange.Microampere);
        recorder.Write(packet!, 1000);
    }

    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    Assert(lines.Length == 129, "静电计 CSV 行数错误");
    string[] headers = lines[0].TrimStart('\uFEFF').Split(',');
    Assert(headers.SequenceEqual(["接收时间", "相对时间_s", "CH1电流_μA", "CH2电荷_nC", "CH6电压_V"]), "静电计 CSV 表头或通道选择错误");
    string[] first = lines[1].Split(',');
    Assert(first.Length == 5 && first[1] == "0.0000000", "静电计 CSV 时间或列数错误");
    Assert(Math.Abs(double.Parse(first[2], System.Globalization.CultureInfo.InvariantCulture) - 1000 / EmgPacket.AdcCountsPerVolt * 1000) < 1e-8, "静电计 CSV CH1 换算错误");
    Assert(Math.Abs(double.Parse(first[3], System.Globalization.CultureInfo.InvariantCulture) - 2000 / EmgPacket.AdcCountsPerVolt * 10) < 1e-8, "静电计 CSV CH2 换算错误");
    Assert(Math.Abs(double.Parse(first[4], System.Globalization.CultureInfo.InvariantCulture) - 6000 / EmgPacket.AdcCountsPerVolt) < 1e-8, "静电计 CSV CH6 换算错误");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
