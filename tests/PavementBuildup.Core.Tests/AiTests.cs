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
    public void Paid_ai_is_off_by_default_and_settings_round_trip()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new PresetStore(Path.Combine(dir.FullName, "presets.json"));
            var file = store.Load();
            Assert.False(file.Ai.Enabled);                 // free route by default: no key needed
            Assert.Equal("claude-opus-5-5", file.Ai.Model);

            file.Ai.Enabled = true;
            store.Save(file);
            Assert.True(store.Load().Ai.Enabled);

            File.WriteAllText(store.Path, "{ \"Presets\": [] }"); // older file without an Ai section
            Assert.NotNull(store.Load().Ai);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Chat_text_is_prompt_then_build_up()
    {
        var text = AiPrompt.ForChat("RULES\nConvert this:", "  60mm pavers on 30 mortar  ");
        Assert.StartsWith("RULES", text);
        Assert.EndsWith("Convert this:" + Environment.NewLine + Environment.NewLine + "60mm pavers on 30 mortar", text);
    }

    [Theory]
    [InlineData("```\n[TYPE A] 60mm THICK PAVERS, SUBGRADE\n```", "[TYPE A] 60mm THICK PAVERS, SUBGRADE")]
    [InlineData("Output: 60mm THICK PAVERS, SUBGRADE", "60mm THICK PAVERS, SUBGRADE")]
    [InlineData("\u201C60mm THICK PAVERS, SUBGRADE\u201D\r\n", "60mm THICK PAVERS, SUBGRADE")]
    public void Cleans_replies_copied_from_a_chat(string reply, string expected) =>
        Assert.Equal(expected, AiPrompt.CleanReply(reply));
}
