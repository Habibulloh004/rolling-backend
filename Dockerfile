FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Rolling.sln ./
COPY Rolling.Application/Rolling.Application.csproj Rolling.Application/
COPY Rolling.Domain/Rolling.Domain.csproj Rolling.Domain/
COPY Rolling.Infrastructure/Rolling.Infrastructure.csproj Rolling.Infrastructure/
COPY Rolling.Web/Rolling.Web.csproj Rolling.Web/

RUN dotnet restore Rolling.Web/Rolling.Web.csproj

COPY . ./
RUN dotnet publish Rolling.Web/Rolling.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5020
EXPOSE 5020

COPY --from=build /app/publish ./
COPY migrations ./migrations

ENTRYPOINT ["dotnet", "Rolling.Web.dll"]
