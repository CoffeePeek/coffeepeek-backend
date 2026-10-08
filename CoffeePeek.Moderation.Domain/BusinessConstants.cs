namespace CoffeePeek.Moderation.Domain;

public static class BusinessConstants
{
    public const int MinReviewRate = 1;
    public const int MaxReviewRate = 5;

    public const int MaxCheckInTextLength = 1000;
    public const int MinRejectReasonCommentLength = 2;
    public const int MaxRejectReasonCommentLength = 1000;

    public const int MinShopIssueReportDescriptionLength = 2;
    public const int MaxShopIssueReportDescriptionLength = 1000;

    /// <summary>Matches the catalog Shops.Name column so an approved shop always fits.</summary>
    public const int MaxShopNameLength = 100;

    #region ShopContact

    public const int MaxShopContactInstagramLinkLength = 255;
    public const int MaxShopContactEmailLength = 255;
    public const int MaxShopContactSiteLinkLength = 2048;
    public const int MaxShopContactPhoneNumberLength = 20;

    #endregion

    #region Roaster

    public const int MaxRoasterNameLength = 100;
    public const int MaxRoasterAboutLength = 2000;

    #endregion
}
