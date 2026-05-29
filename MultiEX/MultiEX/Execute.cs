using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.Json;
using MultiCut.Shortcuts;

namespace MultiEX
{
    /// <summary>
    /// Loads a shortcut JSON file and opens each launch target without showing a console window.
    /// </summary>
    /// <remarks>
    /// The project is built as a Windows executable, so all user-visible diagnostics must go to the log file.
    /// </remarks>
    internal class Execute
    {
        private const long MaxLogFileBytes = 1_048_576;
        private const string LogMutexName = @"Local\MultiCutMultiEXLog";

        /// <summary>
        /// Program entry point for MultiEX.
        /// </summary>
        /// <param name="args">The first argument must be the path to a shortcut JSON file.</param>
        /// <returns>Zero when every valid launch target starts successfully; otherwise, one.</returns>
        static int Main(string[] args)
        {
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                LogError("MultiEX started without a JSON file argument.");
                return 1;
            }

            IReadOnlyList<LaunchTarget> launchTargets;
            try
            {
                launchTargets = LoadLaunchTargets(args[0]);
            }
            catch (Exception exception)
            {
                LogError("Failed to load launch targets.", exception);
                return 1;
            }

            if (launchTargets.Count == 0)
            {
                LogError("No valid launch targets were found.");
                return 1;
            }

            int exitCode = 0;
            foreach (LaunchTarget launchTarget in launchTargets)
            {
                try
                {
                    LaunchProgram(launchTarget);
                }
                catch (Exception exception)
                {
                    LogError($"Failed to launch '{launchTarget.Name}' from '{launchTarget.Location}'.", exception);
                    exitCode = 1;
                }
            }

            return exitCode;
        }

        /// <summary>
        /// Reads a MultiCut JSON file and validates the launch targets inside it.
        /// </summary>
        /// <param name="jsonLocation">The file path provided by the Windows shortcut.</param>
        /// <returns>The valid launch targets found in the JSON file.</returns>
        /// <exception cref="InvalidDataException">Thrown when the JSON root is not a valid MultiCut shortcut.</exception>
        private static IReadOnlyList<LaunchTarget> LoadLaunchTargets(string jsonLocation)
        {
            string jsonPath = Environment.ExpandEnvironmentVariables(jsonLocation.Trim().Trim('"'));
            string json = File.ReadAllText(jsonPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            MultiCutShortcut? multiCut = JsonSerializer.Deserialize<MultiCutShortcut>(json, options);
            if (multiCut is null)
            {
                throw new InvalidDataException("MultiCut JSON cannot be null.");
            }

            if (multiCut.LaunchTargets is null || multiCut.LaunchTargets.Count == 0)
            {
                throw new InvalidDataException("MultiCut JSON must include at least one launch target.");
            }

            // Bad JSON structure is fatal, but bad individual entries are skipped so one typo
            // does not prevent the remaining valid applications from opening.
            var validatedLaunchTargets = new List<LaunchTarget>();
            for (int index = 0; index < multiCut.LaunchTargets.Count; index++)
            {
                LaunchTarget? launchTarget = multiCut.LaunchTargets[index];
                if (launchTarget is null)
                {
                    LogError($"Launch target {index + 1} is null and was skipped.");
                    continue;
                }

                NormalizeLaunchTarget(launchTarget, index);

                if (string.IsNullOrWhiteSpace(launchTarget.Location))
                {
                    LogError($"Launch target '{launchTarget.Name}' does not include a location and was skipped.");
                    continue;
                }

                validatedLaunchTargets.Add(launchTarget);
            }

            return validatedLaunchTargets;
        }

        /// <summary>
        /// Cleans up display and launch values after JSON deserialization.
        /// </summary>
        /// <param name="launchTarget">The launch target to normalize.</param>
        /// <param name="index">The zero-based JSON array position, used for fallback names.</param>
        private static void NormalizeLaunchTarget(LaunchTarget launchTarget, int index)
        {
            string fallbackName = $"Launch target {index + 1}";
            if (!string.IsNullOrWhiteSpace(launchTarget.Location))
            {
                launchTarget.Location = launchTarget.Location.Trim();
                fallbackName = launchTarget.Location;
            }

            launchTarget.Name = string.IsNullOrWhiteSpace(launchTarget.Name)
                ? fallbackName
                : launchTarget.Name.Trim();
        }

        /// <summary>
        /// Opens a launch target using Windows Shell behavior.
        /// </summary>
        /// <param name="launchTarget">The launch target to open.</param>
        private static void LaunchProgram(LaunchTarget launchTarget)
        {
            ProcessStartInfo startInfo = CreateStartInfo(launchTarget.Location);
            if (!string.IsNullOrWhiteSpace(launchTarget.Arguments))
            {
                startInfo.Arguments = Environment.ExpandEnvironmentVariables(launchTarget.Arguments.Trim());
            }

            Process.Start(startInfo);
        }

        /// <summary>
        /// Creates the process start information used to open a location like Explorer would.
        /// </summary>
        /// <param name="location">The executable, shortcut, document, folder, or URL to open.</param>
        /// <returns>A configured <see cref="ProcessStartInfo"/> instance.</returns>
        /// <exception cref="ArgumentException">Thrown when the launch location is blank.</exception>
        private static ProcessStartInfo CreateStartInfo(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Launch target location cannot be blank.", nameof(location));
            }

            string launchLocation = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
            var startInfo = new ProcessStartInfo
            {
                FileName = launchLocation,
                // ShellExecute keeps MultiEX close to "double-click this in Explorer" behavior.
                // It also lets child apps request elevation on their own instead of elevating MultiEX.
                UseShellExecute = true
            };

            // Do not override the working directory for .lnk files; Windows shortcuts
            // can define their own "Start in" value that ShellExecute should honor.
            if (File.Exists(launchLocation) && !IsShortcutFile(launchLocation))
            {
                string? workingDirectory = Path.GetDirectoryName(launchLocation);
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }
            }
            else if (Directory.Exists(launchLocation))
            {
                startInfo.WorkingDirectory = launchLocation;
            }

            return startInfo;
        }

        /// <summary>
        /// Determines whether a location points to a Windows shortcut file.
        /// </summary>
        /// <param name="location">The location to inspect.</param>
        /// <returns><see langword="true"/> when the location has a .lnk extension; otherwise, <see langword="false"/>.</returns>
        private static bool IsShortcutFile(string location)
        {
            return string.Equals(Path.GetExtension(location), ".lnk", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes a diagnostic message to the user-local MultiEX log.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">An optional exception to include in the log entry.</param>
        private static void LogError(string message, Exception? exception = null)
        {
            try
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MultiCut");
                Directory.CreateDirectory(logDirectory);

                string logPath = Path.Combine(logDirectory, "MultiEX.log");
                string logEntry = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
                if (exception is not null)
                {
                    logEntry += $"{exception}{Environment.NewLine}";
                }

                WriteLogEntry(logPath, logEntry);
            }
            catch
            {
                // MultiEX has no UI or console. Logging must never block the launcher itself.
            }
        }

        /// <summary>
        /// Appends a log entry while coordinating with other MultiEX instances.
        /// </summary>
        /// <param name="logPath">The log file path.</param>
        /// <param name="logEntry">The fully formatted log entry.</param>
        private static void WriteLogEntry(string logPath, string logEntry)
        {
            using var logMutex = new Mutex(false, LogMutexName);
            bool mutexAcquired = false;
            try
            {
                // Several shortcuts can invoke MultiEX at once. The mutex protects rotation
                // and append from racing across those separate processes.
                mutexAcquired = logMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                mutexAcquired = true;
            }

            try
            {
                if (!mutexAcquired)
                {
                    // If another instance is holding the mutex too long, prefer a best-effort
                    // append over losing diagnostics completely.
                    File.AppendAllText(logPath, logEntry);
                    return;
                }

                RotateLogIfNeeded(logPath);
                File.AppendAllText(logPath, logEntry);
            }
            finally
            {
                if (mutexAcquired)
                {
                    logMutex.ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Rotates the log file when it grows beyond the configured size cap.
        /// </summary>
        /// <param name="logPath">The log file path.</param>
        private static void RotateLogIfNeeded(string logPath)
        {
            if (!File.Exists(logPath) || new FileInfo(logPath).Length <= MaxLogFileBytes)
            {
                return;
            }

            string backupLogPath = Path.ChangeExtension(logPath, ".log.old");
            if (File.Exists(backupLogPath))
            {
                File.Delete(backupLogPath);
            }

            File.Move(logPath, backupLogPath);
        }
    }
}
