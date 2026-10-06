using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.PublicAddresses;

// Values are persisted in PublicAddressAudit.Kind (CK_PublicAddressAudit_Kind); never renumber.
public enum PublicAddressKind { CoffeeShop = 1, Roaster = 2, City = 3, CoffeeZone = 4, CoffeeBean = 5, Equipment = 6, BrewMethod = 7, ShopTag = 8, CoffeeProduct = 9 }
