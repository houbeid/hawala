# Interface (PWA) servie par Caddy, qui renvoie aussi /api et /hubs vers l'API (WebSocket compris).
# Contexte de construction : frontend/ (Render, et deploy/docker-compose.yml). Réglages par variables : voir Caddyfile.

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM caddy:2-alpine
# Le binaire officiel porte la capacité « cap_net_bind_service » (ports < 1024 sans root). Les hébergeurs qui
# retirent les capacités (Render : no-new-privileges) refusent alors de le lancer (« exec … operation not permitted »).
# Caddy écoute sur 8080 sur Render, et tourne en root dans la variante serveur : la capacité est inutile.
RUN apk add --no-cache libcap && setcap -r /usr/bin/caddy && apk del libcap
COPY --from=build /src/dist /srv
COPY Caddyfile /etc/caddy/Caddyfile
