namespace EmgCollector.Acquisition;

public sealed class PacketStatistics
{
    private readonly object _sync = new();
    private ulong? _lastCounter;
    private long _receivedPackets;
    private long _invalidPackets;
    private long _lostPackets;
    private long _outOfOrderPackets;
    private long _receivedSamples;
    private string _lastError = string.Empty;

    public void OnValid(ushort counter, int samples)
    {
        OnValidCore(counter, samples, 1UL << 16);
    }

    public void OnValid(uint counter, int samples)
    {
        OnValidCore(counter, samples, 1UL << 32);
    }

    private void OnValidCore(ulong counter, int samples, ulong modulus)
    {
        lock (_sync)
        {
            bool updateLastCounter = true;
            if (_lastCounter.HasValue)
            {
                ulong expected = (_lastCounter.Value + 1) % modulus;
                ulong forwardGap = (counter + modulus - expected) % modulus;
                if (forwardGap > 0 && forwardGap < modulus / 2)
                {
                    _lostPackets += checked((long)forwardGap);
                }
                else if (forwardGap >= modulus / 2)
                {
                    _outOfOrderPackets++;
                    updateLastCounter = false;
                }
            }

            if (updateLastCounter)
            {
                _lastCounter = counter;
            }
            _receivedPackets++;
            _receivedSamples += samples;
        }
    }

    public void OnInvalid(string error)
    {
        lock (_sync)
        {
            _invalidPackets++;
            _lastError = error;
        }
    }

    public StatisticsSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new StatisticsSnapshot(
                _receivedPackets,
                _invalidPackets,
                _lostPackets,
                _outOfOrderPackets,
                _receivedSamples,
                _lastCounter,
                _lastError);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _lastCounter = null;
            _receivedPackets = 0;
            _invalidPackets = 0;
            _lostPackets = 0;
            _outOfOrderPackets = 0;
            _receivedSamples = 0;
            _lastError = string.Empty;
        }
    }
}

public sealed record StatisticsSnapshot(
    long ReceivedPackets,
    long InvalidPackets,
    long LostPackets,
    long OutOfOrderPackets,
    long ReceivedSamples,
    ulong? LastCounter,
    string LastError);
