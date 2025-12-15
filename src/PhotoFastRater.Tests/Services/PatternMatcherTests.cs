using Xunit;
using FluentAssertions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Services;

namespace PhotoFastRater.Tests.Services;

public class PatternMatcherTests
{
    [Theory]
    [InlineData("*/temp/*", "C:\\Photos\\temp\\image.jpg", true)]
    [InlineData("*/temp/*", "C:\\Photos\\backup\\image.jpg", false)]
    [InlineData("*/backup/*", "D:\\Photos\\backup\\2024\\image.jpg", true)]
    [InlineData("*/.git/*", "C:\\Projects\\.git\\config", true)]
    public void IsMatch_WildcardPattern_ShouldMatchCorrectly(string pattern, string filePath, bool expectedMatch)
    {
        // Arrange
        var exclusionPattern = new FolderExclusionPattern
        {
            Pattern = pattern,
            Type = PatternType.Wildcard,
            IsEnabled = true
        };

        // Act
        var result = PatternMatcher.IsMatch(filePath, exclusionPattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData("^.*\\\\temp\\\\.*$", "C:\\Photos\\temp\\image.jpg", true)]
    [InlineData("^.*\\\\temp\\\\.*$", "C:\\Photos\\backup\\image.jpg", false)]
    [InlineData("^.*\\.(tmp|bak)$", "C:\\Photos\\image.tmp", true)]
    [InlineData("^.*\\.(tmp|bak)$", "C:\\Photos\\image.jpg", false)]
    public void IsMatch_RegexPattern_ShouldMatchCorrectly(string pattern, string filePath, bool expectedMatch)
    {
        // Arrange
        var exclusionPattern = new FolderExclusionPattern
        {
            Pattern = pattern,
            Type = PatternType.Regex,
            IsEnabled = true
        };

        // Act
        var result = PatternMatcher.IsMatch(filePath, exclusionPattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Theory]
    [InlineData("C:\\Photos\\temp", "C:\\Photos\\temp\\image.jpg", true)]
    [InlineData("C:\\Photos\\temp", "C:\\Photos\\backup\\image.jpg", false)]
    [InlineData("D:\\Backup", "D:\\Backup\\2024\\image.jpg", true)]
    public void IsMatch_ExactPattern_ShouldMatchCorrectly(string pattern, string filePath, bool expectedMatch)
    {
        // Arrange
        var exclusionPattern = new FolderExclusionPattern
        {
            Pattern = pattern,
            Type = PatternType.Exact,
            IsEnabled = true
        };

        // Act
        var result = PatternMatcher.IsMatch(filePath, exclusionPattern);

        // Assert
        result.Should().Be(expectedMatch);
    }

    [Fact]
    public void IsMatch_DisabledPattern_ShouldReturnFalse()
    {
        // Arrange
        var exclusionPattern = new FolderExclusionPattern
        {
            Pattern = "*/temp/*",
            Type = PatternType.Wildcard,
            IsEnabled = false
        };
        var filePath = "C:\\Photos\\temp\\image.jpg";

        // Act
        var result = PatternMatcher.IsMatch(filePath, exclusionPattern);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsMatchAny_MultiplePatterns_ShouldReturnTrueIfAnyMatches()
    {
        // Arrange
        var patterns = new List<FolderExclusionPattern>
        {
            new() { Pattern = "*/temp/*", Type = PatternType.Wildcard, IsEnabled = true },
            new() { Pattern = "*/backup/*", Type = PatternType.Wildcard, IsEnabled = true }
        };
        var filePath = "C:\\Photos\\backup\\image.jpg";

        // Act
        var result = PatternMatcher.IsMatchAny(filePath, patterns);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsMatchAny_NoPatterns_ShouldReturnFalse()
    {
        // Arrange
        var patterns = new List<FolderExclusionPattern>();
        var filePath = "C:\\Photos\\image.jpg";

        // Act
        var result = PatternMatcher.IsMatchAny(filePath, patterns);

        // Assert
        result.Should().BeFalse();
    }
}
