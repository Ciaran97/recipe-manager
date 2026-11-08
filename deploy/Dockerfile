# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM node:26.10.0-bookworm-slim@sha256:3ffc19ea878019d9e9ae8971732ad4a03cda44f167107173174b60ed7c65bed3 AS web
WORKDIR /web
COPY src/RecipeManager.Web/package.json src/RecipeManager.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/RecipeManager.Web/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS backend
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RecipeManager.Core/ src/RecipeManager.Core/
COPY src/RecipeManager.Infrastructure/ src/RecipeManager.Infrastructure/
COPY src/RecipeManager.Api/ src/RecipeManager.Api/
RUN dotnet restore src/RecipeManager.Api --locked-mode
RUN dotnet publish src/RecipeManager.Api --no-restore -c Release -o /publish /p:UseAppHost=false
COPY --from=web /web/dist/recipe-manager-web/browser/ /publish/wwwroot/
COPY --from=web /web/dist/recipe-manager-web/3rdpartylicenses.txt /publish/wwwroot/3rdpartylicenses.txt

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS runtime
USER root
RUN mkdir -p /var/lib/recipe-manager/images /var/lib/recipe-manager/keys /var/lib/recipe-manager/maintenance \
    && chown -R app:app /var/lib/recipe-manager \
    && chmod 0700 /var/lib/recipe-manager/images /var/lib/recipe-manager/keys /var/lib/recipe-manager/maintenance
WORKDIR /app
COPY --from=backend --chown=app:app /publish/ ./
# Keep these paths aligned with the exact SkiaSharp versions in Directory.Packages.props.
COPY --from=backend /root/.nuget/packages/skiasharp/4.154.0/LICENSE.txt /app/third-party/SkiaSharp/LICENSE.txt
COPY --from=backend /root/.nuget/packages/skiasharp.nativeassets.linux.nodependencies/4.154.0/LICENSE.txt /app/third-party/SkiaSharp.NativeAssets.Linux.NoDependencies/LICENSE.txt
COPY --from=backend /root/.nuget/packages/skiasharp.nativeassets.linux.nodependencies/4.154.0/THIRD-PARTY-NOTICES.txt /app/third-party/SkiaSharp.NativeAssets.Linux.NoDependencies/THIRD-PARTY-NOTICES.txt
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 CMD ["dotnet", "RecipeManager.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "RecipeManager.Api.dll"]
