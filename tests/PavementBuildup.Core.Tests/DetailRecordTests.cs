using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class DetailRecordTests
{
    private static DetailRecord Sample() => new()
    {
        Buildup = new Buildup
        {
            Name = "TYPE R — “quoted” ✓",
            Layers = BuildupParser.Parse("250 PQC C32/40 [top H12@200 c40, H16@150 c50 + H10@300]; 0 Geotextile; 150 Type 1 sub-base"),
        },
        Settings = new DetailSettings { ScaleDenominator = 20, WidthMm = 1500, CreateBlock = false, StandardPath = @"\\server\cad\pave.json" },
        UnitsPerMm = 0.001,
        Placement = Enumerable.Range(0, 16).Select(i => (double)i).ToArray(),
        AnchorHandle = "2A3F",
    };

    [Fact]
    public void Round_trips_through_chunks_including_reinforcement_and_unicode()
    {
        var original = Sample();
        var chunks = original.ToChunks();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, DetailRecord.ChunkLength));

        var back = DetailRecord.FromChunks(chunks);
        Assert.Equal(original.Buildup.Name, back.Buildup.Name);
        Assert.Equal(BuildupParser.Format(original.Buildup.Layers), BuildupParser.Format(back.Buildup.Layers));
        Assert.Equal(2, back.Buildup.Layers[0].Reinforcement.Count);
        Assert.Equal(20, back.Settings.ScaleDenominator);
        Assert.False(back.Settings.CreateBlock);
        Assert.Equal(@"\\server\cad\pave.json", back.Settings.StandardPath);
        Assert.Equal(0.001, back.UnitsPerMm);
        Assert.Equal(original.Placement, back.Placement);
        Assert.Equal("2A3F", back.AnchorHandle);
    }

    [Fact]
    public void Chunks_never_split_a_surrogate_pair()
    {
        var r = Sample();
        r.Buildup.Name = new string('x', DetailRecord.ChunkLength - 30) + string.Concat(Enumerable.Repeat("🚧", 50));
        var chunks = r.ToChunks();
        Assert.All(chunks, c => Assert.False(char.IsHighSurrogate(c[^1])));
        Assert.Equal(r.Buildup.Name, DetailRecord.FromChunks(chunks).Buildup.Name);
    }

    [Fact]
    public void Rejects_data_from_a_newer_plugin_and_fills_missing_parts()
    {
        Assert.Throws<InvalidDataException>(() => DetailRecord.FromJson("{\"Version\": 99}"));

        var minimal = DetailRecord.FromJson("{\"Version\": 1, \"Buildup\": {\"Name\": \"X\", \"Layers\": [{\"Description\": \"SMA\", \"ThicknessMm\": 40}]}}");
        Assert.Equal("X", minimal.Buildup.Name);
        Assert.Empty(minimal.Buildup.Layers[0].Reinforcement);
        Assert.NotNull(minimal.Settings);
        Assert.Equal(1, minimal.UnitsPerMm);
    }
}
