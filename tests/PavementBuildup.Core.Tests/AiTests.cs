using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class AiTests
{
    [Fact]
    public void Built_in_prompt_is_the_shipped_prompt_file()
    {
        Assert.Contains("square brackets", AiPrompt.BuiltIn);
        Assert.Contains("[PAVEMENT TYPE A]", AiPrompt.BuiltIn);
    }

    [Fact]
    public void Company_prompt_file_overrides_the_built_in_one_when_it_exists()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "company instructions");
            Assert.Equal("company instructions", AiPrompt.Load(path));
            Assert.Equal(AiPrompt.BuiltIn, AiPrompt.Load(path + ".missing"));
            Assert.Equal(AiPrompt.BuiltIn, AiPrompt.Load(""));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_ai_component_is_reported_not_thrown_on_lookup()
    {
        Assert.Null(AiConverter.FindFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    [Fact]
    public void Ai_settings_default_on_with_opus_and_round_trip()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new PresetStore(Path.Combine(dir.FullName, "presets.json"));
            var file = store.Load();
            Assert.True(file.Ai.Enabled);
            Assert.Equal("claude-opus-5-5", file.Ai.Model);

            file.Ai.Enabled = false;
            store.Save(file);
            Assert.False(store.Load().Ai.Enabled);

            File.WriteAllText(store.Path, "{ \"Presets\": [] }"); // older file without an Ai section
            Assert.NotNull(store.Load().Ai);
        }
        finally { dir.Delete(true); }
    }
}
