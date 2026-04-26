FROM mcr.microsoft.com/dotnet/runtime:9.0-noble AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NeuroBotForNG/NeuroBotForNG.csproj", "NeuroBotForNG/"]
RUN dotnet restore "NeuroBotForNG/NeuroBotForNG.csproj"
COPY . .
WORKDIR "/src/NeuroBotForNG"
RUN dotnet build "./NeuroBotForNG.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NeuroBotForNG.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NeuroBotForNG.dll"]
