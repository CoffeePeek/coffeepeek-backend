FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Used by Docker readiness checks; install before switching to the app user.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["CoffeePeek.ModerationService/CoffeePeek.ModerationService.csproj", "CoffeePeek.ModerationService/"]
COPY ["CoffeePeek.Moderation.Infrastructure/CoffeePeek.Moderation.Infrastructure.csproj", "CoffeePeek.Moderation.Infrastructure/"]
COPY ["CoffeePeek.Moderation.Application/CoffeePeek.Moderation.Application.csproj", "CoffeePeek.Moderation.Application/"]
COPY ["CoffeePeek.Moderation.Domain/CoffeePeek.Moderation.Domain.csproj", "CoffeePeek.Moderation.Domain/"]
COPY ["CoffeePeek.Shared.Domain/CoffeePeek.Shared.Domain.csproj", "CoffeePeek.Shared.Domain/"]
COPY ["CoffeePeek.Shared.Kernel/CoffeePeek.Shared.Kernel.csproj", "CoffeePeek.Shared.Kernel/"]
COPY ["CoffeePeek.Contract/CoffeePeek.Contract.csproj", "CoffeePeek.Contract/"]
COPY ["CoffeePeek.Shared.Auth/CoffeePeek.Shared.Auth.csproj", "CoffeePeek.Shared.Auth/"]
COPY ["CoffeePeek.Shared.Web/CoffeePeek.Shared.Web.csproj", "CoffeePeek.Shared.Web/"]
COPY ["CoffeeShop.Moderation.Persistence/CoffeeShop.Moderation.Persistence.csproj", "CoffeeShop.Moderation.Persistence/"]
COPY ["CoffeePeek.Shared.Persistence/CoffeePeek.Shared.Persistence.csproj", "CoffeePeek.Shared.Persistence/"]
COPY ["CoffePeek.ServiceDefaults/CoffePeek.ServiceDefaults.csproj", "CoffePeek.ServiceDefaults/"]
RUN dotnet restore "CoffeePeek.ModerationService/CoffeePeek.ModerationService.csproj"
COPY . .
WORKDIR "/src/CoffeePeek.ModerationService"
RUN dotnet build "./CoffeePeek.ModerationService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CoffeePeek.ModerationService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CoffeePeek.ModerationService.dll"]
