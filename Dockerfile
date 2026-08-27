FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["backend/StudentHub.API/StudentHub.API.csproj", "backend/StudentHub.API/"]
RUN dotnet restore "backend/StudentHub.API/StudentHub.API.csproj"
COPY . .
WORKDIR "/src/backend/StudentHub.API"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StudentHub.API.dll"]
