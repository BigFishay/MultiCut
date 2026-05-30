using MultiCut.Data;
using MultiCut.Services;
using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class MultiCutAppServiceTests
{
    [Fact]
    public async Task InitializeAsync_PreparesDatabase()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);

        OperationResult result = await service.InitializeAsync();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.True(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public async Task AsyncMethods_SaveLoadAndDeleteMultiCut()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);

        OperationResult<MultiCutRecord> saveResult = await service.SaveMultiCutAsync(
            "Development",
            [new LaunchTarget("Editor", workspace.PathFor("editor.exe"))],
            workspace.PathFor("development.json"));
        OperationResult<IReadOnlyList<MultiCutListItem>> listResult = await service.GetCurrentMultiCutsAsync();
        OperationResult<MultiCutRecord> loadResult = await service.GetMultiCutAsync(saveResult.Value!.Id);
        OperationResult deleteResult = await service.DeleteMultiCutAsync(saveResult.Value!.Id);

        Assert.True(saveResult.Success, string.Join(Environment.NewLine, saveResult.Errors));
        Assert.True(listResult.Success, string.Join(Environment.NewLine, listResult.Errors));
        Assert.True(loadResult.Success, string.Join(Environment.NewLine, loadResult.Errors));
        Assert.True(deleteResult.Success, string.Join(Environment.NewLine, deleteResult.Errors));
        Assert.Single(listResult.Value!);
        Assert.Equal("Development", loadResult.Value!.Name);
        Assert.False(File.Exists(workspace.PathFor("development.json")));
    }

    [Fact]
    public async Task SaveMultiCutAsync_ReturnsFailureWhenLaunchTargetsCannotBeEnumerated()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);

        OperationResult<MultiCutRecord> result = await service.SaveMultiCutAsync(
            "Development",
            null!,
            workspace.PathFor("development.json"));

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task RegenerateJsonAsync_RecreatesMissingJsonFile()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);
        string jsonPath = workspace.PathFor("development.json");
        MultiCutRecord saved = (await service.SaveMultiCutAsync(
            "Development",
            [new LaunchTarget("Editor", workspace.PathFor("editor.exe"))],
            jsonPath)).Value!;
        File.Delete(jsonPath);

        OperationResult<MultiCutRecord> result = await service.RegenerateJsonAsync(saved.Id);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.True(File.Exists(jsonPath));
    }

    [Fact]
    public async Task CreateShortcutAsync_ReturnsFailureWhenMultiExCannotBeResolved()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);
        MultiCutRecord saved = (await service.SaveMultiCutAsync(
            "Development",
            [new LaunchTarget("Editor", workspace.PathFor("editor.exe"))],
            workspace.PathFor("development.json"))).Value!;

        OperationResult<string> result = await service.CreateShortcutAsync(saved.Id, workspace.PathFor("development.lnk"));

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task SaveMultiCutWithShortcutAsync_CreatesShortcutAndStoresMetadata()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.CreateEmptyFile("MultiEX.exe");
        MultiCutAppService service = CreateService(workspace);
        string shortcutPath = workspace.PathFor("development.lnk");

        OperationResult<MultiCutRecord> result = await service.SaveMultiCutWithShortcutAsync(
            "Development",
            [new LaunchTarget("Editor", workspace.PathFor("editor.exe"))],
            workspace.PathFor("development.json"),
            shortcutPath,
            workspace.PathFor("MultiEX.exe"));

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(shortcutPath, result.Value!.ShortcutPath, ignoreCase: true);
        Assert.True(File.Exists(shortcutPath));
    }

    [Fact]
    public async Task DeleteMultiCutAsync_ReturnsSuccessWhenRowDoesNotExist()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);

        OperationResult result = await service.DeleteMultiCutAsync(123456);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("No matching MultiCut was found.", result.Message);
    }

    [Fact]
    public async Task UpdateLaunchTargetAsync_RegeneratesAffectedParentJson()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        MultiCutAppService service = CreateService(workspace);
        string sharedLocation = workspace.PathFor("shared.exe");
        OperationResult<MultiCutRecord> saveResult = await service.SaveMultiCutAsync(
            "Development",
            [new LaunchTarget("Shared", sharedLocation)],
            workspace.PathFor("development.json"));
        OperationResult<IReadOnlyList<LaunchTargetListItem>> targetsResult =
            await service.GetCurrentLaunchTargetsAsync();
        long launchTargetId = Assert.Single(targetsResult.Value!).Id;

        OperationResult<IReadOnlyList<MultiCutRecord>> updateResult =
            await service.UpdateLaunchTargetAsync(
                launchTargetId,
                new LaunchTarget("Shared Updated", sharedLocation, "--updated"));

        Assert.True(updateResult.Success, string.Join(Environment.NewLine, updateResult.Errors));
        Assert.Equal(saveResult.Value!.Id, Assert.Single(updateResult.Value!).Id);
        string generatedJson = File.ReadAllText(workspace.PathFor("development.json"));
        Assert.Contains("--updated", generatedJson, StringComparison.Ordinal);
    }

    private static MultiCutAppService CreateService(TestWorkspace workspace)
    {
        return new MultiCutAppService(
            new MultiCutPathService(),
            new MultiCutRepository(workspace.DatabasePath),
            new ShortcutCreationService());
    }
}
