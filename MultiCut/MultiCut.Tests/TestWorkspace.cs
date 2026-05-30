namespace MultiCut.Tests;

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string directoryPath)
    {
        DirectoryPath = directoryPath;
        DatabasePath = Path.Combine(directoryPath, "multicut.db");
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public static TestWorkspace Create()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "MultiCut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return new TestWorkspace(directoryPath);
    }

    public string PathFor(string fileName)
    {
        return Path.Combine(DirectoryPath, fileName);
    }

    public void CreateEmptyFile(string fileName)
    {
        File.WriteAllText(PathFor(fileName), string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
