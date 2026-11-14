# Build in the SDK image so the final image contains only published output.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MainApi.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app --no-restore
# The runtime image does not need the SDK or source files.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql needs the Kerberos/GSSAPI library on this base image.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
# Run as the non-root .NET application user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "MainApi.dll"]
