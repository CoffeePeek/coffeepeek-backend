namespace CoffeePeek.Account.Domain;

public static class BusinessConstants
{
    public const int MaxRefreshTokenLength = 1000;
    public const int MaxDeviceNameLength = 255;
    public const int MaxIpAddressLength = 70;

    public const int MaxActiveSessions = 5;

    public const int MaxUserNameLength = 30;
    public const int MaxUsernameChangesPerWeek = 3;
    public const int UsernameChangeWindowDays = 7;
    public const int MaxOAuthProviderLength = 50;
    public const int MaxIdProviderLength = 255;
    public const int MaxPhoneNumberLength = 20;
    public const int MaxEmailConfirmationTokenLength = 100;
    public const int MaxPasswordResetTokenLength = 100;
}
