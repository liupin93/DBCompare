using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace DBCompare {
    internal sealed class Database {
        readonly DbDataReader reader;
        readonly Dictionary<int,Item> ids = new Dictionary<int,Item>();
        readonly Snapshot snapshot;
        Item current;
        Database(DbDataReader reader, Snapshot snapshot) { this.reader=reader; this.snapshot=snapshot; }

        public static async Task<Snapshot> Load(string input, CancellationToken token) {
            var settings = new SqlConnectionStringBuilder(input);
            if (String.IsNullOrWhiteSpace(settings.DataSource) || String.IsNullOrWhiteSpace(settings.InitialCatalog))
                throw new ArgumentException("连接字符串需要包含 Server 和 Database（或 Initial Catalog）。");
            if (settings.AttachDBFilename.Length>0 || settings.UserInstance || settings.ContextConnection)
                throw new ArgumentException("请连接已存在的数据库实例。");
            settings.PersistSecurityInfo=false;
            settings.ApplicationName="DBCompare";
            using(var connection=new SqlConnection(settings.ConnectionString)) {
                await connection.OpenAsync(token).ConfigureAwait(false);
                var snap=new Snapshot{Name=connection.DataSource+" / "+connection.Database};
                using(var cmd=new SqlCommand(Assets.Read("Catalog.sql"),connection)) {
                    cmd.CommandTimeout=90;
                    using(var cancel=token.Register(()=>{try {cmd.Cancel();} catch(InvalidOperationException) {} }))
                    using(var reader=await cmd.ExecuteReaderAsync(token).ConfigureAwait(false)) {
                        ReadCatalog(reader,snap,token);
                    }
                }
                return snap;
            }
        }
        internal static void ReadCatalog(DbDataReader reader,Snapshot snapshot,CancellationToken token) {
            var db=new Database(reader,snapshot);
            Action[] sets={db.Object,db.Ddl,db.Column,db.Index,db.IndexColumn,db.ForeignKey,db.Check,db.Event};
            for(int i=0;i<sets.Length;i++) {
                if(i>0 && !reader.NextResult()) throw new InvalidOperationException("元数据结果集不完整。");
                while(reader.Read()) {token.ThrowIfCancellationRequested();sets[i]();}
            }
            reader.NextResult();
        }
        string V(string field) {
            object value=reader[field];
            if(value==DBNull.Value) return null;
            if(value is bool) return (bool)value ? "是" : "否";
            return Convert.ToString(value,CultureInfo.InvariantCulture);
        }
        void Put(string key,string value) { if(value!=null) current.Props[key]=value; }
        void Fields(string group,params string[] pairs) {
            for(int i=0;i<pairs.Length;i+=2) Put(group+pairs[i+1],V(pairs[i]));
        }
        string Group(string category,string field) { return category+" "+Item.Quote(V(field))+" / "; }
        bool SelectItem() { return ids.TryGetValue((int)reader["object_id"],out current); }
        void Add(string kind,string schema) {
            current=new Item{Kind=kind,Schema=schema,Name=V("name"),Sql=V("definition")};
            current.Unknown=kind!="表" && current.Sql==null;
            snapshot.Add(current);
            ids.Add((int)reader["object_id"],current);
        }
        void Object() {
            var type=V("kind");
            string kind=type=="U"?"表":type=="V"?"视图":type=="P"?"存储过程":type=="TR"?"触发器":"函数";
            Add(kind,V("schema_name"));
            Put("对象 / 类型",type);
            Fields("表 / ","is_memory_optimized","内存优化","durability_desc","持久性");
            Fields("模块 / ","uses_ansi_nulls","ANSI_NULLS","uses_quoted_identifier","QUOTED_IDENTIFIER",
                "is_schema_bound","SCHEMABINDING");
            if(V("execute_as_principal_id")!=null) Put("模块 / 执行身份",V("principal_name"));
            Fields("触发器 / ","is_disabled","已禁用","is_instead_of_trigger","INSTEAD OF",
                "is_not_for_replication","NOT FOR REPLICATION","parent_schema","所属架构","parent_name","所属对象");
        }
        void Ddl() {
            Add("数据库触发器",null);
            Fields("模块 / ","uses_ansi_nulls","ANSI_NULLS","uses_quoted_identifier","QUOTED_IDENTIFIER",
                "principal_name","执行身份","is_disabled","已禁用");
        }
        void Column() {
            if(!SelectItem()) return;
            var g=Group("字段","name");
            Fields(g,"position","列序","type_name","数据类型","max_length","最大长度 (字节)","precision","精度",
                "scale","小数位","is_nullable","允许 NULL","collation_name","排序规则","is_rowguidcol","ROWGUIDCOL",
                "is_sparse","稀疏列","is_column_set","列集","is_filestream","FILESTREAM","is_ansi_padded","ANSI_PADDING",
                "is_identity","标识列","seed_value","标识种子","increment_value","标识增量","is_not_for_replication","NOT FOR REPLICATION",
                "is_computed","计算列","expression","计算表达式","is_persisted","持久化","default_name","默认约束名",
                "default_value","默认值","is_xml_document","XML DOCUMENT","xml_collection","XML 架构集合");
            if((V("default_name")!=null && V("default_value")==null) || (V("is_computed")=="是" && V("expression")==null)) current.Unknown=true;
        }
        void Index() {
            if(!SelectItem()) return;
            var g=Group("索引","name");
            Fields(g,"type_desc","类型","is_unique","唯一","is_primary_key","主键","is_unique_constraint","唯一约束",
                "is_disabled","已禁用","has_filter","过滤索引","filter_definition","过滤条件","ignore_dup_key","忽略重复键",
                "fill_factor","填充因子","is_padded","PAD_INDEX","allow_row_locks","行锁","allow_page_locks","页锁","bucket_count","桶数");
            if(V("has_filter")=="是" && V("filter_definition")==null) current.Unknown=true;
        }
        void IndexColumn() {
            if(!SelectItem()) return;
            var g=Group("索引","index_name")+Group("列","column_name");
            Fields(g,"key_ordinal","键顺序","is_descending_key","降序","is_included_column","包含列","partition_ordinal","分区序号");
        }
        void ForeignKey() {
            if(!SelectItem()) return;
            var g=Group("外键","name");
            Fields(g,"ref_schema","引用架构","ref_table","引用表","delete_referential_action_desc","删除规则",
                "update_referential_action_desc","更新规则","is_disabled","已禁用","is_not_trusted","未受信任",
                "is_not_for_replication","NOT FOR REPLICATION");
            Fields(g+"列 "+V("constraint_column_id")+" / ","local_column","本列","ref_column","引用列");
        }
        void Check() {
            if(!SelectItem()) return;
            Fields(Group("检查约束","name"),"definition","条件","is_disabled","已禁用","is_not_trusted","未受信任",
                "is_not_for_replication","NOT FOR REPLICATION","bound_column","绑定列");
            if(V("definition")==null) current.Unknown=true;
        }
        void Event() {
            if(SelectItem()) Fields("触发事件 "+V("type_desc")+" / ","is_first","首先执行","is_last","最后执行");
        }
    }
}
