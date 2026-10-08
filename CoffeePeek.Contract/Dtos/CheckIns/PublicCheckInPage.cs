using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CheckIns;

public record PublicCheckInPage(UnifiedCheckInDto[] Items, string? NextCursor);
