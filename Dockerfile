# Etap 1: build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Skopiuj pliki projektu i przywróć zależności
COPY *.csproj ./
RUN dotnet restore

# Skopiuj resztę i zbuduj
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Etap 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Ustaw zmienną PORT (Render.com ją ustawia automatycznie)
ENV ASPNETCORE_URLS=http://0.0.0.0:$PORT

# Otwórz port (tylko dla lokalnych testów; Render ignoruje EXPOSE)
EXPOSE 5000

ENTRYPOINT ["dotnet", "BlackOutChatServer.dll"]
