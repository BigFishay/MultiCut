using System.Diagnostics;
using MultiCut.Shortcuts;

namespace MultiCut.Tests;

public sealed class MultiExBlackBoxTests
{
    private const string RunEdgeSmokeEnvironmentVariable = "MULTICUT_RUN_EDGE_SMOKE_TEST";
    private const string RunEdgeAndNotepadSmokeEnvironmentVariable = "MULTICUT_RUN_EDGE_NOTEPAD_SMOKE_TEST";
    private static int edgeSmokeStarted;
    private static int edgeAndNotepadSmokeStarted;

    [Fact]
    public void MultiEx_ReturnsFailureWithoutJsonArgument()
    {
        MultiExRunResult result = RunMultiEx();

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void MultiEx_ReturnsFailureForMissingJsonFile()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        MultiExRunResult result = RunMultiEx(workspace.PathFor("missing.json"));

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void MultiEx_ReturnsFailureForEmptyLaunchTargets()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("empty.json");
        File.WriteAllText(jsonPath, """
            {
              "Name": "Empty",
              "LaunchTargets": []
            }
            """);

        MultiExRunResult result = RunMultiEx(jsonPath);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void MultiEx_SkipsInvalidTargetsAndReturnsFailureWhenNoneRemain()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("invalid-targets.json");
        File.WriteAllText(jsonPath, """
            {
              "Name": "Invalid",
              "LaunchTargets": [
                {
                  "Name": "Blank",
                  "Location": " "
                }
              ]
            }
            """);

        MultiExRunResult result = RunMultiEx(jsonPath);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void MultiEx_ReturnsFailureWhenLaunchFails()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("missing-target.json");
        MultiCutShortcutJson.WriteToFile(
            new MultiCutShortcut(
                "Missing Target",
                jsonPath,
                [new LaunchTarget("Missing", workspace.PathFor("does-not-exist.exe"))]),
            jsonPath);

        MultiExRunResult result = RunMultiEx(jsonPath);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void MultiEx_CanLaunchMicrosoftEdgeSmokeTestOnce()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(RunEdgeSmokeEnvironmentVariable),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        string markerPath = GetEdgeSmokeMarkerPath();
        if (File.Exists(markerPath) || Interlocked.Exchange(ref edgeSmokeStarted, 1) == 1)
        {
            return;
        }

        string? edgePath = FindMicrosoftEdgePath();
        Assert.True(edgePath is not null, "Microsoft Edge executable was not found.");

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));

        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("edge-smoke.json");
        MultiCutShortcutJson.WriteToFile(
            new MultiCutShortcut(
                "Edge Smoke",
                jsonPath,
                [new LaunchTarget("Microsoft Edge", edgePath, "--new-window about:blank")]),
            jsonPath);

        MultiExRunResult result = RunMultiEx(jsonPath);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void MultiEx_CanLaunchMicrosoftEdgeAndNotepadFromFileLocationsSmokeTestOnce()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable(RunEdgeAndNotepadSmokeEnvironmentVariable),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        string markerPath = GetEdgeAndNotepadSmokeMarkerPath();
        if (File.Exists(markerPath) || Interlocked.Exchange(ref edgeAndNotepadSmokeStarted, 1) == 1)
        {
            return;
        }

        string? edgePath = FindMicrosoftEdgePath();
        Assert.True(edgePath is not null, "Microsoft Edge executable was not found.");

        string? notepadPath = FindNotepadPath();
        Assert.True(notepadPath is not null, "Notepad executable was not found.");

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));

        using TestWorkspace workspace = TestWorkspace.Create();
        string jsonPath = workspace.PathFor("edge-notepad-smoke.json");
        MultiCutShortcutJson.WriteToFile(
            new MultiCutShortcut(
                "Edge And Notepad Smoke",
                jsonPath,
                [
                    new LaunchTarget("Microsoft Edge", edgePath),
                    new LaunchTarget("Notepad", notepadPath)
                ]),
            jsonPath);

        MultiExRunResult result = RunMultiEx(jsonPath);

        Assert.Equal(0, result.ExitCode);
    }

    private static MultiExRunResult RunMultiEx(string? jsonPath = null)
    {
        string multiExPath = FindMultiExPath();
        var startInfo = new ProcessStartInfo
        {
            FileName = multiExPath,
            Arguments = jsonPath is null ? string.Empty : QuoteArgument(jsonPath),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start MultiEX.");
        if (!process.WaitForExit(milliseconds: 10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("MultiEX did not exit within the test timeout.");
        }

        return new MultiExRunResult(
            process.ExitCode,
            process.StandardOutput.ReadToEnd(),
            process.StandardError.ReadToEnd());
    }

    private static string FindMultiExPath()
    {
        foreach (DirectoryInfo directory in EnumerateCurrentAndParentDirectories())
        {
            foreach (string configuration in new[] { "Debug", "Release" })
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    "MultiEX",
                    "MultiEX",
                    "bin",
                    configuration,
                    "net8.0",
                    "MultiEX.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("MultiEX.exe was not found. Build MultiEX before running this test.");
    }

    private static IEnumerable<DirectoryInfo> EnumerateCurrentAndParentDirectories()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            yield return directory;
            directory = directory.Parent;
        }
    }

    private static string? FindMicrosoftEdgePath()
    {
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string?[] candidates =
        [
            string.IsNullOrWhiteSpace(programFilesX86)
                ? null
                : Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            string.IsNullOrWhiteSpace(programFiles)
                ? null
                : Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
        ];

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string? FindNotepadPath()
    {
        string? systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string? windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string?[] candidates =
        [
            string.IsNullOrWhiteSpace(systemDirectory)
                ? null
                : Path.Combine(systemDirectory, "notepad.exe"),
            string.IsNullOrWhiteSpace(windowsDirectory)
                ? null
                : Path.Combine(windowsDirectory, "notepad.exe")
        ];

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string GetEdgeSmokeMarkerPath()
    {
        return Path.Combine(Path.GetTempPath(), "MultiCut.Tests", "edge-smoke-ran.marker");
    }

    private static string GetEdgeAndNotepadSmokeMarkerPath()
    {
        return Path.Combine(Path.GetTempPath(), "MultiCut.Tests", "edge-notepad-smoke-ran.marker");
    }

    private static string QuoteArgument(string argument)
    {
        return $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record MultiExRunResult(int ExitCode, string StandardOutput, string StandardError);
}
