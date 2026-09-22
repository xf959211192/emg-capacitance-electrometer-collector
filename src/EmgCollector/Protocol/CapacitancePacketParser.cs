using System.Buffers.Binary;

namespace EmgCollector.Protocol;

public static class CapacitancePacketParser
{
    private static readonly byte[] Head = [0x5A, 0x5A, 0x5A, 0x5A];
    private static readonly byte[] Tail = [0x0D, 0x0A, 0x0D, 0x0A];
    private const int HeaderLength = 12;
    private const int TailLength = 4;
    public const int ExpectedPacketLength = HeaderLength +
        CapacitancePacket.ExpectedChannelCount * CapacitancePacket.ExpectedSampleCount * sizeof(uint) +
        TailLength;

    public static bool TryParse(
        ReadOnlySpan<byte> data,
        DateTimeOffset receivedAt,
        out CapacitancePacket? packet,
        out string error)
    {
        packet = null;
        error = string.Empty;

        if (data.Length != ExpectedPacketLength)
        {
            error = $"电容数据包长度不符：收到 {data.Length} 字节，协议应为 {ExpectedPacketLength} 字节";
            return false;
        }

        if (!data[..4].SequenceEqual(Head))
        {
            error = "电容数据包帧头不是 5A 5A 5A 5A";
            return false;
        }

        ushort channelCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2));
        ushort sampleCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
        if (channelCount != CapacitancePacket.ExpectedChannelCount)
        {
            error = $"电容通道数应为 40，实际为 {channelCount}";
            return false;
        }
        if (sampleCount != CapacitancePacket.ExpectedSampleCount)
        {
            error = $"电容每包采样次数应为 10，实际为 {sampleCount}";
            return false;
        }

        if (!data[^4..].SequenceEqual(Tail))
        {
            error = "电容数据包帧尾不是 0D 0A 0D 0A";
            return false;
        }

        uint counter = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        var channels = new uint[channelCount][];
        for (int channel = 0; channel < channelCount; channel++)
        {
            channels[channel] = new uint[sampleCount];
        }

        int offset = HeaderLength;
        // 每次采样依次包含5颗芯片，每颗芯片为 REF + 7个测量通道，共40个 uint32。
        for (int sample = 0; sample < sampleCount; sample++)
        {
            for (int channel = 0; channel < channelCount; channel++)
            {
                channels[channel][sample] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                offset += 4;
            }
        }

        packet = new CapacitancePacket(counter, channelCount, sampleCount, channels, receivedAt);
        return true;
    }
}
