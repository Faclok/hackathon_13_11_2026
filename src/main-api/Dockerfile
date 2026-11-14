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
USER root
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv libgomp1 ca-certificates \
    && python3 -m venv /opt/silero \
    && /opt/silero/bin/pip install --no-cache-dir torch==2.8.0 --index-url https://download.pytorch.org/whl/cpu \
    && /opt/silero/bin/pip install --no-cache-dir numpy==2.3.3 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/models && chown $APP_UID:$APP_UID /app/models
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Speech__PythonExecutable=/opt/silero/bin/python Speech__ModelPath=/app/models/v5_cis_base.pt
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BashkirSchool.Host.dll"]
