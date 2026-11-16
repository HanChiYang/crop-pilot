FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CropPilot.Api/CropPilot.Api.csproj CropPilot.Api/
RUN dotnet restore CropPilot.Api/CropPilot.Api.csproj
COPY CropPilot.Api/ CropPilot.Api/
RUN dotnet publish CropPilot.Api/CropPilot.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/ ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "CropPilot.Api.dll"]
