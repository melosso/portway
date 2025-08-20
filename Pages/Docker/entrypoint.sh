#!/bin/bash

# Repository and folder details
REPO_URL="https://github.com/melosso/portwayapi.git"
REPO_BRANCH="main"
SOURCE_FOLDER="/Pages/Web"
DESTINATION_FOLDER="/usr/share/nginx/html"

# Clone the repository
echo "Cloning repository: $REPO_URL (Branch: $REPO_BRANCH)"
git clone -b "$REPO_BRANCH" "$REPO_URL" /tmp/repo

# Copy only the specified folder
if [ -d "/tmp/repo$SOURCE_FOLDER" ]; then
    echo "Copying contents from $SOURCE_FOLDER to $DESTINATION_FOLDER"
    cp -R "/tmp/repo$SOURCE_FOLDER"/. "$DESTINATION_FOLDER"

    echo "Contents of $DESTINATION_FOLDER:"
    ls -la "$DESTINATION_FOLDER"
else
    echo "Error: Source folder $SOURCE_FOLDER not found in the repository"
    exit 1
fi

# Clean up temporary clone directory
rm -rf /tmp/repo

# Start nginx in foreground mode
nginx -g "daemon off;"
