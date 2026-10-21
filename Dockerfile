# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the project files first so the restore layer is cached until dependencies change.
COPY src/Sunset.Domain/Sunset.Domain.csproj src/Sunset.Domain/
COPY src/Sunset.Application/Sunset.Application.csproj src/Sunset.Application/
COPY src/Sunset.Infrastructure/Sunset.Infrastructure.csproj src/Sunset.Infrastructure/
COPY src/Sunset.API/Sunset.API.csproj src/Sunset.API/
RUN dotnet restore src/Sunset.API/Sunset.API.csproj

COPY src/ src/
RUN dotnet publish src/Sunset.API/Sunset.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Serilog's file sink writes to ./logs; the non-root "app" user needs to own it.
RUN mkdir -p /app/logs && chown -R app:app /app/logs
COPY --from=build --chown=app:app /app/publish .
USER app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Render injects $PORT (default 10000); listen on it on all interfaces. Shell form so $PORT expands.
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet Sunset.API.dll"]
