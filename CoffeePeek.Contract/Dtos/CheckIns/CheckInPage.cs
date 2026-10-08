using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CheckIns;

public record CheckInPage(UnifiedCheckInDto[] Items, int TotalCount);
