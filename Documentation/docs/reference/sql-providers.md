---
title: SQL Providers
description: Supported SQL providers, connection string detection, capabilities and schema defaults.
outline: [2, 3]
keywords: [SQL Server, PostgreSQL, MySQL, SQLite, connection string, provider detection, OData]
---

# SQL Providers

Portway supports SQL Server, PostgreSQL, MySQL/MariaDB and SQLite. The provider of an environment is detected from the `ConnectionString` in its `settings.json`. The parity suite runs against SQL Server 2025, PostgreSQL 18 and MySQL 8.0.

## Detection

The first matching rule wins.

| Priority | Condition | Provider |
|:---:|---|---|
| 1 | SQL Server keywords (`TrustServerCertificate=`, `Integrated Security=`, `Trusted_Connection=`, `Encrypt=`, `Initial Catalog=`, `MultipleActiveResultSets=`, `ApplicationIntent=`, …) | SQL Server |
| 2 | OLE DB provider (`Provider=SQLOLEDB`, `MSOLEDBSQL`, `SQLNCLI`) | SQL Server |
| 3 | ODBC driver (`Driver={SQL Server}`, `Driver={ODBC Driver 17 for SQL Server}`) | SQL Server |
| 4 | `postgres://` or `postgresql://` prefix | PostgreSQL |
| 5 | `mysql://` prefix | MySQL |
| 6 | `Host=` without `Server=` or `Data Source=` | PostgreSQL |
| 7 | `SslMode=`, `AllowUserVariables=` or `AllowPublicKeyRetrieval=` | MySQL |
| 8 | `Data Source=` ending in `.db`, `.sqlite` or `.sqlite3`, or `:memory:` | SQLite |
| 9 | Anything else | SQL Server |

## Connection strings

::: code-group

```json [SQL Server]
{
  "ConnectionString": "Server=SQLPROD01;Database=ProductionDB;User Id=svc_portway;Password=your-password;TrustServerCertificate=true;Encrypt=true;"
}
```

```json [PostgreSQL]
{
  "ConnectionString": "Host=db.example.com;Port=5432;Database=mydb;Username=portway;Password=your-password;"
}
```

```json [MySQL]
{
  "ConnectionString": "Server=db.example.com;Port=3306;Database=mydb;Uid=portway;Pwd=your-password;SslMode=Preferred;"
}
```

```json [SQLite]
{
  "ConnectionString": "Data Source=environments/WMS/demo.db;"
}
```

:::

SQLite paths are relative to the working directory.

## Capabilities

| Feature | SQL Server | PostgreSQL | MySQL | SQLite |
|---|:---:|:---:|:---:|:---:|
| OData reads | Yes | Yes | Yes | Yes |
| Writes through stored procedures | Yes | Yes | Yes | No |
| Writes with `"WriteMode": "Table"` | Yes | Yes | Yes | Yes |
| Table-valued functions | Yes | Yes | No | No |
| `$expand` (Table and View) | Yes | Yes | Yes | Yes |
| Schemas | Yes | Yes | Yes | No |

On PostgreSQL, write routines are functions, since only functions return the created row. They are called with named arguments; parameter names match the lowercased payload fields (e.g. `method`, `id`, `name`). On SQL Server and MySQL, a procedure ends with a `SELECT` of the affected row.

## Schemas

| Provider | Default schema |
|---|---|
| SQL Server | `dbo` |
| PostgreSQL | `public` |
| MySQL | Database in the connection string |
| SQLite | None; the prefix is omitted |

Without `DatabaseSchema`, the provider default applies. A `dbo` on a non SQL Server environment maps to that provider's default; other schemas are used as written.

## Related topics

- [Environment Settings](/reference/environment-settings)
- [SQL Endpoints](/guide/endpoints-sql)
- [Expanding Related Data](/reference/expand)
- [Health Checks](/reference/health-checks)
