FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project files first so the restore layer is cached until a package changes.
COPY src/OT.Assessment.Core/OT.Assessment.Core.csproj src/OT.Assessment.Core/
COPY src/OT.Assessment.Infrastructure/OT.Assessment.Infrastructure.csproj src/OT.Assessment.Infrastructure/
COPY src/OT.Assessment.App/OT.Assessment.App.csproj src/OT.Assessment.App/
RUN dotnet restore src/OT.Assessment.App/OT.Assessment.App.csproj

COPY src/ src/
RUN dotnet publish src/OT.Assessment.App/OT.Assessment.App.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "OT.Assessment.App.dll"]
