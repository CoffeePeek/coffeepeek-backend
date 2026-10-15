namespace CoffeePeek.Contract.Dtos.ProfileActivity;

public sealed record ProfileActivityDto(
    int Year,
    int Month,
    string TimeZone,
    int ActiveDays,
    int TotalCheckIns,
    ProfileActivityDayDto[] Days);
