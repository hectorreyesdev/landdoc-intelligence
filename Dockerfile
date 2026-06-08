# Single image: builds the React SPA and the ASP.NET Core API, then serves both from one
# origin (same-origin, no CORS). Targets Azure Container Apps. Listens on :8080.

# --- Stage: web — build the Vite/React SPA into /web/dist ---
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# --- Stage: api — publish the ASP.NET Core API into /app ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet publish backend/src/LandDoc.Api -c Release -o /app

# --- Final stage: runtime — API + SPA assets served together ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LandDoc.Api.dll"]
