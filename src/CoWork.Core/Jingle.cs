namespace CoWork.Core;

/// <summary>A tiny 8-bit jingle made on the fly (a WAV in memory), so the Easter egg needs no sound file.</summary>
public static class Jingle
{
    /// <summary>Notes as (frequency Hz, seconds); 0 Hz = a rest.</summary>
    public static readonly (double Hz, double Seconds)[] Tune =
    {
        (523.25, .09), (659.25, .09), (783.99, .09), (1046.5, .16), (0, .05), (783.99, .08), (1046.5, .32),
    };

    public static byte[] Wav(int sampleRate = 22050, double volume = 0.18)
    {
        var samples = new List<short>();
        foreach (var (hz, secs) in Tune)
        {
            int n = (int)(sampleRate * secs);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sampleRate;
                double env = Math.Min(1, (n - i) / (sampleRate * 0.02)); // short fade so notes don't click
                double square = hz == 0 ? 0 : (Math.Sin(2 * Math.PI * hz * t) >= 0 ? 1 : -1);
                samples.Add((short)(square * env * volume * short.MaxValue));
            }
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Count * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        foreach (short sm in samples) w.Write(sm);
        w.Flush();
        return ms.ToArray();
    }
}
