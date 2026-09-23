namespace EmgCollector.Protocol;

public enum ElectrostaticCurrentRange
{
    Nanoampere,
    Microampere,
    Milliampere
}

public static class ElectrostaticMeasurement
{
    public const int ExpectedChannelCount = 8;

    private static readonly string[] ChannelNames =
    [
        "CH1 电流",
        "CH2 电荷",
        "CH3 电流",
        "CH4 电流",
        "CH5 电流",
        "CH6 电压",
        "CH7 电压",
        "CH8 电压"
    ];

    private static readonly string[] FixedChannelUnits = ["", "nC", "μA", "μA", "μA", "V", "V", "V"];

    public static string GetChannelName(int channel) => ChannelNames[ValidateChannel(channel)];

    public static string GetUnit(int channel, ElectrostaticCurrentRange currentRange)
    {
        ValidateChannel(channel);
        if (channel != 0)
        {
            return FixedChannelUnits[channel];
        }

        return currentRange switch
        {
            ElectrostaticCurrentRange.Nanoampere => "nA",
            ElectrostaticCurrentRange.Microampere => "μA",
            ElectrostaticCurrentRange.Milliampere => "mA",
            _ => throw new ArgumentOutOfRangeException(nameof(currentRange))
        };
    }

    public static double GetValue(
        EmgPacket packet,
        int channel,
        int sample,
        ElectrostaticCurrentRange currentRange)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ValidateChannel(channel);

        // 静电计使用与 EMG 相同的 8 通道数据帧；先换算基础电压，
        // 再应用厂家文档中的各通道倍率。
        double baseValue = packet.GetVoltage(channel, sample);
        return baseValue * GetMultiplier(channel, currentRange);
    }

    public static double GetMultiplier(int channel, ElectrostaticCurrentRange currentRange)
    {
        ValidateChannel(channel);
        return channel switch
        {
            // 厂家换算关系原本统一换算为 μA：nA 档×1、μA 档×1000、mA 档×1000000。
            // 改为随档位显示 nA/μA/mA 后，需要同步换算单位，因此三档显示值均为基础值×1000。
            0 => currentRange switch
            {
                ElectrostaticCurrentRange.Nanoampere => 1_000d,
                ElectrostaticCurrentRange.Microampere => 1_000d,
                ElectrostaticCurrentRange.Milliampere => 1_000d,
                _ => throw new ArgumentOutOfRangeException(nameof(currentRange))
            },
            1 => 10d,
            2 => 1d,
            3 => 0.1d,
            4 => 0.01d,
            _ => 1d
        };
    }

    private static int ValidateChannel(int channel)
    {
        if (channel is < 0 or >= ExpectedChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
        return channel;
    }
}
