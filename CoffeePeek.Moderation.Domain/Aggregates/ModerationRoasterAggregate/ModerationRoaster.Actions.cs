using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public sealed partial class ModerationRoaster
{
    public static ModerationRoaster Create(string name, Guid? userId, string? about)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required");

        if (name.Trim().Length > BusinessConstants.MaxRoasterNameLength)
            throw new DomainException(
                $"Name cannot be longer than {BusinessConstants.MaxRoasterNameLength} characters.");

        if (about != null && about.Trim().Length > BusinessConstants.MaxRoasterAboutLength)
            throw new DomainException(
                $"About cannot be longer than {BusinessConstants.MaxRoasterAboutLength} characters.");

        return new ModerationRoaster
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            About = about?.Trim(),
            UserId = userId,
            ModerationStatus = ModerationStatus.Pending
        };
    }

    public void SetLocation(Guid cityId, ModerationLocation location)
    {
        CityId = cityId;
        Location = location;
    }

    public void UpdateContact(string? instagramLink, string? siteLink)
    {
        Contact = ModerationRoasterContact.Create(instagramLink, siteLink);
    }

    public void AddPhoto(string fileName, string contentType, string storageKey, long length)
    {
        var photo = ModerationRoasterPhoto.Create(fileName, contentType, storageKey, length, UserId, Id);
        _photos.Add(photo);
    }

    public bool Approve()
    {
        if (ModerationStatus == ModerationStatus.Approved)
            return false;

        if (Location is not null && !Location.IsAddressValidated)
            throw new DomainException("Cannot approve roaster with unvalidated address.");

        ModerationStatus = ModerationStatus.Approved;
        return true;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Reject reason is required.");

        if (reason.Length is < BusinessConstants.MinRejectReasonCommentLength or > BusinessConstants.MaxRejectReasonCommentLength)
            throw new DomainException(
                $"{nameof(reason)} must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");

        ModerationStatus = ModerationStatus.Rejected;
        RejectedReason = reason;
    }
}
