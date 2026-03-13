# Stage 1: Build the VitePress documentation
FROM node:20-alpine AS builder
WORKDIR /app

# Copy package configuration
COPY docs/package*.json ./docs/

WORKDIR /app/docs
# Install dependencies
RUN npm install

# Copy source
WORKDIR /app
COPY docs/ ./docs/

# Build
WORKDIR /app/docs
RUN npx vitepress build

# Stage 2: Serve with Nginx
FROM nginx:alpine

# Copy built static files to Nginx
COPY --from=builder /app/docs/.vitepress/dist /usr/share/nginx/html

# Expose port 80
EXPOSE 80

# Start Nginx
CMD ["nginx", "-g", "daemon off;"]
