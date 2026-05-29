namespace MultiCut.Shortcuts;

/// <summary>
/// Describes one item that MultiEX should open for a multi-launch shortcut.
/// </summary>
/// <remarks>
/// Instances of this class are child entries in the <see cref="MultiCutShortcut"/> JSON contract.
/// </remarks>
public class LaunchTarget
{
    /// <summary>
    /// Gets or sets the user-friendly name shown in the UI and log messages.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executable, shortcut, document, folder, or URL to open.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional command-line arguments to pass to the launch target.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchTarget"/> class.
    /// </summary>
    public LaunchTarget()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchTarget"/> class with launch data.
    /// </summary>
    /// <param name="name">The user-friendly name for the launch target.</param>
    /// <param name="location">The executable, shortcut, document, folder, or URL to open.</param>
    /// <param name="arguments">Optional command-line arguments for the target.</param>
    public LaunchTarget(string name, string location, string? arguments = null)
    {
        Name = name;
        Location = location;
        Arguments = arguments;
    }

    /// <summary>
    /// Returns the target location for simple display and debugging.
    /// </summary>
    /// <returns>The launch target location.</returns>
    public override string ToString()
    {
        return Location;
    }
}
