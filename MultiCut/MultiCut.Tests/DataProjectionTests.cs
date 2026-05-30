using MultiCut.Data;
using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class DataProjectionTests
{
    [Fact]
    public void MultiCutListItem_ExposesConstructorValues()
    {
        var item = new MultiCutListItem(
            1,
            "Development",
            "C:\\Data\\development.json",
            "C:\\Users\\User\\Desktop\\Development.lnk",
            "C:\\Icons\\dev.ico",
            2,
            "created",
            "updated",
            3);

        Assert.Equal(1, item.Id);
        Assert.Equal("Development", item.Name);
        Assert.Equal("C:\\Data\\development.json", item.JsonPath);
        Assert.Equal("C:\\Users\\User\\Desktop\\Development.lnk", item.ShortcutPath);
        Assert.Equal("C:\\Icons\\dev.ico", item.IconPath);
        Assert.Equal(2, item.IconIndex);
        Assert.Equal("created", item.CreatedAt);
        Assert.Equal("updated", item.UpdatedAt);
        Assert.Equal(3, item.LaunchTargetCount);
    }

    [Fact]
    public void LaunchTargetListItem_ExposesConstructorValues()
    {
        var item = new LaunchTargetListItem(
            7,
            "Editor",
            "C:\\Tools\\editor.exe",
            "--workspace",
            "created",
            "updated",
            5);

        Assert.Equal(7, item.Id);
        Assert.Equal("Editor", item.Name);
        Assert.Equal("C:\\Tools\\editor.exe", item.Location);
        Assert.Equal("--workspace", item.Arguments);
        Assert.Equal("created", item.CreatedAt);
        Assert.Equal("updated", item.UpdatedAt);
        Assert.Equal(5, item.MultiCutCount);
    }

    [Fact]
    public void MultiCutRecord_ToShortcutContract_ConvertsToSharedJsonContract()
    {
        var launchTarget = new LaunchTarget("Editor", "C:\\Tools\\editor.exe");
        var record = new MultiCutRecord(
            3,
            "Development",
            "C:\\Data\\development.json",
            null,
            null,
            null,
            [launchTarget]);

        MultiCutShortcut shortcut = record.ToShortcutContract();

        Assert.Equal("Development", shortcut.Name);
        Assert.Equal("C:\\Data\\development.json", shortcut.JsonPath);
        Assert.Equal(launchTarget.Location, Assert.Single(shortcut.LaunchTargets).Location);
    }
}
