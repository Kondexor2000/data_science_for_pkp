FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY webapi/ServerActivity.Api.csproj webapi/
RUN dotnet restore webapi/ServerActivity.Api.csproj

COPY webapi/ webapi/
RUN dotnet publish webapi/ServerActivity.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ServerActivity.Api.dll"]
