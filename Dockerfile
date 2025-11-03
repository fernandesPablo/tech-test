# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution file
COPY ProductComparison.sln ./

# Copy project files
COPY src/ProductComparison.Application/*.csproj ./src/ProductComparison.Application/
COPY src/ProductComparison.Domain/*.csproj ./src/ProductComparison.Domain/
COPY src/ProductComparison.Infrastructure/*.csproj ./src/ProductComparison.Infrastructure/
COPY src/ProductComparison.Infrastructure.IoC/*.csproj ./src/ProductComparison.Infrastructure.IoC/
COPY src/ProductComparison.CrossCutting/*.csproj ./src/ProductComparison.CrossCutting/
COPY tests/ProductComparison.UnitTests/*.csproj ./tests/ProductComparison.UnitTests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ ./src/
COPY tests/ ./tests/

# Build application
WORKDIR /src/src/ProductComparison.Application
RUN dotnet build -c Release -o /app/build

# Publish application
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=build /app/publish .

# Create directory for CSV file and logs
RUN mkdir -p /app/ProductComparison.Application/Csv
RUN mkdir -p /app/logs

# Copy CSV file (optional - can be mounted as volume)
COPY src/ProductComparison.Application/Csv/products.csv /app/ProductComparison.Application/Csv/

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Run application
ENTRYPOINT ["dotnet", "ProductComparison.Application.dll"]
