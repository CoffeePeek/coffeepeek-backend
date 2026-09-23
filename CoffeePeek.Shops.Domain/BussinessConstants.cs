namespace CoffeePeek.Shops.Domain;

public static class BusinessConstants
{
    #region Review
    
    public const int MinReviewRate = 1;
    public const int MaxReviewRate = 5;
    
    public const int MinReviewHeaderLength = 3;
    public const int MaxReviewHeaderLength = 100;
    public const int MinReviewCommentLength = 10;
    public const int MaxReviewCommentLength = 1000;
    
    #endregion 
    
    #region CheckIn
    
    public const int MaxCheckInNoteLength = 500;
    public const int MinPublicCheckinNoteLength = MinReviewHeaderLength;
    public const int MinHoursBetweenUserCheckIns = 3;
    public const int MaxUserCheckInsPerDay = 3;
    /// <summary>Allowed client/server clock drift when validating VisitedAt.</summary>
    public const int MaxVisitedAtClockSkewMinutes = 5;
    
    #endregion

    #region ShopContact

    public const int MaxShopContactInstagramLinkLength = 255;
    public const int MaxShopContactEmailLength = 255;
    public const int MaxShopContactSiteLinkLength = 2048;
    public const int MaxShopContactPhoneNumberLength = 20;

    #endregion

    #region CoffeeShop

    public const int ItNewEntityInDays = 30;

    public const int MaxCoffeeShopNameLength = 100;
    public const int MaxCoffeeShopDescriptionLength = 500;

    #endregion

    #region ShopTag

    public const int MaxShopTagSlugLength = 50;
    public const int MaxShopTagNameLength = 100;
    public const int MaxShopTagDescriptionLength = 500;
    public const int MaxShopTagsPerShop = 20;

    #endregion

    #region CoffeeZone

    public const int MaxCoffeeZoneNameLength = 100;
    public const int MaxCoffeeZoneDescriptionLength = 500;
    public const int MinCoffeeZoneRadiusMeters = 100;
    public const int MaxCoffeeZoneRadiusMeters = 2000;

    #endregion

    #region Catalogs

    public const int MaxCityNameLength = 50;
    public const int MaxCoffeeBeanNameLength = 100;
    public const int MaxRoasterNameLength = 100;
    public const int MaxBrewMethodNameLength = 100;

    #endregion

    #region Roaster

    public const int MaxRoasterAboutLength = 2000;
    public const int MaxRoasterContactInstagramLinkLength = MaxShopContactInstagramLinkLength;
    public const int MaxRoasterContactSiteLinkLength = MaxShopContactSiteLinkLength;
    public const int MaxRoasterPhotoFileNameLength = 255;
    public const int MaxRoasterPhotoContentTypeLength = 100;
    public const int MaxRoasterPhotoStorageKeyLength = 255;

    #endregion

    #region Visits

    public const int MaxVisitNoteLength = 500;

    #endregion

    #region Location

    public const int MaxLocationAddressLength = 500;
    public const int MaxLocationPrecision = 18;
    public const int MaxLocationScale = 10;
    public const int MaxLocationLatitude = 90;
    public const int MaxLocationLongitude = 180;

    #endregion
    
    public const int MaxEquipmentCategoryNameLength = 50;

    #region Menu

    public const int MaxCoffeeDrinkSlugLength = 50;
    public const int MaxCoffeeDrinkNameLength = 100;
    public const int MaxCoffeeDrinkAliasesLength = 500;
    public const int MaxMenuCurrencyLength = 8;
    public const int MaxMenuParseErrorLength = 1000;
    public const int MaxMenuPhotoFileNameLength = 255;
    public const int MaxMenuPhotoContentTypeLength = 100;
    public const int MaxMenuPhotoStorageKeyLength = 500;
    public const int MaxMenuPhotosPerParse = 4;
    public const string DefaultMenuCurrency = "BYN";
    public const decimal MenuCheapBelow = 7.00m;
    public const decimal MenuExpensiveAbove = 9.00m;

    #endregion

    #region AppDistribution

    public const int MaxDownloadUrlLength = 2048;
    public const int MaxAndroidReleaseVersionLength = 32;
    public const int MaxAndroidReleaseFileNameLength = 255;
    public const int Sha256HashLength = 64;
    public const int MaxAuditActionLength = 100;
    public const int MaxAuditValueLength = 4000;
    public const int MaxRedirectChannelLength = 32;
    public const int MaxRedirectUserAgentLength = 500;
    public const int MaxRedirectRefererLength = 2048;
    public const int MaxRedirectCountryLength = 2;

    #endregion
}
