using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class PresetStoreTests
{
    [Fact]
    public void Seeds_examples_then_round_trips_through_json()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new PresetStore(Path.Combine(dir.FullName, "sub", "presets.json"));
            var file = store.Load();
            Assert.NotEmpty(file.Presets);

            var custom = new Buildup { Name = "Car park", Layers = BuildupParser.Parse("30 AC 6 surf; 120 Type 1") };
            PresetStore.Upsert(file, custom);
            file.Settings.ScaleDenominator = 20;
            store.Save(file);

            var loaded = store.Load();
            var found = PresetStore.Find(loaded, "CAR PARK");
            Assert.NotNull(found);
            Assert.Equal(150, found!.TotalThicknessMm);
            Assert.Equal(20, loaded.Settings.ScaleDenominator);

            // Upsert replaces by name rather than duplicating.
            PresetStore.Upsert(loaded, new Buildup { Name = "car park", Layers = BuildupParser.Parse("50 HRA") });
            Assert.Single(loaded.Presets, p => p.Name.Equals("car park", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Example_presets_are_valid_details()
    {
        foreach (var b in PresetStore.Examples())
            Assert.NotEmpty(DetailLayout.Build(b, new DetailSettings(), 1).Bands);
    }
}
