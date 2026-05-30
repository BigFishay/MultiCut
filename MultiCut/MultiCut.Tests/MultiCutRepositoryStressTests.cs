using MultiCut.Data;
using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class MultiCutRepositoryStressTests
{
    [Fact]
    [Trait("Category", "Stress")]
    public void LargeDataset_ReusesLaunchTargetsAndMergesPredictably()
    {
        const int multiCutCount = 100;
        const int sharedTargetCount = 5;
        const int uniqueTargetsPerMultiCut = 10;
        using TestWorkspace workspace = TestWorkspace.Create();
        var repository = new MultiCutRepository(workspace.DatabasePath);
        repository.Initialize();
        LaunchTarget[] sharedTargets = Enumerable
            .Range(0, sharedTargetCount)
            .Select(index => Target($"Shared {index}", workspace.PathFor($"shared-{index}.exe")))
            .ToArray();

        for (int multiCutIndex = 0; multiCutIndex < multiCutCount; multiCutIndex++)
        {
            var launchTargets = new List<LaunchTarget>(sharedTargets);
            launchTargets.AddRange(Enumerable
                .Range(0, uniqueTargetsPerMultiCut)
                .Select(targetIndex => Target(
                    $"Unique {multiCutIndex}-{targetIndex}",
                    workspace.PathFor($"unique-{multiCutIndex}-{targetIndex}.exe"),
                    $"--slot {targetIndex}")));

            repository.SaveMultiCut(
                $"MultiCut {multiCutIndex:000}",
                workspace.PathFor($"multicut-{multiCutIndex:000}.json"),
                launchTargets);
        }

        IReadOnlyList<MultiCutListItem> multiCuts = repository.GetCurrentMultiCuts();
        IReadOnlyList<LaunchTargetListItem> launchTargetsBeforeMerge = repository.GetCurrentLaunchTargets();
        LaunchTargetListItem sharedZero = launchTargetsBeforeMerge.Single(target => target.Name == "Shared 0");

        IReadOnlyList<MultiCutRecord> affectedMultiCuts = repository.UpdateLaunchTarget(
            sharedZero.Id,
            Target("Shared 1 Merged", sharedTargets[1].Location));

        IReadOnlyList<LaunchTargetListItem> launchTargetsAfterMerge = repository.GetCurrentLaunchTargets();
        MultiCutRecord sampleMultiCut = repository.GetMultiCut(multiCuts[multiCuts.Count / 2].Id);

        Assert.Equal(multiCutCount, multiCuts.Count);
        Assert.Equal(sharedTargetCount + (multiCutCount * uniqueTargetsPerMultiCut), launchTargetsBeforeMerge.Count);
        Assert.Equal(multiCutCount, affectedMultiCuts.Count);
        Assert.Equal((sharedTargetCount - 1) + (multiCutCount * uniqueTargetsPerMultiCut), launchTargetsAfterMerge.Count);
        Assert.Equal((sharedTargetCount - 1) + uniqueTargetsPerMultiCut, sampleMultiCut.LaunchTargets.Count);
        Assert.DoesNotContain(sampleMultiCut.LaunchTargets, target => target.Name == "Shared 0");
        Assert.Contains(sampleMultiCut.LaunchTargets, target => target.Name == "Shared 1 Merged");
    }

    private static LaunchTarget Target(string name, string location, string? arguments = null)
    {
        return new LaunchTarget(name, location, arguments);
    }
}
