FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["StudentHub.API.csproj", "./"]
RUN dotnet restore "StudentHub.API.csproj"

COPY . .
RUN dotnet publish "StudentHub.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:10000
ENV DOTNET_USE_POLLING_FILE_WATCHER=true

EXPOSE 10000

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StudentHub.API.dll"]
