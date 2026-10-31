# API Hawala (.NET 10). Contexte de construction : backend/ (voir docker-compose.yml).
# Construit pour la machine cible (amd64 ou arm64, ex. VM Oracle Ampere) : l'image du SDK tourne sur la machine qui
# construit et publie pour l'architecture visée.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish src/Hawala.Api/Hawala.Api.csproj -c Release -a $TARGETARCH -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Clés de Data Protection d'ASP.NET : gardées dans un volume (créé ici pour appartenir à l'utilisateur « app »).
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID /home/app/.aspnet
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hawala.Api.dll"]
