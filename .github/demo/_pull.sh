#!/bin/bash
  set -e
  for f in pull-demo.sh reset-demo.sh; do
    curl -fsSLO "https://raw.githubusercontent.com/melosso/portway/main/.github/demo/$f"
  done
  chmod +x pull-demo.sh reset-demo.sh