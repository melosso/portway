---
title: SQL Providers
description: Reference for all supported SQL database providers, e.g. SQL Server, PostgreSQL, MySQL, and SQLite, including connection string formats, provider auto-detection logic, and capability matrix.
outline: [2, 3]
keywords: [SQL Server, PostgreSQL, MySQL, SQLite, connection string, provider detection, OData]
---

# SQL Providers

Portway supports four relational database backends. The active provider for each environment is determined automatically from the connection string in `settings.json`, no extra configuration key is required.

## Supported Providers

| Provider | Typical use |
|---|---|
| **SQL Server** | Enterprise ERP, WMS, and legacy Windows applications. Default when nothing else matches. |
| **PostgreSQL** | Open-source RDBMS common in Linux and cloud-native stacks. |
| **MySQL / MariaDB** | Web databases and LAMP-stack back-ends. |
| **SQLite** | Local file databases, demos, and lightweight read-only APIs. |

---

## Provider Auto-Detection

Portway reads the connection string and identifies the provider without requiring an explicit `Provider` field. Detection runs top-to-bottom through a priority list; the first match wins.

| Priority | Condition | Detected provider |
|:---:|---|---|
| 1 | SQL Server-exclusive keywords present (`TrustServerCertificate=`, `Integrated Security=`, `Initial Catalog=`, `MultipleActiveResultSets=`, `Encrypt=`, `ApplicationIntent=`, …) | SQL Server |
| 2 | OLE DB provider name (`Provider=SQLOLEDB`, `MSOLEDBSQL`, `SQLNCLI`) | SQL Server |
| 3 | ODBC driver name (`Driver={SQL Server}`, `Driver={ODBC Driver 17 for SQL Server}`) | SQL Server |
| 4 | URI prefix `postgres://` or `postgresql://` | PostgreSQL |
| 5 | URI prefix `mysql://` | MySQL |
| 6 | Key `Host=` present without `Server=` or `Data Source=` | PostgreSQL |
| 7 | MySQL-exclusive keys (`SslMode=`, `AllowUserVariables=`, `AllowPublicKeyRetrieval=`) | MySQL |
| 8 | `Data Source=` value ends in `.db`, `.sqlite`, `.sqlite3`, or equals `:memory:` | SQLite |
| 9 | _(anything else)_ | **SQL Server** (default) |

:::tip
Standard SQL Server connection strings naturally contain keywords like `TrustServerCertificate=` or `Integrated Security=` and are caught at priority 1. Existing environments require no changes.
:::

---

## Connection String Reference

### SQL Server

Windows (integrated) authentication:
```json
{
  "ConnectionString": "Server=SQLPROD01;Database=ProductionDB;Integrated Security=True;TrustServerCertificate=true;"
}
```

SQL authentication:
```json
{
  "ConnectionString": "Server=SQLPROD01;Database=ProductionDB;User Id=svc_portway;Password=your-password;TrustServerCertificate=true;Encrypt=true;"
}
```

**Common SQL Server parameters**

| Parameter | Description | Default |
|---|---|---|
| `Server` | SQL Server instance name or IP | Required |
| `Database` | Target database name | Required |
| `Integrated Security` | Use Windows authentication | `False` |
| `User Id` / `Password` | SQL authentication credentials | - |
| `Encrypt` | Encrypt the connection | `False` |
| `TrustServerCertificate` | Skip certificate validation (dev only) | `False` |
| `Connection Timeout` | Seconds before giving up | `15` |
| `MultipleActiveResultSets` | Enable MARS | `False` |
| `ApplicationIntent` | `ReadOnly` for AG read replicas | - |

---

### PostgreSQL

Key-value format (Npgsql):
```json
{
  "ConnectionString": "Host=db.example.com;Port=5432;Database=mydb;Username=portway;Password=your-password;"
}
```

URI format:
```json
{
  "ConnectionString": "postgresql://portway:your-password@db.example.com:5432/mydb"
}
```

**Common PostgreSQL parameters**

| Parameter | Description | Default |
|---|---|---|
| `Host` | Server hostname or IP | Required |
| `Port` | Server port | `5432` |
| `Database` | Target database name | Required |
| `Username` / `Password` | Credentials | - |
| `SSL Mode` | `Require`, `Prefer`, `Disable` | `Prefer` |
| `Timeout` | Connection timeout (seconds) | `15` |

---

### MySQL / MariaDB

```json
{
  "ConnectionString": "Server=db.example.com;Port=3306;Database=mydb;Uid=portway;Pwd=your-password;SslMode=Preferred;"
}
```

**Common MySQL parameters**

| Parameter | Description | Default |
|---|---|---|
| `Server` | Hostname or IP | Required |
| `Port` | Server port | `3306` |
| `Database` | Target database name | Required |
| `Uid` / `Pwd` | Credentials | - |
| `SslMode` | `Preferred`, `Required`, `None` | `Preferred` |
| `AllowUserVariables` | Allow user-defined variables in queries | `False` |
| `ConnectionTimeout` | Timeout in seconds | `15` |

---

### SQLite

File-based (path relative to the application working directory):
```json
{
  "ConnectionString": "Data Source=environments/WMS/demo.db;"
}
```

In-memory (data is lost when the process restarts):
```json
{
  "ConnectionString": "Data Source=:memory:;"
}
```

:::info
SQLite connection strings carry no credentials. Portway skips the credential-masking step for SQLite environments entirely.
:::

---

## Capability Matrix

| Feature | SQL Server | PostgreSQL | MySQL | SQLite |
|---|:---:|:---:|:---:|:---:|
| GET with OData (`$filter`, `$orderby`, `$select`, `$top`, `$skip`) | ✅ | ✅ | ✅ | ✅ |
| POST / PUT / PATCH via stored procedure | ✅ | ✅ | ✅ | ⚠️ |
| DELETE | ✅ | ✅ | ✅ | ✅ |
| Table-valued functions (TVF) | ✅ | ✅ | ❌ | ❌ |
| Schema namespacing (`dbo.TableName`) | ✅ | ✅ | ✅ | ❌ |
| Column metadata & OpenAPI generation | ✅ | ✅ | ✅ | ✅ |
| Connection pooling | ✅ | ✅ | ✅ | Limited |
| Health check | ✅ | ✅ | ✅ | ✅ |

:::warning
**SQLite, write operations:** SQLite does not support stored procedures. Endpoints that define a `Procedure` field and point to a SQLite environment return `501 Not Implemented`. GET-only endpoints are unaffected.
:::

:::info
**MySQL, table-valued functions:** MySQL/MariaDB has no TVF concept. Endpoints configured as `DatabaseObjectType: TableValuedFunction` are skipped during metadata initialisation for MySQL environments and will not appear in the OpenAPI spec.
:::

---

## Schema Behaviour

| Provider | Schema support | Default schema |
|---|---|---|
| SQL Server | Full two-part names (`dbo.TableName`) | `dbo` |
| PostgreSQL | Full two-part names (`public.table_name`) | `public` |
| MySQL | Schema maps to the database in the connection string | _(from connection string)_ |
| SQLite | No schema support: prefix is omitted automatically | - |

When `DatabaseSchema` is omitted from an endpoint's `entity.json`, Portway defaults to `dbo`. For SQLite environments this prefix is stripped at query time so no manual override is needed.

---

## Related Topics

- [Environments Guide](/guide/environments): creating and managing environments
- [Environment Settings Reference](/reference/environment-settings): full `settings.json` reference
- [SQL Endpoints Guide](/guide/endpoints-sql): configuring SQL endpoints
- [Health Checks](/reference/health-checks): per-environment health status
