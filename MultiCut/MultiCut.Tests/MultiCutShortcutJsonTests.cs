using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class MultiCutShortcutJsonTests
{
    [Fact]
    public void Serialize_OmitsRuntimeJsonPathFromLaunchContract()
    {
        var multiCut = new MultiCutShortcut(
            "Development",
            "C:\\Users\\User\\AppData\\Local\\MultiCut\\Shortcuts\\Development.json",
            [new LaunchTarget("Editor", "C:\\Tools\\editor.exe")]);

        string json = MultiCutShortcutJson.Serialize(multiCut);

        Assert.Contains("\"Name\": \"Development\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Location\": \"C:\\\\Tools\\\\editor.exe\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonPath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Arguments", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_ReadsPropertyNamesCaseInsensitively()
    {
        const string json = """
            {
              "name": "Development",
              "launchTargets": [
                {
                  "name": "Editor",
                  "location": "C:\\Tools\\editor.exe",
                  "arguments": "--workspace"
                }
              ]
            }
            """;

        MultiCutShortcut multiCut = MultiCutShortcutJson.Deserialize(json);

        Assert.Equal("Development", multiCut.Name);
        LaunchTarget launchTarget = Assert.Single(multiCut.LaunchTargets);
        Assert.Equal("Editor", launchTarget.Name);
        Assert.Equal("C:\\Tools\\editor.exe", launchTarget.Location);
        Assert.Equal("--workspace", launchTarget.Arguments);
    }

    [Fact]
    public void WriteToFile_RespectsOverwriteFlag()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("shortcut.json");
        var multiCut = new MultiCutShortcut(
            "Development",
            jsonPath,
            [new LaunchTarget("Editor", "C:\\Tools\\editor.exe")]);

        MultiCutShortcutJson.WriteToFile(multiCut, jsonPath, overwrite: false);

        Assert.True(File.Exists(jsonPath));
        Assert.Throws<InvalidOperationException>(() => MultiCutShortcutJson.WriteToFile(
            multiCut,
            jsonPath,
            overwrite: false));
    }
}
