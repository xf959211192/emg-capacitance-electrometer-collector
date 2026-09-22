using System.Buffers.Binary;

namespace EmgCollector.Protocol;

public static class EmgPacketParser
{
    private static readonly byte[] Head = [0x5A, 0x5A, 0x5A, 0x5A];
    private static readonly byte[] Tail = [0x0D, 0x0A, 0x0D, 0x0A];
    private const int HeaderLength = 12;
    private const int TailLength = 4;

    public static bool TryParse(
        ReadOnlySpan<byte> data,
        DateTimeOffset receivedAt,
        out EmgPacket? packet,
        out string error)
    {
        packet = null;
        error = string.Empty;

        if (data.Length < HeaderLength + TailLength)
        {
            error = $"数据包过短：{data.Length} 字节";
            return false;
        }

        if (!data[..4].SequenceEqual(Head))
        {
            error = "未找到帧头 5A 5A 5A 5A";
            return false;
        }

        ushort layout = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2));
        int channelCount = layout & 0x0F;
        int sampleCount = layout >> 4;
        if (channelCount is <= 0 or > 8)
        {
            error = $"通道数无效：{channelCount}";
            return false;
        }

        if (sampleCount is <= 0 or > 4095)
        {
            error = $"每通道采样点数无效：{sampleCount}";
            return false;
        }

        int expectedLength;
        try
        {
            expectedLength = checked(HeaderLength + channelCount * sampleCount * sizeof(short) + TailLength);
        }
        catch (OverflowException)
        {
            error = "数据包长度溢出";
            return false;
        }

        if (data.Length != expectedLength)
        {
            error = $"数据包长度不符：收到 {data.Length} 字节，协议应为 {expectedLength} 字节";
            return false;
        }

        if (!data[^4..].SequenceEqual(Tail))
        {
            error = "帧尾不是 0D 0A 0D 0A";
            return false;
        }

        ushort counter = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        var channels = new short[channelCount][];
        for (int channel = 0; channel < channelCount; channel++)
        {
            channels[channel] = new short[sampleCount];
        }

        // 协议按每 4 通道分组：先存 CH1~CH4 的全部采样点，再存 CH5~CH8。
        int offset = HeaderLength;
        for (int groupStart = 0; groupStart < channelCount; groupStart += 4)
        {
            int channelsInGroup = Math.Min(4, channelCount - groupStart);
            for (int sample = 0; sample < sampleCount; sample++)
            {
                for (int channelInGroup = 0; channelInGroup < channelsInGroup; channelInGroup++)
                {
                    channels[groupStart + channelInGroup][sample] =
                        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
                    offset += 2;
                }
            }
        }

        packet = new EmgPacket(counter, channelCount, sampleCount, crc, channels, receivedAt);
        return true;
    }
}
