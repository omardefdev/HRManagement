FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy full source so shared project references resolve.
COPY . .

RUN dotnet restore "src/Gateway/HRManagement.Gateway/HRManagement.Gateway.csproj"
RUN dotnet publish "src/Gateway/HRManagement.Gateway/HRManagement.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data/security-audit && chown -R $APP_UID:$APP_UID /app/data

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "HRManagement.Gateway.dll"]

