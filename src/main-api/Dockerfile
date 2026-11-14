FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /source
COPY global.json BashkirSchool.slnx ./
COPY src/BashkirSchool.Domain/BashkirSchool.Domain.csproj src/BashkirSchool.Domain/
COPY src/BashkirSchool.Application/BashkirSchool.Application.csproj src/BashkirSchool.Application/
COPY src/BashkirSchool.Infrastructure/BashkirSchool.Infrastructure.csproj src/BashkirSchool.Infrastructure/
COPY src/BashkirSchool.Host/BashkirSchool.Host.csproj src/BashkirSchool.Host/
RUN dotnet restore src/BashkirSchool.Host/BashkirSchool.Host.csproj
COPY src/ src/
RUN dotnet publish src/BashkirSchool.Host/BashkirSchool.Host.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BashkirSchool.Host.dll"]
