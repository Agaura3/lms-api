# ========================
# Build Stage
# ========================
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish


# ========================
# Runtime Stage
# ========================
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN apt-get update && apt-get install -y libkrb5-3

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "lms-api.dll"]