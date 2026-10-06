using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public sealed class PublicAddressException(string message, int statusCode)
    : BaseException(message, "public_address_error", statusCode);
