FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "./"]
COPY ["src/Codions.ChatAdapter/Codions.ChatAdapter.csproj", "src/Codions.ChatAdapter/"]
COPY ["src/Codions.Contracts/Codions.Contracts.csproj", "src/Codions.Contracts/"]
RUN dotnet restore "src/Codions.ChatAdapter/Codions.ChatAdapter.csproj"
COPY . .
WORKDIR "/src/src/Codions.ChatAdapter"
RUN dotnet build "./Codions.ChatAdapter.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Codions.ChatAdapter.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Codions.ChatAdapter.dll"]
