FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ECommerceStore.Pact.Api/ECommerceStore.Pact.Api.csproj src/ECommerceStore.Pact.Api/packages.lock.json src/ECommerceStore.Pact.Api/
COPY src/ECommerceStore.Pact.Infrastructure/ECommerceStore.Pact.Infrastructure.csproj src/ECommerceStore.Pact.Infrastructure/packages.lock.json src/ECommerceStore.Pact.Infrastructure/
RUN dotnet restore src/ECommerceStore.Pact.Api --locked-mode
COPY src/ src/
RUN dotnet publish src/ECommerceStore.Pact.Api -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ECommerceStore.Pact.Api.dll"]
