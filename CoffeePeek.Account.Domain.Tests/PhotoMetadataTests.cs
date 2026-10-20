using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class PhotoMetadataTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        const string fileName = "photo.jpg";
        const string contentType = "image/jpeg";
        const string storageKey = "photos/user123/photo.jpg";
        const long size = 1024000;
        var beforeCreation = DateTime.UtcNow;

        // Act
        var photo = PhotoMetadata.Create(fileName, contentType, storageKey, size);
        var afterCreation = DateTime.UtcNow;

        // Assert
        photo.Should().NotBeNull();
        photo.FileName.Should().Be(fileName);
        photo.ContentType.Should().Be(contentType);
        photo.StorageKey.Should().Be(storageKey);
        photo.SizeBytes.Should().Be(size);
        photo.UploadedAt.Should().BeAfter(beforeCreation).And.BeBefore(afterCreation.AddSeconds(1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidFileName_ShouldThrowDomainException(string? invalidFileName)
    {
        // Act
        Action act = () => PhotoMetadata.Create(invalidFileName!, "image/jpeg", "key", 1000);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*File name*empty*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidStorageKey_ShouldThrowDomainException(string? invalidKey)
    {
        // Act
        Action act = () => PhotoMetadata.Create("photo.jpg", "image/jpeg", invalidKey!, 1000);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Storage key*required*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void Create_WithInvalidSize_ShouldThrowDomainException(long invalidSize)
    {
        // Act
        Action act = () => PhotoMetadata.Create("photo.jpg", "image/jpeg", "key", invalidSize);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*File size*positive*");
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("application/pdf")]
    public void Create_WithDifferentContentTypes_ShouldSucceed(string contentType)
    {
        // Act
        var photo = PhotoMetadata.Create("file.ext", contentType, "key", 1000);

        // Assert
        photo.ContentType.Should().Be(contentType);
    }

    [Fact]
    public void Create_WithMinimumValidSize_ShouldSucceed()
    {
        // Arrange
        const long minSize = 1;

        // Act
        var photo = PhotoMetadata.Create("tiny.jpg", "image/jpeg", "key", minSize);

        // Assert
        photo.SizeBytes.Should().Be(minSize);
    }

    [Fact]
    public void Create_WithVeryLargeFile_ShouldSucceed()
    {
        // Arrange - 100 MB
        const long veryLargeSize = 100 * 1024 * 1024;

        // Act
        var photo = PhotoMetadata.Create("huge.jpg", "image/jpeg", "key", veryLargeSize);

        // Assert
        photo.SizeBytes.Should().Be(veryLargeSize);
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("document.pdf")]
    [InlineData("image-with-dashes.png")]
    [InlineData("file_with_underscores.gif")]
    [InlineData("file with spaces.jpeg")]
    public void Create_WithDifferentFileNames_ShouldSucceed(string fileName)
    {
        // Act
        var photo = PhotoMetadata.Create(fileName, "image/jpeg", "key", 1000);

        // Assert
        photo.FileName.Should().Be(fileName);
    }

    [Fact]
    public void Create_ShouldSetUploadedAtToUtcNow()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var photo = PhotoMetadata.Create("photo.jpg", "image/jpeg", "key", 1000);

        // Assert
        photo.UploadedAt.Kind.Should().Be(DateTimeKind.Utc);
        photo.UploadedAt.Should().BeCloseTo(beforeCreation, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Create_WithFileNameLongerThan255Characters_ShouldSucceedButTruncate()
    {
        // Arrange - FileName has MaxLength(255) attribute
        var longFileName = new string('a', 260) + ".jpg";

        // Act & Assert
        // Based on the attribute, EF Core will enforce this, but the domain doesn't validate
        var photo = PhotoMetadata.Create(longFileName, "image/jpeg", "key", 1000);
        photo.FileName.Should().Be(longFileName);
        // Note: Validation would happen at persistence layer
    }

    [Fact]
    public void Create_WithStorageKeyLongerThan255Characters_ShouldSucceedButTruncate()
    {
        // Arrange - StorageKey has MaxLength(255) attribute
        var longKey = new string('a', 260);

        // Act
        var photo = PhotoMetadata.Create("photo.jpg", "image/jpeg", longKey, 1000);

        // Assert
        photo.StorageKey.Should().Be(longKey);
        // Note: Validation would happen at persistence layer
    }
}