SET NOCOUNT ON;
IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'),0) <> 1
    THROW 50001, N'需要当前数据库的 VIEW DEFINITION 权限才能比较对象。', 1;

-- IDs are used only for joining rows within one database.
SELECT o.object_id, s.name AS schema_name, o.name, RTRIM(o.type) AS kind, m.definition,
       m.uses_ansi_nulls, m.uses_quoted_identifier, m.is_schema_bound,
       CASE WHEN m.execute_as_principal_id = -2 THEN N'OWNER'
            ELSE USER_NAME(NULLIF(m.execute_as_principal_id,-2)) END AS principal_name,
       m.execute_as_principal_id, t.is_memory_optimized, t.durability_desc,
       tr.is_disabled, tr.is_instead_of_trigger, tr.is_not_for_replication,
       OBJECT_SCHEMA_NAME(tr.parent_id) AS parent_schema, OBJECT_NAME(tr.parent_id) AS parent_name
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id=o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id
LEFT JOIN sys.tables t ON t.object_id=o.object_id
LEFT JOIN sys.triggers tr ON tr.object_id=o.object_id
WHERE o.is_ms_shipped=0 AND o.type IN ('U','V','P','FN','IF','TF','TR');

-- Database-scoped DDL triggers are absent from sys.objects.
SELECT tr.object_id, tr.name, m.definition, m.uses_ansi_nulls, m.uses_quoted_identifier,
       tr.is_disabled, CASE WHEN m.execute_as_principal_id=-2 THEN N'OWNER'
       WHEN m.execute_as_principal_id IS NOT NULL THEN USER_NAME(m.execute_as_principal_id) END AS principal_name
FROM sys.triggers tr LEFT JOIN sys.sql_modules m ON m.object_id=tr.object_id
WHERE tr.parent_class=0 AND tr.is_ms_shipped=0 AND tr.type='TR';

SELECT c.object_id,c.name,ROW_NUMBER() OVER(PARTITION BY c.object_id ORDER BY c.column_id) AS position,
       QUOTENAME(SCHEMA_NAME(ty.schema_id))+N'.'+QUOTENAME(ty.name) AS type_name,
       c.max_length,c.precision,c.scale,c.is_nullable,c.collation_name,c.is_rowguidcol,c.is_sparse,
       c.is_column_set,c.is_filestream,c.is_ansi_padded,c.is_identity,c.is_computed,
       ic.seed_value,ic.increment_value,ic.is_not_for_replication,
       cc.definition AS expression,cc.is_persisted,OBJECT_NAME(c.default_object_id) AS default_name,
       OBJECT_DEFINITION(c.default_object_id) AS default_value,
       c.is_xml_document, QUOTENAME(SCHEMA_NAME(x.schema_id))+N'.'+QUOTENAME(x.name) AS xml_collection
FROM sys.columns c JOIN sys.objects t ON t.object_id=c.object_id AND t.is_ms_shipped=0 AND t.type IN ('U','V')
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
LEFT JOIN sys.xml_schema_collections x ON x.xml_collection_id=c.xml_collection_id;

SELECT i.object_id, i.name, i.type_desc,i.is_unique,i.is_primary_key,i.is_unique_constraint,
       i.is_disabled,i.has_filter,i.filter_definition,i.ignore_dup_key,i.fill_factor,
       i.is_padded,i.allow_row_locks,i.allow_page_locks,h.bucket_count
FROM sys.indexes i JOIN sys.objects o ON o.object_id=i.object_id
LEFT JOIN sys.hash_indexes h ON h.object_id=i.object_id AND h.index_id=i.index_id
WHERE o.is_ms_shipped=0 AND o.type IN ('U','V') AND i.index_id>0 AND i.is_hypothetical=0;

SELECT i.object_id,i.name AS index_name,c.name AS column_name,ic.index_column_id,ic.key_ordinal,
       ic.is_descending_key,ic.is_included_column,ic.partition_ordinal
FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
JOIN sys.objects o ON o.object_id=i.object_id
WHERE o.is_ms_shipped=0 AND o.type IN ('U','V') AND i.index_id>0 AND i.is_hypothetical=0;

SELECT f.parent_object_id AS object_id,f.name,SCHEMA_NAME(rt.schema_id) AS ref_schema,
       rt.name AS ref_table,f.delete_referential_action_desc,f.update_referential_action_desc,
       f.is_disabled,f.is_not_trusted,f.is_not_for_replication,fc.constraint_column_id,
       pc.name AS local_column,rc.name AS ref_column
FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
JOIN sys.tables rt ON rt.object_id=f.referenced_object_id
JOIN sys.columns pc ON pc.object_id=f.parent_object_id AND pc.column_id=fc.parent_column_id
JOIN sys.columns rc ON rc.object_id=f.referenced_object_id AND rc.column_id=fc.referenced_column_id;

SELECT ch.parent_object_id AS object_id,ch.name,ch.definition,ch.is_disabled,ch.is_not_trusted,
       ch.is_not_for_replication,COL_NAME(ch.parent_object_id,ch.parent_column_id) AS bound_column
FROM sys.check_constraints ch WHERE ch.is_ms_shipped=0;

SELECT e.object_id,e.type_desc,e.is_first,e.is_last FROM sys.trigger_events e;
