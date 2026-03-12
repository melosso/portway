# syntax=docker/dockerfile:1.4
# Enable BuildKit features for better performance

# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first to leverage Docker layer caching for dependencies
COPY Source/PortwayApi/PortwayApi.csproj Source/PortwayApi/
COPY Source/Tools/TokenGenerator/TokenGenerator.csproj Source/Tools/TokenGenerator/
COPY Source/Directory.Build.props Source/
COPY Source/Directory.Packages.props Source/
COPY Source/global.json Source/

# Restore dependencies as distinct layers
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "Source/PortwayApi/PortwayApi.csproj" && \
    dotnet restore "Source/Tools/TokenGenerator/TokenGenerator.csproj"
    
# Copy the rest of the source code
COPY . .

# Build projects
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet build "Source/PortwayApi/PortwayApi.csproj" -c Release -o /app/build && \
    dotnet build "Source/Tools/TokenGenerator/TokenGenerator.csproj" -c Release -o /app/tools/build

# Publish the applications
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "Source/PortwayApi/PortwayApi.csproj" -c Release -o /app/publish /p:UseAppHost=false && \
    dotnet publish "Source/Tools/TokenGenerator/TokenGenerator.csproj" -c Release -o /app/tools/publish /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Install SQLite and curl for healthchecks
RUN apt-get update && apt-get install -y \
    sqlite3 \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create necessary directories
RUN mkdir -p /app/endpoints/SQL/Product && \
    mkdir -p /app/tokens && \
    mkdir -p /app/log && \
    mkdir -p /app/environments/prod

# Set environment variables
# For future reference, ASPNETCORE_HTTP_PORTS=8080 is already set by the base image (dotnet/aspnet:8.0+). Can be removed when deemed stable.
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy published output and tools
COPY --from=build /app/publish .
COPY --from=build /app/tools/publish /app/tools

# Copy configuration files
COPY Source/PortwayApi/environments/settings.json /app/environments/
COPY Source/PortwayApi/environments/500/settings.json /app/environments/prod/

# Fix environment names inside the JSON files
RUN sed -i 's/500/prod/g' /app/environments/prod/settings.json && \
    sed -i 's/500/prod/g' /app/environments/settings.json && \
    sed -i 's/, "700"//g' /app/environments/settings.json && \
    sed -i 's/, "Synergy"//g' /app/environments/settings.json

# Add only SQL Product endpoint definitions
COPY Source/PortwayApi/endpoints/SQL/Product/** /app/endpoints/SQL/Product/

# Set proper permissions
RUN chmod -R 755 /app

# Configure health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Expose application port
EXPOSE 8080

# Create startup script that generates default token if needed
RUN echo '#!/bin/sh\n\
if [ ! -f /app/tokens/docker-default.txt ]; then\n\
  echo "No tokens found. Generating default admin token..."\n\
  dotnet /app/tools/TokenGenerator.dll --docker docker-default -s "*" -e "prod" --description "Auto-generated admin token" || echo "Token generation failed, continuing..."\n\
  echo "Token generated at /app/tokens/docker-default.txt"\n\
fi\n\
exec dotnet PortwayApi.dll "$@"' > /app/start.sh && chmod +x /app/start.sh

# Define entrypoint
ENTRYPOINT ["/app/start.sh"]