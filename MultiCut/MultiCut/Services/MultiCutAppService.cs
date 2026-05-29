using MultiCut.Shortcuts;

namespace MultiCut.Services;

/// <summary>
/// Provides the simple backend API intended for future view models.
/// </summary>
/// <remarks>
/// This facade keeps view models from coordinating JSON paths, file writes, shortcut
/// creation, and exception handling on their own.
/// </remarks>
public sealed class MultiCutAppService
{
    private const string StorageMutexName = @"Local\MultiCutStorage";
    private static readonly TimeSpan StorageLockTimeout = TimeSpan.FromSeconds(10);

    private readonly MultiCutStore multiCutStore;
    private readonly ShortcutCreationService shortcutCreationService;
    private readonly MultiCutPathService pathService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutAppService"/> class.
    /// </summary>
    public MultiCutAppService()
        : this(new MultiCutStore(), new ShortcutCreationService(), new MultiCutPathService())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutAppService"/> class with explicit dependencies.
    /// </summary>
    /// <param name="multiCutStore">The JSON-backed MultiCut store.</param>
    /// <param name="shortcutCreationService">The Windows shortcut creation service.</param>
    /// <param name="pathService">The path service.</param>
    public MultiCutAppService(
        MultiCutStore multiCutStore,
        ShortcutCreationService shortcutCreationService,
        MultiCutPathService pathService)
    {
        this.multiCutStore = multiCutStore;
        this.shortcutCreationService = shortcutCreationService;
        this.pathService = pathService;
    }

    /// <summary>
    /// Gets or sets an optional explicit path to MultiEX.exe for shortcut creation.
    /// </summary>
    public string? MultiExPath { get; set; }

    /// <summary>
    /// Gets the default directory where MultiCut JSON files are written.
    /// </summary>
    public string DefaultJsonDirectory => pathService.DefaultJsonDirectory;

    /// <summary>
    /// Gets the default directory where Windows shortcuts are created.
    /// </summary>
    public string DefaultShortcutDirectory => pathService.DefaultShortcutDirectory;

    /// <summary>
    /// Gets the currently loaded MultiCuts as a read-only snapshot.
    /// </summary>
    public IReadOnlyList<MultiCutShortcut> LoadedMultiCuts => multiCutStore.LoadedMultiCuts;

    /// <summary>
    /// Gets the unique launch targets currently referenced by loaded MultiCuts.
    /// </summary>
    public IReadOnlyList<LaunchTarget> LoadedLaunchTargets => multiCutStore.LoadedLaunchTargets;

    /// <summary>
    /// Gets the currently loaded MultiCuts keyed by absolute JSON path.
    /// </summary>
    public IReadOnlyDictionary<string, MultiCutShortcut> MultiCutsByJsonPath => multiCutStore.MultiCutsByJsonPath;

    /// <summary>
    /// Loads MultiCuts from the default JSON directory.
    /// </summary>
    /// <returns>The loaded MultiCuts and any skipped-file diagnostics.</returns>
    public OperationResult<LoadMultiCutsResult> LoadDefaultMultiCuts()
    {
        return Run(
            () => WithStorageLock(() => multiCutStore.LoadFromDirectoryWithDiagnostics(DefaultJsonDirectory)),
            BuildLoadMessage,
            "Unable to load MultiCuts.");
    }

    /// <summary>
    /// Loads MultiCuts from a directory.
    /// </summary>
    /// <param name="directoryPath">The directory to scan.</param>
    /// <returns>The loaded MultiCuts and any skipped-file diagnostics.</returns>
    public OperationResult<LoadMultiCutsResult> LoadMultiCuts(string directoryPath)
    {
        return Run(
            () => WithStorageLock(() => multiCutStore.LoadFromDirectoryWithDiagnostics(directoryPath)),
            BuildLoadMessage,
            "Unable to load MultiCuts.");
    }

    /// <summary>
    /// Creates and saves a MultiCut.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The launch targets that MultiEX should open.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="overwrite">Whether an existing JSON file can be replaced.</param>
    /// <returns>The created MultiCut.</returns>
    public OperationResult<MultiCutShortcut> CreateMultiCut(
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        bool overwrite = false)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                string targetJsonPath = ResolveJsonPath(name, jsonPath);
                return multiCutStore.Create(name, targetJsonPath, launchTargets, overwrite);
            }),
            "MultiCut created.",
            "Unable to create MultiCut.");
    }

    /// <summary>
    /// Saves an existing MultiCut.
    /// </summary>
    /// <param name="multiCut">The MultiCut to save.</param>
    /// <param name="overwrite">Whether an existing JSON file can be replaced.</param>
    /// <returns>The saved MultiCut.</returns>
    public OperationResult<MultiCutShortcut> SaveMultiCut(MultiCutShortcut multiCut, bool overwrite = true)
    {
        return Run(
            () => WithStorageLock(() => multiCutStore.Save(multiCut, overwrite)),
            "MultiCut saved.",
            "Unable to save MultiCut.");
    }

    /// <summary>
    /// Deletes a MultiCut from the index and optionally removes its JSON file.
    /// </summary>
    /// <param name="jsonPath">The JSON path to delete.</param>
    /// <param name="deleteJsonFile">Whether the JSON file should be deleted from disk.</param>
    /// <returns>A result describing whether anything was removed.</returns>
    public OperationResult DeleteMultiCut(string jsonPath, bool deleteJsonFile = true)
    {
        return Run(
            () => WithStorageLock(() => multiCutStore.Delete(jsonPath, deleteJsonFile)),
            removed => removed ? "MultiCut deleted." : "No matching MultiCut was found.",
            "Unable to delete MultiCut.");
    }

    /// <summary>
    /// Creates a Windows shortcut for an existing MultiCut.
    /// </summary>
    /// <param name="multiCut">The MultiCut whose JSON file should be passed to MultiEX.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <returns>The created shortcut path.</returns>
    public OperationResult<string> CreateShortcut(
        MultiCutShortcut multiCut,
        string? shortcutPath = null,
        string? multiExPath = null,
        string? iconPath = null,
        int? iconIndex = null)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                ArgumentNullException.ThrowIfNull(multiCut);

                string targetShortcutPath = ResolveShortcutPath(multiCut.Name, shortcutPath);
                string targetMultiExPath = pathService.ResolveMultiExPath(multiExPath ?? MultiExPath);
                shortcutCreationService.CreateShortcut(
                    multiCut,
                    targetShortcutPath,
                    targetMultiExPath,
                    iconPath,
                    iconIndex);

                return targetShortcutPath;
            }),
            "Shortcut created.",
            "Unable to create shortcut.");
    }

    /// <summary>
    /// Creates a MultiCut JSON file and its Windows shortcut as one ordered operation.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The launch targets that MultiEX should open.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <param name="overwrite">Whether existing files can be replaced.</param>
    /// <returns>The created MultiCut.</returns>
    public OperationResult<MultiCutShortcut> CreateMultiCutWithShortcut(
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        string? shortcutPath = null,
        string? multiExPath = null,
        string? iconPath = null,
        int? iconIndex = null,
        bool overwrite = false)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                string targetJsonPath = ResolveJsonPath(name, jsonPath);
                string targetShortcutPath = ResolveShortcutPath(name, shortcutPath);
                string targetMultiExPath = pathService.ResolveMultiExPath(multiExPath ?? MultiExPath);

                MultiCutShortcut multiCut = multiCutStore.Create(name, targetJsonPath, launchTargets, overwrite);
                shortcutCreationService.CreateShortcut(
                    multiCut,
                    targetShortcutPath,
                    targetMultiExPath,
                    iconPath,
                    iconIndex);

                return multiCut;
            }),
            "MultiCut and shortcut created.",
            "Unable to create MultiCut and shortcut.");
    }

    /// <summary>
    /// Loads MultiCuts from the default JSON directory without blocking the UI thread.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>The loaded MultiCuts and any skipped-file diagnostics.</returns>
    public Task<OperationResult<LoadMultiCutsResult>> LoadDefaultMultiCutsAsync(
        CancellationToken cancellationToken = default)
    {
        return RunAsync(LoadDefaultMultiCuts, cancellationToken);
    }

    /// <summary>
    /// Loads MultiCuts from a directory without blocking the UI thread.
    /// </summary>
    /// <param name="directoryPath">The directory to scan.</param>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>The loaded MultiCuts and any skipped-file diagnostics.</returns>
    public Task<OperationResult<LoadMultiCutsResult>> LoadMultiCutsAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(() => LoadMultiCuts(directoryPath), cancellationToken);
    }

    /// <summary>
    /// Creates and saves a MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The launch targets that MultiEX should open.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="overwrite">Whether an existing JSON file can be replaced.</param>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>The created MultiCut.</returns>
    public Task<OperationResult<MultiCutShortcut>> CreateMultiCutAsync(
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        List<LaunchTarget> launchTargetSnapshot;
        try
        {
            launchTargetSnapshot = launchTargets.ToList();
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<MultiCutShortcut>.Failed("Unable to create MultiCut.", exception));
        }

        return RunAsync(() => CreateMultiCut(name, launchTargetSnapshot, jsonPath, overwrite), cancellationToken);
    }

    /// <summary>
    /// Creates a MultiCut JSON file and its Windows shortcut without blocking the UI thread.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The launch targets that MultiEX should open.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <param name="overwrite">Whether existing files can be replaced.</param>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>The created MultiCut.</returns>
    public Task<OperationResult<MultiCutShortcut>> CreateMultiCutWithShortcutAsync(
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        string? shortcutPath = null,
        string? multiExPath = null,
        string? iconPath = null,
        int? iconIndex = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        List<LaunchTarget> launchTargetSnapshot;
        try
        {
            launchTargetSnapshot = launchTargets.ToList();
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<MultiCutShortcut>.Failed(
                "Unable to create MultiCut and shortcut.",
                exception));
        }

        return RunAsync(
            () => CreateMultiCutWithShortcut(
                name,
                launchTargetSnapshot,
                jsonPath,
                shortcutPath,
                multiExPath,
                iconPath,
                iconIndex,
                overwrite),
            cancellationToken);
    }

    /// <summary>
    /// Saves an existing MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="multiCut">The MultiCut to save.</param>
    /// <param name="overwrite">Whether an existing JSON file can be replaced.</param>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>The saved MultiCut.</returns>
    public Task<OperationResult<MultiCutShortcut>> SaveMultiCutAsync(
        MultiCutShortcut multiCut,
        bool overwrite = true,
        CancellationToken cancellationToken = default)
    {
        MultiCutShortcut multiCutSnapshot;
        try
        {
            ArgumentNullException.ThrowIfNull(multiCut);
            multiCutSnapshot = new MultiCutShortcut(
                multiCut.Name,
                multiCut.JsonPath,
                multiCut.LaunchTargets.ToList());
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<MultiCutShortcut>.Failed("Unable to save MultiCut.", exception));
        }

        return RunAsync(() => SaveMultiCut(multiCutSnapshot, overwrite), cancellationToken);
    }

    /// <summary>
    /// Deletes a MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="jsonPath">The JSON path to delete.</param>
    /// <param name="deleteJsonFile">Whether the JSON file should be deleted from disk.</param>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>A result describing whether anything was removed.</returns>
    public Task<OperationResult> DeleteMultiCutAsync(
        string jsonPath,
        bool deleteJsonFile = true,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(() => DeleteMultiCut(jsonPath, deleteJsonFile), cancellationToken);
    }

    /// <summary>
    /// Creates a Windows shortcut without blocking the UI thread.
    /// </summary>
    /// <param name="multiCut">The MultiCut whose JSON file should be passed to MultiEX.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <param name="cancellationToken">A token used to cancel the background operation.</param>
    /// <returns>The created shortcut path.</returns>
    public Task<OperationResult<string>> CreateShortcutAsync(
        MultiCutShortcut multiCut,
        string? shortcutPath = null,
        string? multiExPath = null,
        string? iconPath = null,
        int? iconIndex = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            () => CreateShortcut(multiCut, shortcutPath, multiExPath, iconPath, iconIndex),
            cancellationToken);
    }

    private static OperationResult Run(
        Func<bool> operation,
        Func<bool, string> successMessage,
        string failureMessage)
    {
        try
        {
            bool result = operation();
            return OperationResult.Succeeded(successMessage(result));
        }
        catch (Exception exception)
        {
            return OperationResult.Failed(failureMessage, exception);
        }
    }

    private static OperationResult<T> Run<T>(
        Func<T> operation,
        string successMessage,
        string failureMessage)
    {
        return Run(operation, _ => successMessage, failureMessage);
    }

    private static OperationResult<T> Run<T>(
        Func<T> operation,
        Func<T, string> successMessage,
        string failureMessage)
    {
        try
        {
            T result = operation();
            return OperationResult<T>.Succeeded(result, successMessage(result));
        }
        catch (Exception exception)
        {
            return OperationResult<T>.Failed(failureMessage, exception);
        }
    }

    private static Task<OperationResult> RunAsync(
        Func<OperationResult> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            },
            cancellationToken);
    }

    private static Task<OperationResult<T>> RunAsync<T>(
        Func<OperationResult<T>> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            },
            cancellationToken);
    }

    private static T WithStorageLock<T>(Func<T> operation)
    {
        using var storageMutex = new Mutex(false, StorageMutexName);
        bool lockAcquired = false;
        try
        {
            try
            {
                lockAcquired = storageMutex.WaitOne(StorageLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                lockAcquired = true;
            }

            if (!lockAcquired)
            {
                throw new TimeoutException("MultiCut storage is busy. Try again in a few seconds.");
            }

            return operation();
        }
        finally
        {
            if (lockAcquired)
            {
                storageMutex.ReleaseMutex();
            }
        }
    }

    private string ResolveJsonPath(string name, string? jsonPath)
    {
        return string.IsNullOrWhiteSpace(jsonPath)
            ? pathService.GetDefaultJsonPath(name)
            : pathService.NormalizeJsonPath(jsonPath);
    }

    private string ResolveShortcutPath(string name, string? shortcutPath)
    {
        return string.IsNullOrWhiteSpace(shortcutPath)
            ? pathService.GetDefaultShortcutPath(name)
            : pathService.NormalizeShortcutPath(shortcutPath);
    }

    private static string BuildLoadMessage(LoadMultiCutsResult result)
    {
        return result.HasSkippedFiles
            ? $"Loaded {result.MultiCuts.Count} MultiCuts. Skipped {result.SkippedFiles.Count} file(s)."
            : $"Loaded {result.MultiCuts.Count} MultiCuts.";
    }
}
