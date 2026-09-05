using FluentAssertions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using Xunit;

namespace PhotoFastRater.Tests.Architecture;

public sealed class CoreBoundaryTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferenceInfrastructureFrameworks()
    {
        var references = typeof(Photo).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        references.Should().NotContain(name => name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        references.Should().NotContain(name => name!.StartsWith("SixLabors", StringComparison.Ordinal));
        references.Should().NotContain("MetadataExtractor");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(6)]
    public void RatingState_RejectsValuesOutsideXmpRange(int value)
    {
        var action = () => new RatingState(value, false, false);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RatingState_RejectValueAlsoSetsRejectedFlag()
    {
        var state = new RatingState(-1, false, false);
        state.IsRejected.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1, 0, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0.5, 0.5, 0.6, 0.5)]
    public void CropRect_RejectsEmptyOrOutOfBoundsValues(double x, double y, double width, double height)
    {
        var action = () => new CropRect(x, y, width, height);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
