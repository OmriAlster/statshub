# syntax=docker/dockerfile:1
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM node:20-alpine AS runtime
WORKDIR /app
RUN npm install -g serve@14

COPY --from=build /app/dist ./dist

EXPOSE 8080
# Railway (and similar hosts) inject $PORT at runtime; `serve -s` handles
# client-side routing by falling back to index.html for unknown paths.
ENTRYPOINT ["/bin/sh", "-c", "serve -s dist -l ${PORT:-8080}"]
