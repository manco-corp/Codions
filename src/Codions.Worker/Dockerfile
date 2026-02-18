FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Codions.Worker/Codions.Worker.csproj", "src/Codions.Worker/"]
COPY ["src/Codions.Contracts/Codions.Contracts.csproj", "src/Codions.Contracts/"]
COPY ["src/Codions.Core/Codions.Core.csproj", "src/Codions.Core/"]
COPY ["src/Codions.Infrastructure/Codions.Infrastructure.csproj", "src/Codions.Infrastructure/"]
RUN dotnet restore "src/Codions.Worker/Codions.Worker.csproj"
COPY . .
WORKDIR "/src/src/Codions.Worker"
RUN dotnet build "./Codions.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Codions.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Codions.Worker.dll"]
