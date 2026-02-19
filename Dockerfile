# ── Stage 1: base — runtime + ODBC driver ────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Install unixODBC and Simba Spark ODBC Driver
# Driver .deb is fetched by the CI pipeline from Azure Blob and placed in drivers/
# before docker build runs. Never committed to the repo.
COPY drivers/*.deb /tmp/simbaspark.deb
RUN apt-get update && apt-get install -y --no-install-recommends \
  unixodbc \
  libsasl2-modules-gssapi-mit \
  && dpkg -i /tmp/simbaspark.deb \
  && rm /tmp/simbaspark.deb \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/* \
  # Register driver with unixODBC.
  # [ODBC Drivers] section is required — it is the authoritative installed-driver
  # list that the driver manager checks. Without it, odbcinst tooling and some
  # driver manager versions behave unpredictably even when the .so path is used
  # directly in the connection string.
  && printf '[ODBC Drivers]\nSimba Spark ODBC Driver=Installed\n\n[Simba Spark ODBC Driver]\nDescription=Simba Spark ODBC Driver\nDriver=/opt/simba/spark/lib/64/libsparkodbc_sb64.so\n' \
  >> /etc/odbcinst.ini

# Simba driver runtime environment
# ODBCSYSINI    : directory containing odbcinst.ini — tells unixODBC where to look
# SIMBASPARKINI : driver-level config (logging, error messages, etc.)
# LD_LIBRARY_PATH : ensures the .so resolves its own internal dependencies at load time
ENV ODBCSYSINI=/etc \
  SIMBASPARKINI=/opt/simba/spark/lib/64/simba.sparkodbc.ini \
  LD_LIBRARY_PATH=/opt/simba/spark/lib/64

USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# ── Stage 2: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DatabricksSln.csproj", "."]
RUN dotnet restore "./DatabricksSln.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./DatabricksSln.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ── Stage 3: publish ──────────────────────────────────────────────────────────
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DatabricksSln.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ── Stage 4: final ────────────────────────────────────────────────────────────
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DatabricksSln.dll"]