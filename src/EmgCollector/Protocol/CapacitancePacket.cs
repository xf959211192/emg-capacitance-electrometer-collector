namespace EmgCollector.Protocol;

public sealed record CapacitancePacket(
    uint Counter,
    int ChannelCount,
    int SampleCount,
    uint[][] RawChannels,
    DateTimeOffset ReceivedAt)
{
    public const int ExpectedChannelCount = 40;
    public const int ExpectedSampleCount = 10;
    public const double RawCountsPerPicofarad = 1000.0;

    public double GetCapacitancePf(int channel, int sample) =>
        RawChannels[channel][sample] / RawCountsPerPicofarad;

    public static string GetChannelName(int channel)
    {
        if (channel is < 0 or >= ExpectedChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        int chip = channel / 8;
        int position = channel % 8;
        return position == 0 ? $"REF{chip + 1}" : $"PC{chip * 7 + position}";
    }

    public static bool IsReferenceChannel(int channel) => channel is >= 0 and < ExpectedChannelCount && channel % 8 == 0;
}
