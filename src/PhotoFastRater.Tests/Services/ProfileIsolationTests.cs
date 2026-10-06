using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using FluentAssertions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Services;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;
using Xunit;

namespace PhotoFastRater.Tests.Services;

/// <summary>Protects real user preferences and preserves legacy formats when profile locations change.</summary>
public sealed class ProfileIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void StartupOptionsAcceptEitherOrderAndKeepEveryLocationInProfile()
    {
        var first = StartupOptions.Parse(["--folder", _root, "--data-dir", _root]);
        var second = StartupOptions.Parse(["--data-dir", _root, "--folder", _root]);
        first.FolderPath.Should().Be(second.FolderPath);
        first.FolderMode.Should().BeTrue();
        var paths = first.Paths;
        new[] { paths.Database, paths.Settings, paths.FolderSettings, paths.Shortcuts, paths.Sessions, paths.LegacySessionRoot, paths.Cache, paths.Logs }
            .Should().OnlyContain(path => Path.GetRelativePath(_root, path).StartsWith("..", StringComparison.Ordinal) == false);
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--unknown")]
    [InlineData("--data-dir", "relative")]
    [InlineData("--folder", "--folder")]
    public void InvalidArgumentsFailBeforeOpeningFiles(params string[] arguments) =>
        FluentActions.Invoking(() => StartupOptions.Parse(arguments)).Should().Throw<ArgumentException>();

    [Fact]
    public void FolderPreferencesRoundTripWithoutResettingOtherDisplayOptions()
    {
        var paths = new ApplicationPaths(_root);
        var original = new FolderModeSettingsViewModel(paths)
        {
            DefaultThumbnailSize = 320,
            NameLabelBelow = true,
            UniformPhotoSize = true,
            ShowOriginalImages = true,
            ShowExifInItem = true,
            ExifShowCamera = true
        };
        original.Save();
        var loaded = new FolderModeSettingsViewModel(paths);
        loaded.Load();
        loaded.DefaultThumbnailSize.Should().Be(320);
        loaded.NameLabelBelow.Should().BeTrue();
        loaded.UniformPhotoSize.Should().BeTrue();
        loaded.ShowOriginalImages.Should().BeTrue();
        loaded.ExifShowCamera.Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"defaultThumbnailSize\":320,\"showExifInItem\":\"wrong\"}")]
    [InlineData("{\"defaultThumbnailSize\":-1}")]
    [InlineData("{broken")]
    public void MalformedFolderPreferencesDoNotPartiallyChangeDefaults(string json)
    {
        Directory.CreateDirectory(_root);
        var model = new FolderModeSettingsViewModel(new ApplicationPaths(_root));
        File.WriteAllText(model.SettingsPath, json);
        model.Load();
        model.DefaultThumbnailSize.Should().Be(200);
        model.ShowExifInItem.Should().BeFalse();
    }

    [Fact]
    public void FailedFolderSavePreservesPreviousSnapshot()
    {
        var model = new FolderModeSettingsViewModel(new ApplicationPaths(_root));
        model.Save();
        var previous = File.ReadAllBytes(model.SettingsPath);
        model.DefaultThumbnailSize = -1;
        FluentActions.Invoking(model.Save).Should().Throw<ArgumentException>();
        File.ReadAllBytes(model.SettingsPath).Should().Equal(previous);
    }

    [Fact]
    public void CustomShortcutsRemainIsolatedAndPersist()
    {
        var paths = new ApplicationPaths(_root);
        var store = new ShortcutService(paths);
        var entries = store.Load();
        entries.Single(entry => entry.CommandName == "ToggleReject").Key = Key.X;
        store.Save(entries);
        new ShortcutService(paths).Load().Single(entry => entry.CommandName == "ToggleReject").Key.Should().Be(Key.X);
        File.Exists(paths.Shortcuts).Should().BeTrue();
    }

    [Fact]
    public async Task LegacySessionImportPreservesOriginalAndIsIdempotent()
    {
        var paths = new ApplicationPaths(_root);
        var folder = Path.Combine(_root, "Photos");
#pragma warning disable CA5351 // The legacy filename intentionally reproduces the released MD5 scheme.
        var hash = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())))[..16];
#pragma warning restore CA5351
        var legacy = Path.Combine(paths.LegacySessionRoot, hash, "session.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var session = new FolderSession
        {
            SessionId = Guid.NewGuid(),
            FolderPath = folder,
            Photos = [new FolderSessionPhoto { FileName = "old.jpg", FilePath = Path.Combine(folder, "old.jpg"), Rating = 4, IsFavorite = true }]
        };
        File.WriteAllText(legacy, JsonSerializer.Serialize(session));
        var bytes = File.ReadAllBytes(legacy);
        var service = new FolderSessionService(new ExifService(), paths.Sessions, paths.LegacySessionRoot);
        var first = await service.LoadSessionAsync(folder);
        var second = await service.LoadSessionAsync(folder);
        first!.SessionId.Should().Be(second!.SessionId);
        second.Photos.Should().ContainSingle().Which.Rating.Should().Be(4);
        File.ReadAllBytes(legacy).Should().Equal(bytes);
        Directory.GetFiles(paths.Sessions, "session.json", SearchOption.AllDirectories).Should().ContainSingle();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            var garbage = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", "_GARBAGE");
            Directory.CreateDirectory(garbage);
            Directory.Move(_root, Path.Combine(garbage, Path.GetFileName(_root)));
        }
        GC.SuppressFinalize(this);
    }
}
