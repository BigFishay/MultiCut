using MultiCut.Services;

namespace MultiCut.Tests;

public sealed class MultiCutPathServiceTests
{
    [Fact]
    public void GetDefaultJsonPath_SanitizesFileName()
    {
        var pathService = new MultiCutPathService();

        string jsonPath = pathService.GetDefaultJsonPath("Dev:Build/Debug*");
        string fileName = Path.GetFileName(jsonPath);

        Assert.EndsWith(".json", jsonPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(':', fileName);
        Assert.DoesNotContain('/', fileName);
        Assert.DoesNotContain('*', fileName);
    }

    [Fact]
    public void NormalizeJsonPath_RejectsNonJsonExtension()
    {
        var pathService = new MultiCutPathService();

        Assert.Throws<ArgumentException>(() => pathService.NormalizeJsonPath("shortcut.txt"));
    }

    [Fact]
    public void NormalizeShortcutPath_RejectsNonShortcutExtension()
    {
        var pathService = new MultiCutPathService();

        Assert.Throws<ArgumentException>(() => pathService.NormalizeShortcutPath("shortcut.json"));
    }

    [Fact]
    public void ResolveMultiExPath_UsesConfiguredExecutable()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.CreateEmptyFile("MultiEX.exe");
        var pathService = new MultiCutPathService();

        string resolvedPath = pathService.ResolveMultiExPath(workspace.PathFor("MultiEX.exe"));

        Assert.Equal(workspace.PathFor("MultiEX.exe"), resolvedPath, ignoreCase: true);
    }
}
