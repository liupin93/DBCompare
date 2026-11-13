# DBCompare

[简体中文](README.md) · English

DBCompare is a Windows desktop tool for comparing the schemas of two SQL Server databases (2014 or later) or two PostgreSQL databases (14 or later). It reads database metadata and shows differences side by side. It does not read application data or execute the SQL definitions it compares.

DBCompare requires .NET Framework 4.7.2 or later. Extract the entire release archive and double-click `DBCompare.exe`, keeping its configuration file and the `drivers` folder alongside it. You can also run [bin/DBCompare.exe](bin/DBCompare.exe) from the source checkout. The PostgreSQL driver is included, so you can connect without installing ODBC or the PostgreSQL client.

## Start a comparison

1. On the home page, choose **SQL Server schema comparison** or **PostgreSQL schema comparison**. Enter a connection string for each database and click **Start comparison**. You can view, edit, and copy both strings in plain text. The comparison name is optional. If you leave it blank, the completed comparison tab takes its name from the database pair. Each session opens in its own tab, so you can run several comparisons at once. The **+** button at the lower left starts a SQL Server session.
2. In the overview, expand a category such as **Tables** or **Views** to see its objects. The toolbar can expand or collapse all categories. Use the toolbar or the **View** menu to show all objects, differences, matches, or objects found only on the left or right. Matching objects appear in black. Differences appear in red and include formatting changes and one-sided objects. The **All** filter also shows objects whose definitions could not be read and compared.
3. Double-click an object or select it and press **Enter** to open its details. SQL definitions scroll together with fixed line numbers. A triangle marks the start of each difference block. Click the color strip on the left to jump to a difference, or select a line to see both versions stacked in the **Selected line comparison** pane. Filters apply to the current view, whether it shows objects, SQL lines, or structural properties. Returning to the overview restores its filter and expanded categories.
4. Sky blue highlights differences in indentation, whitespace, and line breaks. Yellow highlights content changes and changed structural properties. A changed line ending appears as ↵. Text inside string literals, quoted identifiers, and comments is compared as written.

On first launch, DBCompare uses the Windows display language if it is Chinese, Japanese, or English, and defaults to English for other languages. To switch, open **Settings → Options → General → Language** and click **OK**. The change applies immediately to open tabs. DBCompare remembers your choice in `%LOCALAPPDATA%\DBCompare\language.txt`. In Chinese, the options menu is **设定 → 选项**. In Japanese, it is **設定 → オプション**.

### Comparison history

After each successful comparison, the home page adds an entry under **Auto-saved**, grouped by date. Search by comparison name, server, or database. Double-click an entry to inspect its saved objects and SQL definitions offline. To read the latest schemas, choose **Compare again**. A new tab opens with the saved settings. You can edit them before clicking **Start comparison**. The status bar at the bottom shows progress and results.

Right-click an entry and choose **Rename…** to change its saved name. Press **Enter** to save or **Esc** to cancel. Choose **Delete history entry…** and confirm to remove the entry and its snapshot. The **−** button at the lower left also deletes the selected entry.

History is stored under `%LOCALAPPDATA%\DBCompare\History` for the current Windows user. Each entry includes the comparison name, both schema snapshots, result counts, and **both complete connection strings in plain text, including passwords**. Older SQL Server snapshots still open. If an older entry omitted a password, enter it before running the comparison again. Each new snapshot records the database engine so DBCompare can select the corresponding driver when you reconnect.

### Connection strings and permissions

**SQL Server with Windows authentication**

```text
Server=SERVER01\INSTANCE;Database=Sales;Integrated Security=True;
```

**SQL Server with a SQL login**

```text
Server=tcp:SERVER02,1433;Database=SalesTest;User ID=compare_user;Password=your_password;
```

The SQL Server login needs access to the target database and database-level `VIEW DEFINITION` permission. An explicit denial on an individual object can still hide it from the comparison.

**PostgreSQL with Npgsql**

```text
Host=db.example.com;Port=5432;Database=sales;Username=compare_user;Password=your_password;
```

The PostgreSQL role must be able to connect to the target database, read the required system catalogs, and call the functions that retrieve object definitions. DBCompare reads each PostgreSQL snapshot in a read-only, repeatable-read transaction.

## SQL Server comparison scope

| Category | What DBCompare compares |
| --- | --- |
| Tables | Column order, types, lengths, precision, nullability, collations, identity and computed columns, defaults, primary keys, unique and check constraints, foreign keys, index keys, and included columns |
| Views | T-SQL definitions, module settings, output column metadata, and indexes |
| Stored procedures and functions | T-SQL definitions, module settings, and execution context |
| Triggers | Definitions, enabled state, and events for DML triggers on tables or views and database-level DDL triggers |

DBCompare matches objects by type, schema name, and object name, using exact, case-sensitive names. It preserves constraint names, including those generated by SQL Server. If a module is encrypted, DBCompare indicates why its definition is unavailable. Permissions, CLR modules, instance-level objects, physical storage layout, and features specific to newer SQL Server versions are outside the comparison scope.

## PostgreSQL comparison scope

| Category | What DBCompare compares |
| --- | --- |
| Tables | For regular and partitioned tables, column order, types, collations, nullability, defaults, generated columns, and identity settings. Also compares partition keys and bounds, inheritance, primary and foreign keys, unique, check, and exclusion constraints, and index definitions including expressions, predicates, and included columns |
| Views | Definitions and output columns for views and materialized views |
| Stored procedures and functions | Definitions matched by schema, name, and input argument types so each overload is compared separately. For SQL and PL/pgSQL routines, DBCompare distinguishes formatting changes from changes inside string literals. Bodies written in other languages are compared as literal text |
| Triggers | Definitions and enabled modes for triggers, matched by schema, owning table or view, and trigger name |

PostgreSQL reconstructs view definitions from its catalogs, so the original formatting is unavailable. DBCompare skips system schemas and internal triggers. Standalone sequences, extension installation state, event triggers, permissions, row-level security policies, and cross-engine migration comparisons are outside the comparison scope.

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Go home / New comparison | Alt+Home / Ctrl+N |
| Edit connections / Start comparison | Ctrl+L / F5 |
| Open selected object / Return to overview | Enter / Ctrl+1 |
| Previous difference / Next difference | Shift+F7 / F7 |
| Close the current comparison or detail tab | Ctrl+W |

## Build from source

Run these commands in PowerShell from the project directory to build the application, run the tests, and create the release archive. The archive is written to `dist/DBCompare-windows.zip`.

```powershell
.\build.ps1
.\build.ps1 -Test
.\build.ps1 -Package
```

The distribution includes Npgsql 8.0.9 and its .NET Framework dependencies. The build checks the DLL hashes against [the package lock file](src/PostgresPackages.lock) and restores missing or mismatched files from the official NuGet feed.

The default tests use sample objects and metadata. To run the PostgreSQL integration tests, set `DBCOMPARE_PG_TEST_ADMIN` to an administrator connection string for a dedicated test instance and run `.\build.ps1 -Test`. The tests create and clean up two randomly named databases and a test role. They start a read-only comparison from the home page, reopen the saved snapshot, and run the comparison again.

## License

Copyright © 2026 liu pin

Contact [lp1060724715@163.com](mailto:lp1060724715@163.com).

DBCompare is licensed under the [Apache License 2.0](LICENSE), which permits use, modification, and distribution, including commercial use, subject to its terms. Bundled third-party components retain their own licenses. See the [third-party notices](bin/drivers/ThirdPartyNotices.txt).
