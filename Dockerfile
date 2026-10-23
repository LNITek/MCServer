FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MCServer/MCServer.csproj", "MCServer/"]
COPY ["MCServer.Plugin/MCServer.Plugin.csproj", "MCServer.Plugin/"]
COPY ["MCServer.BDS/MCServer.BDS.csproj", "MCServer.BDS/"]
COPY ["./README.md", "./"]
COPY ["./ChangeLog.md", "./"]
RUN dotnet restore "MCServer/MCServer.csproj"
COPY . .
WORKDIR "/src/MCServer"
RUN dotnet build "./MCServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MCServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Data/config root override (see MCServer/Program.cs ResolveRootPath).
# /data must be pre-created and owned by the app user: VOLUME inherits
# the image directory's ownership on auto-created anonymous volumes.
ENV MCS_DATA=/data
USER 0
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID

ENTRYPOINT ["dotnet", "MCServer.dll"]
