using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiCut.Shortcuts;

namespace MultiCut.Services;

/// <summary>
/// Creates, serializes, loads, and indexes MultiCut shortcut groups for the UI layer.
/// </summary>
public sealed class MultiCutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly Dictionary<string, MultiCutShortcut> multiCutsByJsonPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the default directory for MultiCut JSON files.
    /// </summary>
    public string DefaultShortcutDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MultiCut",
        "Shortcuts");

    /// <summary>
    /// Gets the currently loaded MultiCuts as a read-only snapshot.
    /// </summary>
    public IReadOnlyList<MultiCutShortcut> LoadedMultiCuts => multiCutsByJsonPath.Values.ToList();

    /// <summary>
    /// Gets the unique launch targets currently referenced by loaded MultiCuts.
    /// </summary>
    public IReadOnlyList<LaunchTarget> LoadedLaunchTargets
    {
        get
        {
            var launchTargetsByState = new Dictionary<LaunchState, LaunchTarget>(LaunchStateComparer.Instance);
            foreach (MultiCutShortcut multiCut in multiCutsByJsonPath.Values)
            {
                foreach (LaunchTarget launchTarget in NormalizeLaunchTargets(multiCut.LaunchTargets))
                {
                    var launchState = new LaunchState(launchTarget.Location, launchTarget.Arguments ?? string.Empty);
                    launchTargetsByState.TryAdd(launchState, launchTarget);
                }
            }

            return launchTargetsByState.Values.ToList();
        }
    }

    /// <summary>
    /// Gets the loaded MultiCuts keyed by absolute JSON path.
    /// </summary>
    public IReadOnlyDictionary<string, MultiCutShortcut> MultiCutsByJsonPath =>
        new ReadOnlyDictionary<string, MultiCutShortcut>(multiCutsByJsonPath);

    /// <summary>
    /// Creates a new MultiCut, writes its full JSON contract, and indexes it by absolute JSON path.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="jsonPath">The intended JSON file path. Relative paths are normalized to absolute paths.</param>
    /// <param name="launchTargets">The launch targets that MultiEX should open.</param>
    /// <param name="overwrite">Whether an existing JSON path can be replaced.</param>
    /// <returns>The created and indexed MultiCut.</returns>
    /// <exception cref="ArgumentException">Thrown when required input is blank or invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the path already exists and overwrite is not enabled.</exception>
    public MultiCutShortcut Create(
        string name,
        string jsonPath,
        IEnumerable<LaunchTarget> launchTargets,
        bool overwrite = false)
    {
        string absoluteJsonPath = NormalizeJsonPath(jsonPath);
        var multiCut = new MultiCutShortcut(
            NormalizeName(name),
            absoluteJsonPath,
            NormalizeLaunchTargets(launchTargets));

        if (!overwrite && multiCutsByJsonPath.ContainsKey(absoluteJsonPath))
        {
            throw new InvalidOperationException($"A MultiCut already exists for '{absoluteJsonPath}'.");
        }

        WriteMultiCut(multiCut, overwrite);
        multiCutsByJsonPath[absoluteJsonPath] = multiCut;
        return multiCut;
    }

    /// <summary>
    /// Writes an existing MultiCut to disk and updates the in-memory index.
    /// </summary>
    /// <param name="multiCut">The MultiCut to save.</param>
    /// <param name="overwrite">Whether an existing JSON file can be replaced.</param>
    /// <returns>The normalized and indexed MultiCut.</returns>
    public MultiCutShortcut Save(MultiCutShortcut multiCut, bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(multiCut);

        string absoluteJsonPath = NormalizeJsonPath(multiCut.JsonPath);
        MultiCutShortcut normalizedMultiCut = NormalizeMultiCut(multiCut, absoluteJsonPath);

        WriteMultiCut(normalizedMultiCut, overwrite);
        multiCutsByJsonPath[absoluteJsonPath] = normalizedMultiCut;
        return normalizedMultiCut;
    }

    /// <summary>
    /// Loads one MultiCut from a JSON path and indexes it by that absolute path.
    /// </summary>
    /// <param name="jsonPath">The JSON file path to load.</param>
    /// <returns>The loaded and indexed MultiCut.</returns>
    public MultiCutShortcut LoadFromJsonPath(string jsonPath)
    {
        string absoluteJsonPath = NormalizeJsonPath(jsonPath);
        string json = File.ReadAllText(absoluteJsonPath);

        MultiCutShortcut? multiCut = JsonSerializer.Deserialize<MultiCutShortcut>(json, JsonOptions);
        if (multiCut is null)
        {
            throw new InvalidDataException("MultiCut JSON cannot be null.");
        }

        // The file path is authoritative. If the JSON was moved or copied, keep the
        // in-memory object aligned with the actual absolute path that was loaded.
        MultiCutShortcut normalizedMultiCut = NormalizeMultiCut(multiCut, absoluteJsonPath);
        multiCutsByJsonPath[absoluteJsonPath] = normalizedMultiCut;
        return normalizedMultiCut;
    }

    /// <summary>
    /// Loads all valid MultiCut JSON files from a directory.
    /// </summary>
    /// <param name="directoryPath">The directory to scan for MultiCut JSON files.</param>
    /// <returns>The valid MultiCuts loaded from the directory.</returns>
    public IReadOnlyList<MultiCutShortcut> LoadFromDirectory(string directoryPath)
    {
        return LoadFromDirectoryWithDiagnostics(directoryPath).MultiCuts;
    }

    /// <summary>
    /// Loads all valid MultiCut JSON files from a directory and reports skipped files.
    /// </summary>
    /// <param name="directoryPath">The directory to scan for MultiCut JSON files.</param>
    /// <returns>The loaded MultiCuts and skipped-file diagnostics.</returns>
    public LoadMultiCutsResult LoadFromDirectoryWithDiagnostics(string directoryPath)
    {
        string absoluteDirectoryPath = NormalizeDirectoryPath(directoryPath);
        Directory.CreateDirectory(absoluteDirectoryPath);

        var loadedMultiCuts = new List<MultiCutShortcut>();
        var skippedFiles = new List<SkippedMultiCutFile>();
        foreach (string jsonPath in Directory.EnumerateFiles(absoluteDirectoryPath, "*.json"))
        {
            try
            {
                loadedMultiCuts.Add(LoadFromJsonPath(jsonPath));
            }
            catch (Exception exception)
            {
                // Startup loading should be resilient: one damaged JSON file should not
                // prevent the rest of the user's MultiCuts from appearing in the UI.
                skippedFiles.Add(new SkippedMultiCutFile(jsonPath, exception.Message));
            }
        }

        return new LoadMultiCutsResult(loadedMultiCuts, skippedFiles);
    }

    /// <summary>
    /// Loads all valid MultiCut JSON files from the default shortcut directory.
    /// </summary>
    /// <returns>The valid MultiCuts loaded from the default shortcut directory.</returns>
    public IReadOnlyList<MultiCutShortcut> LoadFromDefaultDirectory()
    {
        return LoadFromDirectory(DefaultShortcutDirectory);
    }

    /// <summary>
    /// Attempts to find a MultiCut by JSON path.
    /// </summary>
    /// <param name="jsonPath">The JSON path to look up.</param>
    /// <param name="multiCut">The matching MultiCut, when found.</param>
    /// <returns><see langword="true"/> when a matching MultiCut is indexed; otherwise, <see langword="false"/>.</returns>
    public bool TryGetByJsonPath(string jsonPath, out MultiCutShortcut? multiCut)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            multiCut = null;
            return false;
        }

        return multiCutsByJsonPath.TryGetValue(NormalizeJsonPath(jsonPath), out multiCut);
    }

    /// <summary>
    /// Removes a MultiCut from the in-memory index and optionally deletes its JSON file.
    /// </summary>
    /// <param name="jsonPath">The JSON path to remove.</param>
    /// <param name="deleteJsonFile">Whether to delete the JSON file from disk.</param>
    /// <returns><see langword="true"/> when an indexed entry or JSON file was removed.</returns>
    public bool Delete(string jsonPath, bool deleteJsonFile = true)
    {
        string absoluteJsonPath = NormalizeJsonPath(jsonPath);
        bool removedFromIndex = multiCutsByJsonPath.Remove(absoluteJsonPath);
        bool deletedFile = false;

        if (deleteJsonFile && File.Exists(absoluteJsonPath))
        {
            File.Delete(absoluteJsonPath);
            deletedFile = true;
        }

        return removedFromIndex || deletedFile;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("MultiCut name cannot be blank.", nameof(name));
        }

        return name.Trim();
    }

    private static string NormalizeJsonPath(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            throw new ArgumentException("JSON path cannot be blank.", nameof(jsonPath));
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(jsonPath.Trim().Trim('"')));
    }

    private static string NormalizeDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path cannot be blank.", nameof(directoryPath));
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(directoryPath.Trim().Trim('"')));
    }

    private static MultiCutShortcut NormalizeMultiCut(MultiCutShortcut multiCut, string absoluteJsonPath)
    {
        string fallbackName = Path.GetFileNameWithoutExtension(absoluteJsonPath);
        string normalizedName = string.IsNullOrWhiteSpace(multiCut.Name)
            ? fallbackName
            : multiCut.Name.Trim();

        return new MultiCutShortcut(
            normalizedName,
            absoluteJsonPath,
            NormalizeLaunchTargets(multiCut.LaunchTargets));
    }

    private static List<LaunchTarget> NormalizeLaunchTargets(IEnumerable<LaunchTarget> launchTargets)
    {
        ArgumentNullException.ThrowIfNull(launchTargets);

        var normalizedLaunchTargets = new List<LaunchTarget>();
        var seenLaunchStates = new HashSet<LaunchState>(LaunchStateComparer.Instance);
        foreach (LaunchTarget? launchTarget in launchTargets)
        {
            if (launchTarget is null)
            {
                throw new ArgumentException("Launch target cannot be null.", nameof(launchTargets));
            }

            if (string.IsNullOrWhiteSpace(launchTarget.Location))
            {
                throw new ArgumentException("Launch target location cannot be blank.", nameof(launchTargets));
            }

            string normalizedLocation = launchTarget.Location.Trim();
            string normalizedName = string.IsNullOrWhiteSpace(launchTarget.Name)
                ? normalizedLocation
                : launchTarget.Name.Trim();
            string? normalizedArguments = string.IsNullOrWhiteSpace(launchTarget.Arguments)
                ? null
                : launchTarget.Arguments.Trim();

            // A MultiCut is an ordered set: the same Location + Arguments state should
            // not appear twice, but the same app can appear with different arguments.
            var launchState = new LaunchState(normalizedLocation, normalizedArguments ?? string.Empty);
            if (!seenLaunchStates.Add(launchState))
            {
                throw new ArgumentException(
                    $"The launch target '{normalizedName}' is already in this MultiCut.",
                    nameof(launchTargets));
            }

            normalizedLaunchTargets.Add(new LaunchTarget(
                normalizedName,
                normalizedLocation,
                normalizedArguments));
        }

        if (normalizedLaunchTargets.Count == 0)
        {
            throw new ArgumentException("A MultiCut must contain at least one launch target.", nameof(launchTargets));
        }

        return normalizedLaunchTargets;
    }

    private static void WriteMultiCut(MultiCutShortcut multiCut, bool overwrite)
    {
        string? directory = Path.GetDirectoryName(multiCut.JsonPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!overwrite && File.Exists(multiCut.JsonPath))
        {
            throw new InvalidOperationException($"The JSON file already exists at '{multiCut.JsonPath}'.");
        }

        // The JSON file is the v1 MultiEX launch contract. Icon paths, shortcut paths,
        // database IDs, and timestamps stay out of this file.
        string json = JsonSerializer.Serialize(multiCut, JsonOptions);
        File.WriteAllText(multiCut.JsonPath, json);
    }

    private readonly record struct LaunchState(string Location, string Arguments);

    private sealed class LaunchStateComparer : IEqualityComparer<LaunchState>
    {
        internal static readonly LaunchStateComparer Instance = new();

        public bool Equals(LaunchState x, LaunchState y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.Location, y.Location)
                && StringComparer.Ordinal.Equals(x.Arguments, y.Arguments);
        }

        public int GetHashCode(LaunchState obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Location),
                StringComparer.Ordinal.GetHashCode(obj.Arguments));
        }
    }
}
