FROM node:22 AS build

WORKDIR /app

COPY package*.json ./
RUN npm ci --include=optional

    COPY . .
    RUN npm run build

FROM nginx:1.27-alpine

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/kvbind-sample-ui/browser /usr/share/nginx/html

EXPOSE 80
