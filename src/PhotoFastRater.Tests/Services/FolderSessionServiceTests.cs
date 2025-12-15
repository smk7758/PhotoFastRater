using System.IO;
using Xunit;
using FluentAssertions;
using Moq;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Services;

namespace PhotoFastRater.Tests.Services;

public class FolderSessionServiceTests
{
    private readonly Mock<ExifService> _mockExifService;
    private readonly FolderSessionService _service;
    private readonly string _testFolderPath;

    public FolderSessionServiceTests()
    {
        _mockExifService = new Mock<ExifService>();
        _service = new FolderSessionService(_mockExifService.Object);
        _testFolderPath = Path.Combine(Path.GetTempPath(), "TestPhotos");

        // テストフォルダを作成
        if (!Directory.Exists(_testFolderPath))
        {
            Directory.CreateDirectory(_testFolderPath);
        }
    }

    [Fact]
    public async Task CreateSessionAsync_NewFolder_ShouldCreateNewSession()
    {
        // Act
        var session = await _service.CreateSessionAsync(_testFolderPath);

        // Assert
        session.Should().NotBeNull();
        session.SessionId.Should().NotBeEmpty();
        session.FolderPath.Should().Be(_testFolderPath);
    }

    [Fact]
    public async Task SaveSessionAsync_ValidSession_ShouldSaveSuccessfully()
    {
        // Arrange
        var session = new FolderSession
        {
            SessionId = Guid.NewGuid(),
            FolderPath = _testFolderPath,
            CreatedDate = DateTime.Now,
            Photos = new List<FolderSessionPhoto>()
        };

        // Act & Assert - should not throw
        await _service.SaveSessionAsync(session);
    }

    [Fact]
    public async Task LoadSessionAsync_ExistingSession_ShouldLoadCorrectly()
    {
        // Arrange
        var originalSession = new FolderSession
        {
            SessionId = Guid.NewGuid(),
            FolderPath = _testFolderPath,
            CreatedDate = DateTime.Now,
            Photos = new List<FolderSessionPhoto>
            {
                new()
                {
                    FilePath = "test.jpg",
                    FileName = "test.jpg",
                    Rating = 4,
                    IsFavorite = true
                }
            }
        };

        await _service.SaveSessionAsync(originalSession);

        // Act
        var loadedSession = await _service.LoadSessionAsync(_testFolderPath);

        // Assert
        loadedSession.Should().NotBeNull();
        loadedSession!.SessionId.Should().Be(originalSession.SessionId);
        loadedSession.Photos.Should().HaveCount(1);
        loadedSession.Photos[0].Rating.Should().Be(4);
        loadedSession.Photos[0].IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task LoadSessionAsync_NonExistingSession_ShouldReturnNull()
    {
        // Arrange
        var nonExistingPath = Path.Combine(Path.GetTempPath(), "NonExisting" + Guid.NewGuid());

        // Act
        var session = await _service.LoadSessionAsync(nonExistingPath);

        // Assert
        session.Should().BeNull();
    }

    [Fact]
    public void UpdatePhotoRating_ShouldUpdateCorrectly()
    {
        // Arrange
        var session = new FolderSession
        {
            SessionId = Guid.NewGuid(),
            FolderPath = _testFolderPath,
            Photos = new List<FolderSessionPhoto>
            {
                new()
                {
                    FilePath = "test.jpg",
                    FileName = "test.jpg",
                    Rating = 0,
                    IsFavorite = false,
                    IsRejected = false
                }
            }
        };

        // Act
        _service.UpdatePhotoRating(session, "test.jpg", 5, true, false);

        // Assert
        var photo = session.Photos[0];
        photo.Rating.Should().Be(5);
        photo.IsFavorite.Should().BeTrue();
        photo.IsRejected.Should().BeFalse();
    }
}
