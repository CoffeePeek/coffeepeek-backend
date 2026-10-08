using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public record DeleteRoasterCommand([property: JsonIgnore] Guid Id);
