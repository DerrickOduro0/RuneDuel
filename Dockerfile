# Builds and runs the Rune Duel API. Render builds this file on every push.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/RuneReign.Domain/RuneReign.Domain.csproj src/RuneReign.Domain/
COPY src/RuneReign.Api/RuneReign.Api.csproj src/RuneReign.Api/
RUN dotnet restore src/RuneReign.Api/RuneReign.Api.csproj
COPY src/ src/
RUN dotnet publish src/RuneReign.Api/RuneReign.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
# Render tells the server which port to use in PORT.
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet RuneReign.Api.dll"]
