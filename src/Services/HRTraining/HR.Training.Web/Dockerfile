FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/Services/HRTraining/HR.Training.Web/HR.Training.Web.csproj"
RUN dotnet publish "src/Services/HRTraining/HR.Training.Web/HR.Training.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "HR.Training.Web.dll"]

