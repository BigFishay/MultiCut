using MultiCut.Services;

namespace MultiCut.Tests;

public sealed class OperationResultTests
{
    [Fact]
    public void Succeeded_CreatesSuccessfulResultWithMessage()
    {
        OperationResult result = OperationResult.Succeeded("Done.");

        Assert.True(result.Success);
        Assert.Equal("Done.", result.Message);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failed_UsesMessageWhenNoDetailedErrorsAreProvided()
    {
        OperationResult result = OperationResult.Failed("Something failed.", "", " ");

        Assert.False(result.Success);
        Assert.Equal("Something failed.", result.Message);
        Assert.Equal(["Something failed."], result.Errors);
    }

    [Fact]
    public void Failed_TrimsDetailedErrors()
    {
        OperationResult result = OperationResult.Failed("Something failed.", " first ", "second");

        Assert.False(result.Success);
        Assert.Equal(["first", "second"], result.Errors);
    }

    [Fact]
    public void GenericSucceeded_CarriesValue()
    {
        OperationResult<int> result = OperationResult<int>.Succeeded(42, "Loaded.");

        Assert.True(result.Success);
        Assert.Equal(42, result.Value);
        Assert.Equal("Loaded.", result.Message);
    }

    [Fact]
    public void GenericFailed_UsesExceptionMessage()
    {
        var exception = new InvalidOperationException("Bad state.");

        OperationResult<int> result = OperationResult<int>.Failed("Unable to load.", exception);

        Assert.False(result.Success);
        Assert.Equal(default, result.Value);
        Assert.Equal(["Bad state."], result.Errors);
    }
}
