FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy source tree so project references resolve during restore/publish.
COPY . .

RUN dotnet restore "src/Services/HRCore/HR.Core.Web/HR.Core.Web.csproj"
RUN dotnet publish "src/Services/HRCore/HR.Core.Web/HR.Core.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "HR.Core.Web.dll"]

