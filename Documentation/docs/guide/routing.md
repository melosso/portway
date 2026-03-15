# Folder structure and routing

> Portway derives API routes from the `endpoints/` folder hierarchy, no route registration required.

Each subfolder under `endpoints/` corresponds to an endpoint type. The folder name within each type becomes the endpoint name in the API URL. Portway watches these folders and reloads configuration when files change.

## Directory layout

```
PortwayApi/
├── appsettings.json
├── web.config
├── auth.db                         # Authentication database (auto-created)
├── log/                            # Application logs (daily rotation)
├── tokens/                         # Token files (remove after recording)
├── environments/
│   ├── settings.json               # Global: server name and allowed environments
│   ├── dev/
│   │   └── settings.json
│   ├── test/
│   │   └── settings.json
│   └── prod/
│       └── settings.json
└── endpoints/
    ├── SQL/
    │   └── Products/
    │       └── entity.json
    ├── Proxy/
    │   ├── Accounts/
    │   │   └── entity.json
    │   └── SalesOrder/             # Composite (Type: "Composite" in entity.json)
    │       └── entity.json
    ├── Webhooks/
    │   └── entity.json
    ├── Files/
    │   ├── CustomerData/
    │   │   └── entity.json
    │   └── Images/
    │       └── entity.json
    └── Static/
        └── Countries/
            └── entity.json
```

## Route patterns

| Endpoint type | Folder path | URL pattern |
|---|---|---|
| SQL | `endpoints/SQL/{Name}/entity.json` | `/api/{env}/{Name}` |
| Proxy | `endpoints/Proxy/{Name}/entity.json` | `/api/{env}/{Name}` |
| Composite | `endpoints/Proxy/{Name}/entity.json` (Type: Composite) | `/api/{env}/composite/{Name}` |
| Webhook | `endpoints/Webhooks/entity.json` | `/api/{env}/webhook/{id}` |
| File | `endpoints/Files/{Name}/entity.json` | `/api/{env}/files/{Name}` |
| Static | `endpoints/Static/{Name}/entity.json` | `/api/{env}/{Name}` |

The endpoint name in the URL is case-sensitive and matches the folder name exactly.

## Folder permissions

Grant the IIS Application Pool identity read/write access to the deployment directory:

```powershell
# ApplicationPoolIdentity
icacls "C:\Apps\Portway" /grant "IIS AppPool\PortwayAppPool:(F)" /T /C

# Custom service account
icacls "C:\Apps\Portway" /grant "DOMAIN\SVC_PORTWAY:(F)" /T /C
```

| Folder | Minimum permission | Reason |
|---|---|---|
| `log/` | Read/Write | Log file creation and rotation |
| `tokens/` | Read/Write | Token file management |
| `environments/` | Read | Configuration reads |
| `endpoints/` | Read | Configuration reads |
| Root | Read/Write | `auth.db` and temporary files |

:::warning
Do not expose the deployment directory via web browsing. Verify that `web.config` disables directory listing.
:::

## Next steps

- [Environments](./environments)
- [SQL Endpoints](./endpoints-sql)
- [Proxy Endpoints](./endpoints-proxy)
