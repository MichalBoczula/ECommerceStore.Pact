FROM golang:1.27.3-alpine AS comparison-tool
RUN apk add --no-cache curl
COPY scripts/install-oasdiff.sh scripts/oasdiff-version.env /install/
RUN sh /install/install-oasdiff.sh /tools

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ECommerceStore.Pact.Api/ECommerceStore.Pact.Api.csproj src/ECommerceStore.Pact.Api/packages.lock.json src/ECommerceStore.Pact.Api/
COPY src/ECommerceStore.Pact.Application/ECommerceStore.Pact.Application.csproj src/ECommerceStore.Pact.Application/packages.lock.json src/ECommerceStore.Pact.Application/
COPY src/ECommerceStore.Pact.Domain/ECommerceStore.Pact.Domain.csproj src/ECommerceStore.Pact.Domain/packages.lock.json src/ECommerceStore.Pact.Domain/
COPY src/ECommerceStore.Pact.Infrastructure/ECommerceStore.Pact.Infrastructure.csproj src/ECommerceStore.Pact.Infrastructure/packages.lock.json src/ECommerceStore.Pact.Infrastructure/
RUN dotnet restore src/ECommerceStore.Pact.Api --locked-mode
COPY src/ src/
RUN dotnet publish src/ECommerceStore.Pact.Api -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Apply Ubuntu's CVE-2026-109518 fix before switching to the unprivileged user.
# Disable retries while upgrading the vulnerable APT client (Ubuntu guidance).
RUN apt-get -o Acquire::Retries=0 update \
    && apt-get -o Acquire::Retries=0 install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=comparison-tool /tools/oasdiff /usr/local/bin/oasdiff
COPY --from=comparison-tool /tools/oasdiff-LICENSE /usr/share/licenses/oasdiff/LICENSE
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ECommerceStore.Pact.Api.dll"]
