FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props /src/
COPY Directory.Packages.props /src/
COPY src/Codions.Contracts/Codions.Contracts.csproj Codions.Contracts/
COPY src/Codions.BotHarness/Codions.BotHarness.csproj Codions.BotHarness/
RUN dotnet restore Codions.BotHarness/Codions.BotHarness.csproj

COPY src/Codions.Contracts/ Codions.Contracts/
COPY src/Codions.BotHarness/ Codions.BotHarness/
RUN dotnet publish Codions.BotHarness/Codions.BotHarness.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    ripgrep \
    curl \
    ca-certificates \
    gnupg \
    python3 \
    python3-pip \
    python3-venv \
    golang-go \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -s /bin/bash botuser

WORKDIR /app
COPY --from=build /app/publish .

RUN chown -R botuser:botuser /app
USER botuser

WORKDIR /workspace
ENTRYPOINT ["dotnet", "/app/Codions.BotHarness.dll"]
