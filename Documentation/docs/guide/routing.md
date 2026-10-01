---
title: Folder structure and routing
description: "Each subfolder under endpoints/ corresponds to an endpoint type"
---

# Folder structure and routing

Routes are derived from the `endpoints/` folder tree: the first level is the endpoint type, the folder names below it form the namespace and endpoint name. Changes are reloaded without a restart. `Namespace` in `entity.json` overrides the folder namespace ([Namespaces](/reference/namespaces)).

## Directory layout

```
PortwayApi/
├── appsettings.json
├── web.config
├── *.db
├── log/
├── tokens/
├── environments/
│   ├── settings.json
│   ├── dev/
│   │   └── settings.json
│   ├── test/
│   │   └── settings.json
│   └── prod/
│       └── settings.json
└── endpoints/
    ├── SQL/
    │   └── Inventory/            # namespace
    │       └── Products/
    │           └── entity.json
    ├── Proxy/
    │   ├── Accounts/             # no namespace
    │   │   └── entity.json
    │   └── Financial/
    │       └── SalesOrder/
    │           └── entity.json
    ├── Webhooks/
    │   └── Integrations/
    │       └── Inbound/
    │           └── entity.json
    ├── Files/
    │   ├── CustomerData/
    │   │   └── entity.json
    │   └── Images/
    │       └── entity.json
    └── Static/
        └── Masterdata/
            └── Countries/
                └── entity.json
```

## Route patterns

| Endpoint type | Folder path | URL pattern |
|---|---|---|
| SQL | `endpoints/SQL/[{Namespace}/]{Name}/entity.json` | `/api/{env}/[{Namespace}/]{Name}` |
| Proxy | `endpoints/Proxy/[{Namespace}/]{Name}/entity.json` | `/api/{env}/[{Namespace}/]{Name}` |
| Composite | `endpoints/Proxy/[{Namespace}/]{Name}/entity.json` (Type: Composite) | `/api/{env}/[{Namespace}/]{Name}` |
| Webhook | `endpoints/Webhooks/{Namespace}/{Name}/entity.json` | `/api/{env}/{Namespace}/{Name}/{id}` |
| File | `endpoints/Files/[{Namespace}/]{Name}/entity.json` | `/api/{env}/files/[{Namespace}/]{Name}` |
| Static | `endpoints/Static/[{Namespace}/]{Name}/entity.json` | `/api/{env}/[{Namespace}/]{Name}` |

Segments in square brackets are optional; a namespace folder adds its name to the URL.

## Folder permissions

Application Pool identity access to the deployment directory:

```powershell
# ApplicationPoolIdentity
icacls "C:\Apps\Portway" /grant "IIS AppPool\PortwayAppPool:(F)" /T /C

# Custom service account
icacls "C:\Apps\Portway" /grant "DOMAIN\SVC_PORTWAY:(F)" /T /C
```

| Folder | Minimum permission | Reason |
|---|---|---|
| `log/` | Read/Write | Log files and rotation |
| `tokens/` | Read/Write | Token files |
| `environments/` | Read/Write | Configuration; console edits and secret encryption |
| `endpoints/` | Read/Write | Configuration; console edits |
| Root | Read/Write | `auth.db`, `mcp.db`, `metrics.db`, `portway.key` |

:::warning
Directory browsing must be disabled in `web.config`.
:::

## Next steps

- [Environments](/guide/environments)
- [SQL Endpoints](/guide/endpoints-sql)
- [Proxy Endpoints](/guide/endpoints-proxy)
- [HTTP Methods](/reference/http-methods)
