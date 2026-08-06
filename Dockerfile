# Multi-stage build: the SDK never ships to production, only the runtime does.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first so `restore` is cached until dependencies actually change.
COPY IMS.sln ./
COPY src/IMS.Domain/IMS.Domain.csproj              src/IMS.Domain/
COPY src/IMS.Application/IMS.Application.csproj    src/IMS.Application/
COPY src/IMS.Infrastructure/IMS.Infrastructure.csproj src/IMS.Infrastructure/
COPY src/IMS.Api/IMS.Api.csproj                    src/IMS.Api/
COPY tests/IMS.Tests/IMS.Tests.csproj              tests/IMS.Tests/
RUN dotnet restore

COPY . .

# Fail the image build if the tests fail: a broken build should never become an image.
RUN dotnet test tests/IMS.Tests --no-restore -c Release --verbosity quiet

RUN dotnet publish src/IMS.Api/IMS.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root user. The aspnet image ships an "app" user for exactly this.
USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "IMS.Api.dll"]
