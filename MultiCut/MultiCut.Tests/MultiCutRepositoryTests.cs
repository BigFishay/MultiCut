using MultiCut.Data;
using MultiCut.Services;
using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class MultiCutRepositoryTests
{
    [Fact]
    public void SaveMultiCut_ReusesIdenticalLaunchTargets()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        var repository = new MultiCutRepository(workspace.DatabasePath);
        repository.Initialize();

        LaunchTarget sharedTarget = Target("Codex", workspace.PathFor("codex.exe"));
        repository.SaveMultiCut(
            "Development A",
            workspace.PathFor("development-a.json"),
            [sharedTarget]);
        repository.SaveMultiCut(
            "Development B",
            workspace.PathFor("development-b.json"),
            [Target("Codex", sharedTarget.Location)]);

        IReadOnlyList<LaunchTargetListItem> launchTargets = repository.GetCurrentLaunchTargets();

        Assert.Single(launchTargets);
        Assert.Equal(2, launchTargets[0].MultiCutCount);
    }

    [Fact]
    public void SaveMultiCut_RejectsDuplicateLaunchStatesInsideOneMultiCut()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        var repository = new MultiCutRepository(workspace.DatabasePath);
        repository.Initialize();
        string location = workspace.PathFor("tool.exe");

        Assert.Throws<ArgumentException>(() => repository.SaveMultiCut(
            "Duplicate",
            workspace.PathFor("duplicate.json"),
            [
                Target("Tool", location, "--same"),
                Target("Tool Copy", location.ToUpperInvariant(), "--same")
            ]));
    }

    [Fact]
    public void UpdateLaunchTarget_MergesIntoExistingLaunchTargetAndCollapsesParentDuplicates()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        var repository = new MultiCutRepository(workspace.DatabasePath);
        repository.Initialize();
        LaunchTarget codex = Target("Codex", workspace.PathFor("codex.exe"));
        LaunchTarget browser = Target("Browser", workspace.PathFor("browser.exe"));
        MultiCutRecord first = repository.SaveMultiCut(
            "Development A",
            workspace.PathFor("development-a.json"),
            [codex, browser]);
        MultiCutRecord second = repository.SaveMultiCut(
            "Development B",
            workspace.PathFor("development-b.json"),
            [Target("Codex", codex.Location)]);
        LaunchTargetListItem codexRow = repository
            .GetCurrentLaunchTargets()
            .Single(target => target.Location == codex.Location);

        IReadOnlyList<MultiCutRecord> affectedMultiCuts = repository.UpdateLaunchTarget(
            codexRow.Id,
            Target("Browser Merged", browser.Location));

        Assert.Equal([first.Id, second.Id], affectedMultiCuts.Select(multiCut => multiCut.Id).Order().ToArray());

        MultiCutRecord updatedFirst = repository.GetMultiCut(first.Id);
        MultiCutRecord updatedSecond = repository.GetMultiCut(second.Id);
        Assert.Single(updatedFirst.LaunchTargets);
        Assert.Single(updatedSecond.LaunchTargets);
        Assert.Equal("Browser Merged", updatedFirst.LaunchTargets[0].Name);
        Assert.Equal("Browser Merged", updatedSecond.LaunchTargets[0].Name);
        Assert.Single(repository.GetCurrentLaunchTargets());
    }

    [Fact]
    public void UpdateMultiCut_UpdatesByIdEvenWhenJsonPathChanges()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        var repository = new MultiCutRepository(workspace.DatabasePath);
        repository.Initialize();
        MultiCutRecord original = repository.SaveMultiCut(
            "Original",
            workspace.PathFor("original.json"),
            [Target("Tool", workspace.PathFor("tool.exe"))]);

        MultiCutRecord updated = repository.UpdateMultiCut(
            original.Id,
            "Updated",
            [Target("Tool", workspace.PathFor("tool.exe"), "--updated")],
            workspace.PathFor("updated.json"));

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("Updated", updated.Name);
        Assert.EndsWith("updated.json", updated.JsonPath, StringComparison.OrdinalIgnoreCase);
        Assert.Single(repository.GetCurrentMultiCuts());
    }

    [Fact]
    public void AppService_UpdateMultiCut_RewritesGeneratedJsonAndDeletesOldJson()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        var pathService = new MultiCutPathService();
        var service = new MultiCutAppService(
            pathService,
            new MultiCutRepository(workspace.DatabasePath),
            new ShortcutCreationService());
        string oldJsonPath = workspace.PathFor("old.json");
        string newJsonPath = workspace.PathFor("new.json");
        MultiCutRecord saved = service
            .SaveMultiCut("Original", [Target("Tool", workspace.PathFor("tool.exe"))], oldJsonPath)
            .Value!;

        OperationResult<MultiCutRecord> result = service.UpdateMultiCut(
            saved.Id,
            "Updated",
            [Target("Tool", workspace.PathFor("tool.exe"), "--updated")],
            newJsonPath);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.False(File.Exists(oldJsonPath));
        Assert.True(File.Exists(newJsonPath));
        string generatedJson = File.ReadAllText(newJsonPath);
        Assert.Contains("--updated", generatedJson, StringComparison.Ordinal);
    }

    private static LaunchTarget Target(string name, string location, string? arguments = null)
    {
        return new LaunchTarget(name, location, arguments);
    }
}
