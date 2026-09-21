FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/MondayBrief.Core/MondayBrief.Core.csproj src/MondayBrief.Core/
COPY src/MondayBrief.Api/MondayBrief.Api.csproj src/MondayBrief.Api/
RUN dotnet restore src/MondayBrief.Api/MondayBrief.Api.csproj

COPY src/MondayBrief.Core/ src/MondayBrief.Core/
COPY src/MondayBrief.Api/ src/MondayBrief.Api/
RUN dotnet publish src/MondayBrief.Api/MondayBrief.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish ./

COPY deploy/mondaybrief.db ./App_Data/mondaybrief.db

RUN chown -R app:app /app/App_Data
USER app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "MondayBrief.Api.dll"]