#!/bin/bash
set -e

# Volume hides image; refresh binaries
find /opt/portway -mindepth 1 -maxdepth 1 ! -name 'appsettings*.json' ! -name endpoints ! -name environments ! -name tokens ! -name files -exec cp -a {} /app/ \;
for f in /opt/portway/appsettings*.json; do [ -e "/app/${f##*/}" ] || cp -a "$f" /app/; done

cd /app
exec dotnet /app/PortwayApi.dll
