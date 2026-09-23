FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /app

# Restore dependencies first for better layer caching.
COPY proxy.csproj ./
RUN dotnet restore

# Build and publish the application.
COPY . .
RUN dotnet publish -c Release -o /out

# Runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=builder /out ./

EXPOSE 8000

ENTRYPOINT ["dotnet", "proxy-service.dll"]
