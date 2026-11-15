FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
# npm install (ולא npm ci) - סובלני להבדלי גרסאות npm בין סביבת הפיתוח לאימג׳
RUN npm install --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
EXPOSE 80
