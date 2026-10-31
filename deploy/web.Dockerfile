# Interface (PWA) servie par Caddy, qui fait aussi le HTTPS et renvoie /api et /hubs vers l'API.
# Contexte de construction : frontend/ ; le Caddyfile est monté par docker-compose.yml.

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM caddy:2-alpine
COPY --from=build /src/dist /srv
