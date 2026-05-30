namespace MultiCut.Shortcuts;

/// <summary>
/// Provides shared validation, normalization, and equality behavior for launch targets.
/// </summary>
public static class LaunchTargetRules
{
    /// <summary>
    /// Normalizes a sequence of launch targets and enforces the MultiCut set rule.
    /// </summary>
    /// <param name="launchTargets">The launch targets to normalize.</param>
    /// <returns>A normalized ordered set of launch targets.</returns>
    /// <exception cref="ArgumentException">Thrown when a target is invalid or duplicated.</exception>
    public static List<LaunchTarget> NormalizeLaunchTargets(IEnumerable<LaunchTarget> launchTargets)
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

            LaunchTarget normalizedLaunchTarget = NormalizeLaunchTarget(launchTarget);
            var launchState = GetLaunchState(normalizedLaunchTarget);
            if (!seenLaunchStates.Add(launchState))
            {
                throw new ArgumentException(
                    $"The launch target '{normalizedLaunchTarget.Name}' is already in this MultiCut.",
                    nameof(launchTargets));
            }

            normalizedLaunchTargets.Add(normalizedLaunchTarget);
        }

        if (normalizedLaunchTargets.Count == 0)
        {
            throw new ArgumentException("A MultiCut must contain at least one launch target.", nameof(launchTargets));
        }

        return normalizedLaunchTargets;
    }

    /// <summary>
    /// Normalizes one launch target.
    /// </summary>
    /// <param name="launchTarget">The launch target to normalize.</param>
    /// <returns>A normalized copy of the launch target.</returns>
    public static LaunchTarget NormalizeLaunchTarget(LaunchTarget launchTarget)
    {
        ArgumentNullException.ThrowIfNull(launchTarget);

        if (string.IsNullOrWhiteSpace(launchTarget.Location))
        {
            throw new ArgumentException("Launch target location cannot be blank.", nameof(launchTarget));
        }

        string normalizedLocation = launchTarget.Location.Trim();
        string normalizedName = string.IsNullOrWhiteSpace(launchTarget.Name)
            ? normalizedLocation
            : launchTarget.Name.Trim();
        string? normalizedArguments = string.IsNullOrWhiteSpace(launchTarget.Arguments)
            ? null
            : launchTarget.Arguments.Trim();

        return new LaunchTarget(normalizedName, normalizedLocation, normalizedArguments);
    }

    /// <summary>
    /// Gets a database-safe argument value for a launch target.
    /// </summary>
    /// <param name="arguments">The argument value to normalize.</param>
    /// <returns>A trimmed argument value, or an empty string when no arguments are present.</returns>
    public static string NormalizeArgumentsForStorage(string? arguments)
    {
        return string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments.Trim();
    }

    /// <summary>
    /// Determines whether two launch targets represent the same launch state.
    /// </summary>
    /// <param name="first">The first launch target.</param>
    /// <param name="second">The second launch target.</param>
    /// <returns><see langword="true"/> when both targets have the same location and arguments.</returns>
    public static bool AreSameLaunchState(LaunchTarget first, LaunchTarget second)
    {
        return LaunchStateComparer.Instance.Equals(GetLaunchState(first), GetLaunchState(second));
    }

    /// <summary>
    /// Gets a hash code for the normalized launch state.
    /// </summary>
    /// <param name="launchTarget">The launch target to hash.</param>
    /// <returns>A hash code based on location and arguments.</returns>
    public static int GetLaunchStateHashCode(LaunchTarget launchTarget)
    {
        return LaunchStateComparer.Instance.GetHashCode(GetLaunchState(launchTarget));
    }

    private static LaunchState GetLaunchState(LaunchTarget launchTarget)
    {
        LaunchTarget normalizedLaunchTarget = NormalizeLaunchTarget(launchTarget);
        return new LaunchState(
            normalizedLaunchTarget.Location,
            NormalizeArgumentsForStorage(normalizedLaunchTarget.Arguments));
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
