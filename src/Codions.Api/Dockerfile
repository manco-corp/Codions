FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "./"]
COPY ["src/Codions.Api/Codions.Api.csproj", "src/Codions.Api/"]
COPY ["src/Codions.Contracts/Codions.Contracts.csproj", "src/Codions.Contracts/"]
COPY ["src/Codions.Core/Codions.Core.csproj", "src/Codions.Core/"]
COPY ["src/Codions.Infrastructure/Codions.Infrastructure.csproj", "src/Codions.Infrastructure/"]
RUN dotnet restore "src/Codions.Api/Codions.Api.csproj"
COPY . .
WORKDIR "/src/src/Codions.Api"
RUN dotnet build "./Codions.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Codions.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Codions.Api.dll"]
