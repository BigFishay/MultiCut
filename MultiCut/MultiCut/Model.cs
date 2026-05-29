using MultiCut.Services;

namespace MultiCut;

/// <summary>
/// Root model entry point for UI state and shortcut-management behavior.
/// </summary>
internal class Model
{
    /// <summary>
    /// Gets the UI-facing backend API for loading, saving, and creating MultiCut shortcuts.
    /// </summary>
    public MultiCutAppService MultiCuts { get; } = new();
}
