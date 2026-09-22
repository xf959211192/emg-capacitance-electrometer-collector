namespace EmgCollector.Protocol;

public sealed record EmgPacket(
    ushort Counter,
    int ChannelCount,
    int SampleCount,
    uint Crc,
    short[][] RawChannels,
    DateTimeOffset ReceivedAt)
{
    public const double AdcCountsPerVolt = 3276.8;

    public double GetVoltage(int channel, int sample) =>
        RawChannels[channel][sample] / AdcCountsPerVolt;
}
