-- The caller fixes search_path to pg_catalog inside a read-only repeatable-read transaction.
SELECT c.oid::bigint AS id, n.nspname AS schema_name, c.relname AS name, c.relkind::text AS kind,
       CASE WHEN c.relkind IN ('v','m') THEN pg_get_viewdef(c.oid,false) END AS definition,
       CASE c.relkind WHEN 'r' THEN 'TABLE' WHEN 'p' THEN 'PARTITIONED TABLE' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END AS relation_type,
       CASE c.relpersistence WHEN 'p' THEN 'PERMANENT' WHEN 'u' THEN 'UNLOGGED' ELSE 'TEMPORARY' END AS persistence,
       pg_get_partkeydef(c.oid) AS partition_key,
       pg_get_expr(c.relpartbound,c.oid,false) AS partition_bound,
       (SELECT string_agg(i.inhparent::regclass::text, ', ' ORDER BY i.inhseqno)
          FROM pg_inherits i WHERE i.inhrelid=c.oid) AS parents,
       (SELECT string_agg(opt, ', ' ORDER BY opt) FROM unnest(c.reloptions) opt) AS options
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind IN ('r','p','v','m') AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema';

SELECT a.attrelid::bigint AS id, a.attname AS name,
       row_number() OVER(PARTITION BY a.attrelid ORDER BY a.attnum) AS position,
       format_type(a.atttypid,a.atttypmod) AS data_type, a.attndims AS dimensions,
       NOT a.attnotnull AS nullable,
       CASE a.attidentity WHEN 'a' THEN 'ALWAYS' WHEN 'd' THEN 'BY DEFAULT' END AS identity_kind,
       CASE a.attgenerated WHEN 's' THEN 'STORED' WHEN 'v' THEN 'VIRTUAL' END AS generated_kind,
       CASE WHEN a.attcollation<>0 THEN a.attcollation::regcollation::text END AS collation_name,
       pg_get_expr(d.adbin,d.adrelid,false) AS expression,
       seq.seqstart,seq.seqincrement,seq.seqmin,seq.seqmax,seq.seqcache,seq.seqcycle
FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
LEFT JOIN pg_depend dep ON a.attidentity<>'' AND dep.refclassid='pg_class'::regclass
     AND dep.refobjid=a.attrelid AND dep.refobjsubid=a.attnum AND dep.classid='pg_class'::regclass AND dep.deptype='i'
LEFT JOIN pg_sequence seq ON seq.seqrelid=dep.objid
WHERE a.attnum>0 AND NOT a.attisdropped AND c.relkind IN ('r','p','v','m')
  AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema';

SELECT con.conrelid::bigint AS id, con.conname AS name, pg_get_constraintdef(con.oid,false) AS definition,
       con.convalidated AS validated, con.condeferrable AS deferrable_flag, con.condeferred AS initially_deferred
FROM pg_constraint con JOIN pg_class c ON c.oid=con.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind IN ('r','p','v','m') AND con.contype IN ('p','u','c','f','x') AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema';

SELECT i.indrelid::bigint AS id, idx.relname AS name, pg_get_indexdef(idx.oid,0,false) AS definition, i.indisvalid AS valid
FROM pg_index i JOIN pg_class idx ON idx.oid=i.indexrelid JOIN pg_namespace n ON n.oid=idx.relnamespace
WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema';

SELECT n.nspname AS schema_name,p.proname AS name,p.prokind::text AS kind,l.lanname AS language,
       oidvectortypes(p.proargtypes) AS input_types, pg_get_functiondef(p.oid) AS definition
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace JOIN pg_language l ON l.oid=p.prolang
WHERE p.prokind IN ('f','p') AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema';

SELECT n.nspname AS schema_name,c.relname AS parent_name,t.tgname AS name,
       pg_get_triggerdef(t.oid,false) AS definition,
       CASE t.tgenabled WHEN 'O' THEN 'ORIGIN' WHEN 'D' THEN 'DISABLED' WHEN 'R' THEN 'REPLICA' WHEN 'A' THEN 'ALWAYS' END AS enabled_mode
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE NOT t.tgisinternal AND n.nspname !~ '^pg_' AND n.nspname<>'information_schema';
