using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public sealed class WindowPlacementStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SpanishReaderPlacementTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "window-placement.json");

    [Fact]
    public void FirstLaunchKeepsDefaultPlacementWithoutCreatingSettings()
    {
        Assert.Null(new WindowPlacementStore(FilePath).Load());
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalBoundsAndMaximizedStateSurviveRestartAndReplacement(bool maximized)
    {
        var store = new WindowPlacementStore(FilePath);
        store.Save(new WindowPlacement(20, 30, 900, 700, !maximized));
        // Negative coordinates are valid for monitors left of/above the primary.
        var expected = new WindowPlacement(-1400, -300, -200, 600, maximized);
        store.Save(expected);

        Assert.Equal(expected, new WindowPlacementStore(FilePath).Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"left\":100,\"top\":10,\"right\":50,\"bottom\":900}")]
    [InlineData("{\"left\":-2147483648,\"top\":0,\"right\":2147483647,\"bottom\":900}")]
    public void CorruptOrInvalidSettingsKeepDefaultPlacement(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        Assert.Null(new WindowPlacementStore(FilePath).Load());
    }

    [Fact]
    public void InvalidBoundsCannotReplaceLastSavedPlacement()
    {
        var store = new WindowPlacementStore(FilePath);
        var expected = new WindowPlacement(10, 20, 900, 700, true);
        store.Save(expected);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new WindowPlacement(10, 20, 10, 700, false)));
        Assert.Equal(expected, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
