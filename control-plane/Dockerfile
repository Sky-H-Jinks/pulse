FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ControlPlane.Api/ControlPlane.Api.csproj src/ControlPlane.Api/
RUN dotnet restore src/ControlPlane.Api/ControlPlane.Api.csproj
COPY src/ src/
RUN dotnet publish src/ControlPlane.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "ControlPlane.Api.dll"]
