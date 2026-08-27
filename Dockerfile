# Builds the Angular app and the .NET API into a single image: the API serves the compiled
# Angular build as static files (see MapFallbackToFile in Program.cs) and answers /api and
# /hubs/chat itself, so there's only one service to deploy.

# --- Frontend build ---
FROM node:22-alpine AS frontend-build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY angular.json tsconfig.json tsconfig.app.json tsconfig.spec.json ./
COPY public ./public
COPY src ./src
RUN npm run build

# --- Backend build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY api/api.csproj ./api/
RUN dotnet restore ./api/api.csproj
COPY api/. ./api/
RUN dotnet publish ./api/api.csproj -c Release -o /app/publish --no-restore

# --- Runtime image ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=backend-build /app/publish .
COPY --from=frontend-build /src/dist/realtime-chat/browser ./wwwroot

# Railway injects PORT at runtime; Program.cs reads it and binds Kestrel accordingly.
EXPOSE 8080
ENTRYPOINT ["dotnet", "api.dll"]
