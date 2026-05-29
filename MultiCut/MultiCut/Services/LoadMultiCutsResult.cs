using MultiCut.Shortcuts;

namespace MultiCut.Services;

/// <summary>
/// Describes the result of loading multiple MultiCut JSON files from disk.
/// </summary>
public sealed class LoadMultiCutsResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoadMultiCutsResult"/> class.
    /// </summary>
    /// <param name="multiCuts">The successfully loaded MultiCuts.</param>
    /// <param name="skippedFiles">The JSON files that could not be loaded.</param>
    public LoadMultiCutsResult(
        IEnumerable<MultiCutShortcut> multiCuts,
        IEnumerable<SkippedMultiCutFile> skippedFiles)
    {
        MultiCuts = multiCuts.ToList();
        SkippedFiles = skippedFiles.ToList();
    }

    /// <summary>
    /// Gets the successfully loaded MultiCuts.
    /// </summary>
    public IReadOnlyList<MultiCutShortcut> MultiCuts { get; }

    /// <summary>
    /// Gets the JSON files that were skipped during loading.
    /// </summary>
    public IReadOnlyList<SkippedMultiCutFile> SkippedFiles { get; }

    /// <summary>
    /// Gets a value indicating whether any JSON files were skipped.
    /// </summary>
    public bool HasSkippedFiles => SkippedFiles.Count > 0;
}

/// <summary>
/// Describes one JSON file that could not be loaded.
/// </summary>
public sealed class SkippedMultiCutFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedMultiCutFile"/> class.
    /// </summary>
    /// <param name="jsonPath">The JSON path that failed to load.</param>
    /// <param name="message">The reason the file was skipped.</param>
    public SkippedMultiCutFile(string jsonPath, string message)
    {
        JsonPath = jsonPath;
        Message = message;
    }

    /// <summary>
    /// Gets the JSON path that failed to load.
    /// </summary>
    public string JsonPath { get; }

    /// <summary>
    /// Gets the reason the file was skipped.
    /// </summary>
    public string Message { get; }
}
