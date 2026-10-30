using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public sealed class ThemePreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SpanishReaderThemeTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "theme.txt");

    [Fact]
    public void FirstLaunchUsesSystemWithoutWritingAnOverride()
    {
        Assert.Equal(ThemePreference.System, new ThemePreferenceStore(FilePath).Load());
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.System)]
    public void ChoiceSurvivesNewStoreAndCanBeReplaced(ThemePreference preference)
    {
        var store = new ThemePreferenceStore(FilePath);
        store.Save(ThemePreference.Dark);
        store.Save(preference);
        Assert.Equal(preference, new ThemePreferenceStore(FilePath).Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("999")]
    public void InvalidSettingsFallBackToSystem(string value)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, value);
        Assert.Equal(ThemePreference.System, new ThemePreferenceStore(FilePath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
