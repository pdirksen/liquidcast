# syntax=docker/dockerfile:1

# --- 1. Build the Vue SPA ---
FROM node:22-alpine AS web
WORKDIR /web
COPY src/web/package*.json ./
RUN npm ci
COPY src/web/ ./
# Override the dev outDir; emit the SPA into ./dist for the next stage.
RUN npx vite build --outDir dist --emptyOutDir

# --- 2. Publish the .NET API (self-contained linux-x64) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Liquidcast.Api/Liquidcast.Api.csproj Liquidcast.Api/
RUN dotnet restore Liquidcast.Api/Liquidcast.Api.csproj -r linux-x64
COPY src/Liquidcast.Api/ Liquidcast.Api/
COPY --from=web /web/dist Liquidcast.Api/wwwroot
RUN dotnet publish Liquidcast.Api/Liquidcast.Api.csproj \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=false -o /publish

# --- 3. Runtime on the official Liquidsoap image ---
FROM savonet/liquidsoap:v2.4.5
USER root
WORKDIR /app
COPY --from=build /publish ./
RUN chmod +x /app/Liquidcast.Api

ENV DataPath=/data \
    ASPNETCORE_URLS=http://0.0.0.0:5000 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    ADMIN_USER=admin \
    ADMIN_PASSWORD=admin

VOLUME /data
EXPOSE 5000
ENTRYPOINT ["/app/Liquidcast.Api"]
