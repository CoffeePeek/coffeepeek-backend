FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Used by Docker readiness checks; install before switching to the app user.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
# Listen on IPv4 and IPv6. IPv6-only ([::]:8080) is unreachable from Docker IPv4 DNS.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["CoffeePeek.ShopsService/CoffeePeek.ShopsService.csproj", "CoffeePeek.ShopsService/"]
COPY ["CoffeePeek.Shops.Infrastructure/CoffeePeek.Shops.Infrastructure.csproj", "CoffeePeek.Shops.Infrastructure/"]
COPY ["CoffeePeek.Shops.Application/CoffeePeek.Shops.Application.csproj", "CoffeePeek.Shops.Application/"]
COPY ["CoffeePeek.Contract/CoffeePeek.Contract.csproj", "CoffeePeek.Contract/"]
COPY ["CoffeePeek.Shared.Kernel/CoffeePeek.Shared.Kernel.csproj", "CoffeePeek.Shared.Kernel/"]
COPY ["CoffeePeek.Shops.Domain/CoffeePeek.Shops.Domain.csproj", "CoffeePeek.Shops.Domain/"]
COPY ["CoffeePeek.Shared.Domain/CoffeePeek.Shared.Domain.csproj", "CoffeePeek.Shared.Domain/"]
COPY ["CoffeePeek.Shops.Persistance/CoffeePeek.Shops.Persistance.csproj", "CoffeePeek.Shops.Persistance/"]
COPY ["CoffeePeek.Shared.Persistence/CoffeePeek.Shared.Persistence.csproj", "CoffeePeek.Shared.Persistence/"]
COPY ["CoffeePeek.Shared.Auth/CoffeePeek.Shared.Auth.csproj", "CoffeePeek.Shared.Auth/"]
COPY ["CoffeePeek.Shared.Web/CoffeePeek.Shared.Web.csproj", "CoffeePeek.Shared.Web/"]
COPY ["CoffePeek.ServiceDefaults/CoffePeek.ServiceDefaults.csproj", "CoffePeek.ServiceDefaults/"]
RUN dotnet restore "CoffeePeek.ShopsService/CoffeePeek.ShopsService.csproj"
COPY . .
WORKDIR "/src/CoffeePeek.ShopsService"
RUN dotnet build "./CoffeePeek.ShopsService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CoffeePeek.ShopsService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CoffeePeek.ShopsService.dll"]
