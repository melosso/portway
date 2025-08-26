# Upgrading Portway

Upgrading Portway is simple. Follow these steps to ensure a smooth upgrade from a previous version.

> [!IMPORTANT]
> **Portway is subject to data and database changes between versions.** Staying up to date with the release notes, especially for major version upgrades, is adviced to avoid data loss or compatibility issues. Please make sure to validate no such breaking changes have been made before upgrading.

1. **Read the Release Notes**
   - Review the [release notes](https://github.com/melosso/portway/releases/) for important changes or migration steps.

2. **Backup Your Current Installation**
   - Copy your existing Portway directory to a safe location.
   - Save your configuration files, including:
     - `appsettings.json`
     - `endpoints.json` (or similar)
     - `environments.json` (or similar)
   - Backup any SQLite database files used by Portway (e.g., `*.db`).

3. **Install the New Version**
   - Download the latest release from GitHub.
   - Replace the old files with the new ones, or follow the [installation guide](/guide/getting-started).

4. **Restore Configuration and Data**
   - Copy your saved configuration files and SQLite databases into the new installation if needed.

5. **Test and Validate**
   - Start Portway and verify that your endpoints, environments, and data work as expected.

> [!TIP]
> For major upgrades, always test in a non-production environment first.

> [!INFORMATION0]
> You can find your installated version in file `version.txt`.