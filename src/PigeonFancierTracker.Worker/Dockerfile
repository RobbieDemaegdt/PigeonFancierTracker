FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/PigeonFancierTracker.Core/PigeonFancierTracker.Core.csproj src/PigeonFancierTracker.Core/
COPY src/PigeonFancierTracker.Infrastructure/PigeonFancierTracker.Infrastructure.csproj src/PigeonFancierTracker.Infrastructure/
COPY src/PigeonFancierTracker.Worker/PigeonFancierTracker.Worker.csproj src/PigeonFancierTracker.Worker/
RUN dotnet restore src/PigeonFancierTracker.Worker/PigeonFancierTracker.Worker.csproj

COPY src/ src/
RUN dotnet publish src/PigeonFancierTracker.Worker/PigeonFancierTracker.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /data

ENV AppDataDirectory=/data

ENTRYPOINT ["dotnet", "PigeonFancierTracker.Worker.dll"]
