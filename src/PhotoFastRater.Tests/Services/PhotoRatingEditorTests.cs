using FluentAssertions;
using Moq;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;
using Xunit;

namespace PhotoFastRater.Tests.Services;

/// <summary>Guards against optimistic UI success and lost toggles when input arrives faster than disk writes.</summary>
public sealed class PhotoRatingEditorTests
{
    [Fact]
    public async Task FailedCommitLeavesDisplayAndExportModelUnchanged()
    {
        var coordinator = new Mock<IRatingCoordinator>();
        coordinator.Setup(service => service.SetRatingAsync(It.IsAny<int>(), It.IsAny<RatingState>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk failed"));
        using var editor = new PhotoRatingEditor(coordinator.Object);
        var photo = new PhotoViewModel(new Photo { Id = 1, Rating = 2 });
        await FluentActions.Awaiting(() => editor.SetStarsAsync(photo, 5)).Should().ThrowAsync<InvalidOperationException>();
        photo.Rating.Should().Be(2); photo.GetModel().Rating.Should().Be(2);
    }

    [Fact]
    public async Task RapidTogglesSerializeFromLastCommittedState()
    {
        var committed = new List<RatingState>();
        var coordinator = new Mock<IRatingCoordinator>();
        coordinator.Setup(service => service.SetRatingAsync(It.IsAny<int>(), It.IsAny<RatingState>(), It.IsAny<CancellationToken>()))
            .Returns(async (int id, RatingState state, CancellationToken token) => { await Task.Delay(10, token); committed.Add(state); });
        using var editor = new PhotoRatingEditor(coordinator.Object);
        var photo = new PhotoViewModel(new Photo { Id = 1 });
        await Task.WhenAll(editor.ToggleFavoriteAsync(photo), editor.ToggleFavoriteAsync(photo));
        committed.Select(state => state.IsFavorite).Should().Equal(true, false);
        photo.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task RejectImportedMinusOneCanBeCleared()
    {
        using var editor = new PhotoRatingEditor(Mock.Of<IRatingCoordinator>());
        var photo = new PhotoViewModel(new Photo { Id = 1, Rating = -1, IsRejected = true });
        await editor.ToggleRejectedAsync(photo);
        photo.Rating.Should().Be(0); photo.IsRejected.Should().BeFalse();
    }
}
