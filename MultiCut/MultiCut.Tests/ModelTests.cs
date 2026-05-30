using MultiCut.Services;

namespace MultiCut.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Constructor_ExposesMultiCutAppService()
    {
        Type modelType = typeof(App).Assembly.GetType("MultiCut.Model")
            ?? throw new InvalidOperationException("Model type was not found.");
        object model = Activator.CreateInstance(modelType, nonPublic: true)
            ?? throw new InvalidOperationException("Model instance could not be created.");
        object? multiCuts = modelType.GetProperty("MultiCuts")?.GetValue(model);

        Assert.NotNull(multiCuts);
        Assert.IsType<MultiCutAppService>(multiCuts);
    }
}
