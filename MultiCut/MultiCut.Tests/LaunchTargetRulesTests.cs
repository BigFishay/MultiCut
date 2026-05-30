using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class LaunchTargetRulesTests
{
    [Fact]
    public void NormalizeLaunchTarget_TrimsValuesAndUsesLocationAsFallbackName()
    {
        var launchTarget = new LaunchTarget("  ", "  C:\\Tools\\tool.exe  ", "  --open  ");

        LaunchTarget normalized = LaunchTargetRules.NormalizeLaunchTarget(launchTarget);

        Assert.Equal("C:\\Tools\\tool.exe", normalized.Name);
        Assert.Equal("C:\\Tools\\tool.exe", normalized.Location);
        Assert.Equal("--open", normalized.Arguments);
    }

    [Fact]
    public void NormalizeLaunchTargets_AllowsSameLocationWithDifferentArguments()
    {
        List<LaunchTarget> normalized = LaunchTargetRules.NormalizeLaunchTargets(
        [
            new("Tool", "C:\\Tools\\tool.exe", "--first"),
            new("Tool Again", "c:\\tools\\TOOL.exe", "--second")
        ]);

        Assert.Equal(2, normalized.Count);
    }

    [Fact]
    public void NormalizeLaunchTargets_TreatsArgumentsAsCaseSensitive()
    {
        List<LaunchTarget> normalized = LaunchTargetRules.NormalizeLaunchTargets(
        [
            new("Tool", "C:\\Tools\\tool.exe", "--open"),
            new("Tool Again", "c:\\tools\\TOOL.exe", "--OPEN")
        ]);

        Assert.Equal(2, normalized.Count);
    }

    [Fact]
    public void NormalizeLaunchTargets_RejectsBlankLocation()
    {
        Assert.Throws<ArgumentException>(() => LaunchTargetRules.NormalizeLaunchTargets(
        [
            new LaunchTarget("Blank", " ")
        ]));
    }
}
