# Interface (PWA) servie par Caddy, qui renvoie aussi /api et /hubs vers l'API (WebSocket compris).
# Contexte de construction : frontend/ (Render, et deploy/docker-compose.yml). Réglages par variables : voir Caddyfile.

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM caddy:2-alpine
COPY --from=build /src/dist /srv
COPY Caddyfile /etc/caddy/Caddyfile
