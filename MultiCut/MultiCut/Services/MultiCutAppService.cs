using System.IO;
using MultiCut.Data;
using MultiCut.Shortcuts;

namespace MultiCut.Services;

/// <summary>
/// Provides the simple backend API intended for future view models.
/// </summary>
/// <remarks>
/// SQLite is the source of truth for the UI. JSON files are generated launch artifacts
/// that MultiEX consumes when a Windows shortcut is opened.
/// </remarks>
public sealed class MultiCutAppService
{
    private const string StorageMutexName = @"Local\MultiCutStorage";
    private static readonly TimeSpan StorageLockTimeout = TimeSpan.FromSeconds(10);

    private readonly MultiCutPathService pathService;
    private readonly MultiCutRepository repository;
    private readonly ShortcutCreationService shortcutCreationService;
    private bool initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutAppService"/> class.
    /// </summary>
    public MultiCutAppService()
        : this(new MultiCutPathService())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutAppService"/> class with a path service.
    /// </summary>
    /// <param name="pathService">The path service used by the app.</param>
    public MultiCutAppService(MultiCutPathService pathService)
        : this(
            pathService,
            new MultiCutRepository(pathService.DefaultDatabasePath),
            new ShortcutCreationService())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutAppService"/> class with explicit dependencies.
    /// </summary>
    /// <param name="pathService">The path service used by the app.</param>
    /// <param name="repository">The SQLite repository.</param>
    /// <param name="shortcutCreationService">The Windows shortcut creation service.</param>
    public MultiCutAppService(
        MultiCutPathService pathService,
        MultiCutRepository repository,
        ShortcutCreationService shortcutCreationService)
    {
        this.pathService = pathService;
        this.repository = repository;
        this.shortcutCreationService = shortcutCreationService;
    }

    /// <summary>
    /// Gets or sets an optional explicit path to MultiEX.exe for shortcut creation.
    /// </summary>
    public string? MultiExPath { get; set; }

    /// <summary>
    /// Gets the default SQLite database path.
    /// </summary>
    public string DefaultDatabasePath => pathService.DefaultDatabasePath;

    /// <summary>
    /// Gets the default directory where MultiCut JSON files are generated.
    /// </summary>
    public string DefaultJsonDirectory => pathService.DefaultJsonDirectory;

    /// <summary>
    /// Gets the default directory where Windows shortcuts are created.
    /// </summary>
    public string DefaultShortcutDirectory => pathService.DefaultShortcutDirectory;

    /// <summary>
    /// Creates the database file and schema if needed.
    /// </summary>
    /// <returns>The operation result.</returns>
    public OperationResult Initialize()
    {
        return Run(
            () =>
            {
                EnsureInitialized();
                return true;
            },
            _ => "MultiCut database is ready.",
            "Unable to initialize MultiCut database.");
    }

    /// <summary>
    /// Gets every current MultiCut for read-only UI list views.
    /// </summary>
    /// <returns>The current MultiCut list.</returns>
    public OperationResult<IReadOnlyList<MultiCutListItem>> GetCurrentMultiCuts()
    {
        return Run(
            () => WithStorageLock(repository.GetCurrentMultiCuts),
            multiCuts => $"Loaded {multiCuts.Count} MultiCuts.",
            "Unable to load MultiCuts.");
    }

    /// <summary>
    /// Gets every current reusable launch target for read-only UI list views.
    /// </summary>
    /// <returns>The current launch target list.</returns>
    public OperationResult<IReadOnlyList<LaunchTargetListItem>> GetCurrentLaunchTargets()
    {
        return Run(
            () => WithStorageLock(repository.GetCurrentLaunchTargets),
            launchTargets => $"Loaded {launchTargets.Count} launch targets.",
            "Unable to load launch targets.");
    }

    /// <summary>
    /// Gets one complete MultiCut for editing.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <returns>The matching MultiCut record.</returns>
    public OperationResult<MultiCutRecord> GetMultiCut(long multiCutId)
    {
        return Run(
            () => WithStorageLock(() => repository.GetMultiCut(multiCutId)),
            "MultiCut loaded.",
            "Unable to load MultiCut.");
    }

    /// <summary>
    /// Creates or replaces a MultiCut in the database and writes its generated JSON file.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="overwrite">Whether an existing MultiCut at the same JSON path can be replaced.</param>
    /// <returns>The saved MultiCut record.</returns>
    public OperationResult<MultiCutRecord> SaveMultiCut(
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        bool overwrite = false)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                List<LaunchTarget> launchTargetSnapshot = launchTargets.ToList();
                MultiCutRecord multiCut = repository.SaveMultiCut(
                    name,
                    ResolveJsonPath(name, jsonPath),
                    launchTargetSnapshot,
                    overwrite: overwrite);
                WriteJson(multiCut);
                return multiCut;
            }),
            "MultiCut saved.",
            "Unable to save MultiCut.");
    }

    /// <summary>
    /// Updates an existing MultiCut by ID and rewrites its generated JSON file.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional replacement JSON path. When omitted, the current path is kept.</param>
    /// <param name="deleteOldJsonFile">Whether to delete the old JSON file when the JSON path changes.</param>
    /// <returns>The updated MultiCut record.</returns>
    public OperationResult<MultiCutRecord> UpdateMultiCut(
        long multiCutId,
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        bool deleteOldJsonFile = true)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                List<LaunchTarget> launchTargetSnapshot = launchTargets.ToList();
                MultiCutRecord oldMultiCut = repository.GetMultiCut(multiCutId);
                MultiCutRecord updatedMultiCut = repository.UpdateMultiCut(
                    multiCutId,
                    name,
                    launchTargetSnapshot,
                    jsonPath);

                WriteJson(updatedMultiCut);
                bool jsonPathChanged = !string.Equals(
                    oldMultiCut.JsonPath,
                    updatedMultiCut.JsonPath,
                    StringComparison.OrdinalIgnoreCase);
                if (jsonPathChanged)
                {
                    RefreshShortcutForJsonPathChange(updatedMultiCut);
                    DeleteGeneratedFile(deleteOldJsonFile, oldMultiCut.JsonPath);
                }

                return updatedMultiCut;
            }),
            "MultiCut updated.",
            "Unable to update MultiCut.");
    }

    /// <summary>
    /// Creates or replaces a MultiCut, writes its JSON file, and creates its Windows shortcut.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <param name="overwrite">Whether an existing MultiCut at the same JSON path can be replaced.</param>
    /// <returns>The saved MultiCut record.</returns>
    public OperationResult<MultiCutRecord> SaveMultiCutWithShortcut(
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
                List<LaunchTarget> launchTargetSnapshot = launchTargets.ToList();
                string resolvedShortcutPath = ResolveShortcutPath(name, shortcutPath);
                MultiCutRecord multiCut = repository.SaveMultiCut(
                    name,
                    ResolveJsonPath(name, jsonPath),
                    launchTargetSnapshot,
                    overwrite: overwrite);
                WriteJson(multiCut);
                CreateShortcutFile(multiCut, resolvedShortcutPath, multiExPath, iconPath, iconIndex);
                repository.UpdateShortcutMetadata(multiCut.Id, resolvedShortcutPath, iconPath, iconIndex);
                return repository.GetMultiCut(multiCut.Id);
            }),
            "MultiCut and shortcut saved.",
            "Unable to save MultiCut and shortcut.");
    }

    /// <summary>
    /// Regenerates the JSON file for an existing database MultiCut.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <returns>The regenerated MultiCut record.</returns>
    public OperationResult<MultiCutRecord> RegenerateJson(long multiCutId)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                MultiCutRecord multiCut = repository.GetMultiCut(multiCutId);
                WriteJson(multiCut);
                return multiCut;
            }),
            "MultiCut JSON regenerated.",
            "Unable to regenerate MultiCut JSON.");
    }

    /// <summary>
    /// Creates or replaces a Windows shortcut for an existing MultiCut.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <returns>The created shortcut path.</returns>
    public OperationResult<string> CreateShortcut(
        long multiCutId,
        string? shortcutPath = null,
        string? multiExPath = null,
        string? iconPath = null,
        int? iconIndex = null)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                MultiCutRecord multiCut = repository.GetMultiCut(multiCutId);
                string resolvedShortcutPath = ResolveShortcutPath(multiCut.Name, shortcutPath);
                string? resolvedIconPath = iconPath ?? multiCut.IconPath;
                int? resolvedIconIndex = iconIndex ?? multiCut.IconIndex;

                WriteJson(multiCut);
                CreateShortcutFile(multiCut, resolvedShortcutPath, multiExPath, resolvedIconPath, resolvedIconIndex);
                repository.UpdateShortcutMetadata(
                    multiCut.Id,
                    resolvedShortcutPath,
                    resolvedIconPath,
                    resolvedIconIndex);

                return resolvedShortcutPath;
            }),
            "Shortcut created.",
            "Unable to create shortcut.");
    }

    /// <summary>
    /// Updates a reusable launch target and regenerates every affected parent JSON file.
    /// </summary>
    /// <param name="launchTargetId">The launch target database identifier.</param>
    /// <param name="launchTarget">The updated launch target values.</param>
    /// <returns>The affected parent MultiCuts.</returns>
    public OperationResult<IReadOnlyList<MultiCutRecord>> UpdateLaunchTarget(
        long launchTargetId,
        LaunchTarget launchTarget)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                IReadOnlyList<MultiCutRecord> affectedMultiCuts =
                    repository.UpdateLaunchTarget(launchTargetId, launchTarget);
                foreach (MultiCutRecord multiCut in affectedMultiCuts)
                {
                    WriteJson(multiCut);
                }

                return affectedMultiCuts;
            }),
            affectedMultiCuts => $"Updated launch target and regenerated {affectedMultiCuts.Count} MultiCut JSON file(s).",
            "Unable to update launch target.");
    }

    /// <summary>
    /// Deletes a MultiCut and optionally removes its generated artifacts.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="deleteJsonFile">Whether to delete the generated JSON file.</param>
    /// <param name="deleteShortcutFile">Whether to delete the Windows shortcut file when known.</param>
    /// <returns>The operation result.</returns>
    public OperationResult DeleteMultiCut(
        long multiCutId,
        bool deleteJsonFile = true,
        bool deleteShortcutFile = false)
    {
        return Run(
            () => WithStorageLock(() =>
            {
                MultiCutRecord? deletedMultiCut = repository.DeleteMultiCut(multiCutId);
                if (deletedMultiCut is null)
                {
                    return false;
                }

                DeleteGeneratedFile(deleteJsonFile, deletedMultiCut.JsonPath);
                DeleteGeneratedFile(deleteShortcutFile, deletedMultiCut.ShortcutPath);
                return true;
            }),
            deleted => deleted ? "MultiCut deleted." : "No matching MultiCut was found.",
            "Unable to delete MultiCut.");
    }

    /// <summary>
    /// Gets every current MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The current MultiCut list.</returns>
    public Task<OperationResult<IReadOnlyList<MultiCutListItem>>> GetCurrentMultiCutsAsync(
        CancellationToken cancellationToken = default)
    {
        return RunAsync(GetCurrentMultiCuts, cancellationToken);
    }

    /// <summary>
    /// Gets every current launch target without blocking the UI thread.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The current launch target list.</returns>
    public Task<OperationResult<IReadOnlyList<LaunchTargetListItem>>> GetCurrentLaunchTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        return RunAsync(GetCurrentLaunchTargets, cancellationToken);
    }

    /// <summary>
    /// Creates the database file and schema without blocking the UI thread.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(Initialize, cancellationToken);
    }

    /// <summary>
    /// Gets one complete MultiCut for editing without blocking the UI thread.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The matching MultiCut record.</returns>
    public Task<OperationResult<MultiCutRecord>> GetMultiCutAsync(
        long multiCutId,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(() => GetMultiCut(multiCutId), cancellationToken);
    }

    /// <summary>
    /// Creates or replaces a MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="overwrite">Whether an existing MultiCut at the same JSON path can be replaced.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The saved MultiCut record.</returns>
    public Task<OperationResult<MultiCutRecord>> SaveMultiCutAsync(
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (!TrySnapshotLaunchTargets(launchTargets, "Unable to save MultiCut.", out List<LaunchTarget> snapshot, out OperationResult<MultiCutRecord> failure))
        {
            return Task.FromResult(failure);
        }

        return RunAsync(() => SaveMultiCut(name, snapshot, jsonPath, overwrite), cancellationToken);
    }

    /// <summary>
    /// Updates an existing MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional replacement JSON path. When omitted, the current path is kept.</param>
    /// <param name="deleteOldJsonFile">Whether to delete the old JSON file when the JSON path changes.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The updated MultiCut record.</returns>
    public Task<OperationResult<MultiCutRecord>> UpdateMultiCutAsync(
        long multiCutId,
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null,
        bool deleteOldJsonFile = true,
        CancellationToken cancellationToken = default)
    {
        if (!TrySnapshotLaunchTargets(launchTargets, "Unable to update MultiCut.", out List<LaunchTarget> snapshot, out OperationResult<MultiCutRecord> failure))
        {
            return Task.FromResult(failure);
        }

        return RunAsync(
            () => UpdateMultiCut(multiCutId, name, snapshot, jsonPath, deleteOldJsonFile),
            cancellationToken);
    }

    /// <summary>
    /// Creates or replaces a MultiCut and shortcut without blocking the UI thread.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional explicit JSON path.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <param name="overwrite">Whether an existing MultiCut at the same JSON path can be replaced.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The saved MultiCut record.</returns>
    public Task<OperationResult<MultiCutRecord>> SaveMultiCutWithShortcutAsync(
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
        if (!TrySnapshotLaunchTargets(launchTargets, "Unable to save MultiCut and shortcut.", out List<LaunchTarget> snapshot, out OperationResult<MultiCutRecord> failure))
        {
            return Task.FromResult(failure);
        }

        return RunAsync(
            () => SaveMultiCutWithShortcut(
                name,
                snapshot,
                jsonPath,
                shortcutPath,
                multiExPath,
                iconPath,
                iconIndex,
                overwrite),
            cancellationToken);
    }

    /// <summary>
    /// Regenerates one MultiCut JSON file without blocking the UI thread.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The regenerated MultiCut record.</returns>
    public Task<OperationResult<MultiCutRecord>> RegenerateJsonAsync(
        long multiCutId,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(() => RegenerateJson(multiCutId), cancellationToken);
    }

    /// <summary>
    /// Creates or replaces a Windows shortcut without blocking the UI thread.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="shortcutPath">An optional explicit .lnk path.</param>
    /// <param name="multiExPath">An optional explicit MultiEX.exe path.</param>
    /// <param name="iconPath">An optional icon source path.</param>
    /// <param name="iconIndex">An optional icon index.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The created shortcut path.</returns>
    public Task<OperationResult<string>> CreateShortcutAsync(
        long multiCutId,
        string? shortcutPath = null,
        string? multiExPath = null,
        string? iconPath = null,
        int? iconIndex = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            () => CreateShortcut(multiCutId, shortcutPath, multiExPath, iconPath, iconIndex),
            cancellationToken);
    }

    /// <summary>
    /// Updates a reusable launch target without blocking the UI thread.
    /// </summary>
    /// <param name="launchTargetId">The launch target database identifier.</param>
    /// <param name="launchTarget">The updated launch target values.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The affected parent MultiCuts.</returns>
    public Task<OperationResult<IReadOnlyList<MultiCutRecord>>> UpdateLaunchTargetAsync(
        long launchTargetId,
        LaunchTarget launchTarget,
        CancellationToken cancellationToken = default)
    {
        LaunchTarget launchTargetSnapshot;
        try
        {
            ArgumentNullException.ThrowIfNull(launchTarget);
            launchTargetSnapshot = new LaunchTarget(
                launchTarget.Name,
                launchTarget.Location,
                launchTarget.Arguments);
        }
        catch (Exception exception)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<MultiCutRecord>>.Failed(
                "Unable to update launch target.",
                exception));
        }

        return RunAsync(() => UpdateLaunchTarget(launchTargetId, launchTargetSnapshot), cancellationToken);
    }

    /// <summary>
    /// Deletes a MultiCut without blocking the UI thread.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="deleteJsonFile">Whether to delete the generated JSON file.</param>
    /// <param name="deleteShortcutFile">Whether to delete the Windows shortcut file when known.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The operation result.</returns>
    public Task<OperationResult> DeleteMultiCutAsync(
        long multiCutId,
        bool deleteJsonFile = true,
        bool deleteShortcutFile = false,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            () => DeleteMultiCut(multiCutId, deleteJsonFile, deleteShortcutFile),
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

    private static bool TrySnapshotLaunchTargets(
        IEnumerable<LaunchTarget> launchTargets,
        string failureMessage,
        out List<LaunchTarget> snapshot,
        out OperationResult<MultiCutRecord> failure)
    {
        try
        {
            snapshot = launchTargets.ToList();
            failure = OperationResult<MultiCutRecord>.Succeeded(
                new MultiCutRecord(0, string.Empty, string.Empty, null, null, null, []));
            return true;
        }
        catch (Exception exception)
        {
            snapshot = [];
            failure = OperationResult<MultiCutRecord>.Failed(failureMessage, exception);
            return false;
        }
    }

    private T WithStorageLock<T>(Func<T> operation)
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

            EnsureInitialized();
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

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        repository.Initialize();
        initialized = true;
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

    private void CreateShortcutFile(
        MultiCutRecord multiCut,
        string shortcutPath,
        string? multiExPath,
        string? iconPath,
        int? iconIndex)
    {
        string resolvedMultiExPath = pathService.ResolveMultiExPath(multiExPath ?? MultiExPath);
        shortcutCreationService.CreateShortcut(
            multiCut.ToShortcutContract(),
            shortcutPath,
            resolvedMultiExPath,
            iconPath,
            iconIndex);
    }

    private void RefreshShortcutForJsonPathChange(MultiCutRecord multiCut)
    {
        if (string.IsNullOrWhiteSpace(multiCut.ShortcutPath))
        {
            return;
        }

        CreateShortcutFile(
            multiCut,
            multiCut.ShortcutPath,
            null,
            multiCut.IconPath,
            multiCut.IconIndex);
    }

    private static void WriteJson(MultiCutRecord multiCut)
    {
        MultiCutShortcutJson.WriteToFile(multiCut.ToShortcutContract(), multiCut.JsonPath);
    }

    private static void DeleteGeneratedFile(bool shouldDelete, string? filePath)
    {
        if (!shouldDelete || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        File.Delete(filePath);
    }
}
