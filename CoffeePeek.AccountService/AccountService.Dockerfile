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
COPY ["CoffeePeek.AccountService/CoffeePeek.AccountService.csproj", "CoffeePeek.AccountService/"]
COPY ["CoffeePeek.Account.Infrastructure/CoffeePeek.Account.Infrastructure.csproj", "CoffeePeek.Account.Infrastructure/"]
COPY ["CoffeePeek.Account.Application/CoffeePeek.Account.Application.csproj", "CoffeePeek.Account.Application/"]
COPY ["CoffeePeek.Account.Domain/CoffeePeek.Account.Domain.csproj", "CoffeePeek.Account.Domain/"]
COPY ["CoffeePeek.Shared.Domain/CoffeePeek.Shared.Domain.csproj", "CoffeePeek.Shared.Domain/"]
COPY ["CoffeePeek.Shared.Kernel/CoffeePeek.Shared.Kernel.csproj", "CoffeePeek.Shared.Kernel/"]
COPY ["CoffeePeek.Shared.Auth/CoffeePeek.Shared.Auth.csproj", "CoffeePeek.Shared.Auth/"]
COPY ["CoffeePeek.Shared.Web/CoffeePeek.Shared.Web.csproj", "CoffeePeek.Shared.Web/"]
COPY ["CoffeePeek.Contract/CoffeePeek.Contract.csproj", "CoffeePeek.Contract/"]
COPY ["CoffeePeek.Account.Persistence/CoffeePeek.Account.Persistence.csproj", "CoffeePeek.Account.Persistence/"]
COPY ["CoffeePeek.Shared.Persistence/CoffeePeek.Shared.Persistence.csproj", "CoffeePeek.Shared.Persistence/"]
COPY ["CoffePeek.ServiceDefaults/CoffePeek.ServiceDefaults.csproj", "CoffePeek.ServiceDefaults/"]
RUN dotnet restore "CoffeePeek.AccountService/CoffeePeek.AccountService.csproj"
COPY . .
WORKDIR "/src/CoffeePeek.AccountService"
RUN dotnet build "./CoffeePeek.AccountService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CoffeePeek.AccountService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CoffeePeek.AccountService.dll"]
