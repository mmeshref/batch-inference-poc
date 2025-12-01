FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ./BatchPortal/BatchPortal.csproj ./BatchPortal/
COPY ./Shared/Shared.csproj ./Shared/
RUN dotnet restore ./BatchPortal/BatchPortal.csproj

COPY . .
RUN dotnet publish ./BatchPortal/BatchPortal.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "BatchPortal.dll"]

