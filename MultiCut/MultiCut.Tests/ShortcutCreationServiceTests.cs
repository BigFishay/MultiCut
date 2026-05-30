using MultiCut.Services;
using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class ShortcutCreationServiceTests
{
    [Fact]
    public void CreateShortcut_RejectsNonShortcutPath()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("shortcut.json");
        File.WriteAllText(jsonPath, "{}");
        string multiExPath = workspace.PathFor("MultiEX.exe");
        File.WriteAllText(multiExPath, string.Empty);
        var service = new ShortcutCreationService();

        Assert.Throws<ArgumentException>(() => service.CreateShortcut(
            new MultiCutShortcut("Development", jsonPath, []),
            workspace.PathFor("shortcut.txt"),
            multiExPath));
    }

    [Fact]
    public void CreateShortcut_RejectsRelativeJsonPath()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string multiExPath = workspace.PathFor("MultiEX.exe");
        File.WriteAllText(multiExPath, string.Empty);
        var service = new ShortcutCreationService();

        Assert.Throws<ArgumentException>(() => service.CreateShortcut(
            new MultiCutShortcut("Development", "relative.json", []),
            workspace.PathFor("shortcut.lnk"),
            multiExPath));
    }

    [Fact]
    public void CreateShortcut_RejectsMissingJsonFile()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string multiExPath = workspace.PathFor("MultiEX.exe");
        File.WriteAllText(multiExPath, string.Empty);
        var service = new ShortcutCreationService();

        Assert.Throws<FileNotFoundException>(() => service.CreateShortcut(
            new MultiCutShortcut("Development", workspace.PathFor("missing.json"), []),
            workspace.PathFor("shortcut.lnk"),
            multiExPath));
    }

    [Fact]
    public void CreateShortcut_RejectsNonExecutableMultiExPath()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("shortcut.json");
        File.WriteAllText(jsonPath, "{}");
        var service = new ShortcutCreationService();

        Assert.Throws<ArgumentException>(() => service.CreateShortcut(
            new MultiCutShortcut("Development", jsonPath, []),
            workspace.PathFor("shortcut.lnk"),
            workspace.PathFor("MultiEX.dll")));
    }

    [Fact]
    public void CreateShortcut_CreatesShortcutWhenInputsAreValid()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("shortcut.json");
        File.WriteAllText(jsonPath, "{}");
        string multiExPath = workspace.PathFor("MultiEX.exe");
        File.WriteAllText(multiExPath, string.Empty);
        string shortcutPath = workspace.PathFor("shortcut.lnk");
        var service = new ShortcutCreationService();

        service.CreateShortcut(
            new MultiCutShortcut("Development", jsonPath, []),
            shortcutPath,
            multiExPath);

        Assert.True(File.Exists(shortcutPath));
    }
}
