---
title: Folders and Routes
description: "Each subfolder under endpoints/ corresponds to an endpoint type"
---

# Folders and Routes

Routes are derived from the `endpoints/` folder tree. The first level is the endpoint type. The folder names below it form the namespace, the endpoint name and the [version](/guide/endpoint-versions). `Namespace` in `entity.json` overrides the folder [namespace](/reference/namespaces).

## Directory layout

```
<portway>/
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

The service account requires write access to the deployment directory. The Linux package sets ownership to the `portway` user. IIS commands: [Deploying on Windows Server](/guide/deployment-windows#3-configure-iis). Docker containers run as the image user: bind-mounted folders require write access for that user.

| Folder | Minimum permission | Reason |
|---|---|---|
| `log/` | Read/Write | Log files and rotation |
| `tokens/` | Read/Write | Token files |
| `environments/` | Read/Write | Configuration; console edits and secret encryption |
| `endpoints/` | Read/Write | Configuration; console edits |
| Root | Read/Write | `auth.db`, `mcp.db`, `metrics.db`, `portway.key` |
