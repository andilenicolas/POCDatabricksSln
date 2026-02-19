# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Install unixODBC and the Simba Spark ODBC Driver.
# Place the driver .deb in the drivers/ folder before building.
# Download from: https://www.databricks.com/spark/odbc-drivers-download
# (Linux .deb — e.g. simbaspark_2.8.3.1005-2_amd64.deb)
COPY drivers/*.deb /tmp/simbaspark.deb
RUN apt-get update && apt-get install -y --no-install-recommends \
  unixodbc libsasl2-modules-gssapi-mit \
  && dpkg -i /tmp/simbaspark.deb \
  && rm /tmp/simbaspark.deb \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/* \
  # Register the driver so unixODBC can find it by name
  && printf '[Simba Spark ODBC Driver]\nDescription=Simba Spark ODBC Driver\nDriver=/opt/simba/spark/lib/64/libsparkodbc_sb64.so\n' \
  >> /etc/odbcinst.ini

USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DatabricksSln.csproj", "."]
RUN dotnet restore "./DatabricksSln.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./DatabricksSln.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DatabricksSln.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DatabricksSln.dll"]