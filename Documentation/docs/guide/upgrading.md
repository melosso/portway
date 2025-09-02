# Upgrading Portway

Upgrading Portway is simple. Follow these steps to ensure a smooth upgrade from a previous version.

> [!IMPORTANT]
> **Portway can be subject to data and database changes between versions.** Staying up to date with the release notes, especially for major version upgrades, is adviced to avoid data loss or compatibility issues. Please make sure to validate no such breaking changes have been made before upgrading.

1. **Read the Release Notes**
   - Review the [release notes](https://github.com/melosso/portway/releases/) for important changes or migration steps.

2. **Backup Your Current Installation**
   - Copy your existing Portway directory to a safe location.
   - Save your configuration files, including:
     - `appsettings.json`
     - `Endpoints\` folder (and its content)
     - `Environments\` folder (and its content)
   - Backup any SQLite database files used by Portway (e.g., `*.db`).

3. **Windows: Suspend The Webserver**
   - If you're using Windows Server (IIS): Stop the Application Pool. 

4. **Install the New Version**
   - Download the latest release from GitHub.
   - Replace the old files with the new ones, or follow the [installation guide](/guide/getting-started).

5. **Restore Configuration and Data**
   - Copy your saved configuration files and SQLite databases into the new installation if needed.

6. **Test and Validate**
   - Start Portway and verify that your endpoints, environments, and data work as expected.

> [!TIP]
> For major upgrades, always test in a non-production environment first.

> [!INFORMATION]
> You can find your installated version in file `version.txt`. Please make sure to overwrite it, to keep version information up to date (which is especially important when submitting bug reports).