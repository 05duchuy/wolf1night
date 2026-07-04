# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY WolfGameServer.csproj ./
RUN dotnet restore WolfGameServer.csproj

COPY . .
RUN dotnet publish WolfGameServer.csproj -c Release -o /app/publish

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render.com (and most PaaS hosts) inject the port to listen on via $PORT.
# Program.cs reads this at startup. 10000 is just a harmless local default.
ENV PORT=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "WolfGameServer.dll"]
