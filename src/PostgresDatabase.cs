using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace DBCompare {
    internal static class PostgresDatabase {
        internal static string ErrorMessage(Exception e) {
            if(e is NotSupportedException) return L.T("需要 PostgreSQL 14 或更高版本。");
            if(e is ArgumentException || e is FormatException) return L.T("请检查 PostgreSQL 连接字符串的 Host 和 Database。");
            var pg=e as PostgresException;
            if(pg!=null) {
                if(pg.SqlState=="28P01" || pg.SqlState=="28000") return L.T("登录失败，请检查身份验证方式和凭据。");
                if(pg.SqlState=="3D000") return L.T("无法打开指定数据库，请检查库名和访问权限。");
                return L.F("PostgreSQL 错误 {0}：{1}",pg.SqlState,pg.MessageText);
            }
            return L.F("无法读取 PostgreSQL 结构，请检查网络、SSL 和连接设置（{0}）。",e.GetType().Name);
        }
        public static async Task<Snapshot> Load(string input,CancellationToken token) {
            var settings=new NpgsqlConnectionStringBuilder(input);
            if(String.IsNullOrWhiteSpace(settings.Host) || String.IsNullOrWhiteSpace(settings.Database))
                throw new ArgumentException("Host / Database");
            settings.ApplicationName="DBCompare";
            using(var connection=new NpgsqlConnection(settings.ConnectionString)) {
                await connection.OpenAsync(token).ConfigureAwait(false);
                if(connection.PostgreSqlVersion.Major<14) throw new NotSupportedException("PostgreSQL 14+");
                using(var tx=connection.BeginTransaction(IsolationLevel.RepeatableRead))
                using(var cmd=connection.CreateCommand()) {
                    cmd.Transaction=tx;cmd.CommandTimeout=90;
                    cmd.CommandText="SET TRANSACTION READ ONLY; SET LOCAL search_path=pg_catalog; SET LOCAL standard_conforming_strings=on; "+
                        "SET LOCAL quote_all_identifiers=off; SET LOCAL DateStyle='ISO, YMD'; SET LOCAL IntervalStyle=postgres; SET LOCAL TimeZone='UTC'";
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    cmd.CommandText=Assets.Read("PostgresCatalog.sql");
                    var snap=new Snapshot{Engine=DatabaseEngine.PostgreSql,Name=connection.Host+":"+connection.Port+" / "+connection.Database};
                    using(var r=await cmd.ExecuteReaderAsync(token).ConfigureAwait(false)) {
                        var relations=new Dictionary<long,Item>();
                        while(await r.ReadAsync(token).ConfigureAwait(false)) {
                            string kind=V(r,"kind");
                            var item=Object(r,kind=="v"?"视图":kind=="m"?"物化视图":"表");
                            Fields(r,item,"对象 / ","relation_type","类型","persistence","持久性","partition_key","分区键",
                                "partition_bound","分区边界","parents","继承来源","options","对象选项");
                            relations.Add(r.GetInt64(0),item);snap.Add(item);
                        }
                        await Next(r,token).ConfigureAwait(false);
                        while(await r.ReadAsync(token).ConfigureAwait(false)) {
                            var item=relations[r.GetInt64(0)];string group=Group("字段",V(r,"name"));
                            Fields(r,item,group,"position","列序","data_type","数据类型","dimensions","数组维数","nullable","允许 NULL",
                                "identity_kind","标识模式","generated_kind","生成模式","collation_name","排序规则",
                                "expression",V(r,"generated_kind")==null?"默认值":"计算表达式","seqstart","标识种子",
                                "seqincrement","标识增量","seqmin","序列最小值","seqmax","序列最大值","seqcache","序列缓存","seqcycle","序列循环");
                        }
                        await Next(r,token).ConfigureAwait(false);
                        while(await r.ReadAsync(token).ConfigureAwait(false))
                            Fields(r,relations[r.GetInt64(0)],Group("约束",V(r,"name")),"definition","SQL 定义",
                                "validated","已验证","deferrable_flag","可延迟","initially_deferred","初始延迟");
                        await Next(r,token).ConfigureAwait(false);
                        while(await r.ReadAsync(token).ConfigureAwait(false))
                            Fields(r,relations[r.GetInt64(0)],Group("索引",V(r,"name")),"definition","SQL 定义","valid","已验证");
                        await Next(r,token).ConfigureAwait(false);
                        while(await r.ReadAsync(token).ConfigureAwait(false)) {
                            var item=Object(r,V(r,"kind")=="p"?"存储过程":"函数");
                            Fields(r,item,"例程 / ","input_types","输入参数类型","language","语言");snap.Add(item);
                        }
                        await Next(r,token).ConfigureAwait(false);
                        while(await r.ReadAsync(token).ConfigureAwait(false)) {
                            var item=Object(r,"触发器");
                            Fields(r,item,"触发器 / ","parent_name","所属关系","enabled_mode","启用模式");snap.Add(item);
                        }
                    }
                    tx.Commit();return snap;
                }
            }
        }
        static async Task Next(DbDataReader r,CancellationToken token) {
            if(!await r.NextResultAsync(token).ConfigureAwait(false)) throw new InvalidOperationException("元数据结果集不完整。");
        }
        static Item Object(DbDataReader r,string kind) {
            string sql=V(r,"definition");
            return new Item{Kind=kind,Schema=V(r,"schema_name"),Name=V(r,"name"),Sql=sql,Unknown=kind!="表" && sql==null};
        }
        static string V(DbDataReader r,string field) {
            object value=r[field];
            if(value==DBNull.Value) return null;
            if(value is bool) return (bool)value?"是":"否";
            return Convert.ToString(value,CultureInfo.InvariantCulture);
        }
        static string Group(string category,string name) {return category+" "+Item.Quote(name)+" / ";}
        static void Fields(DbDataReader r,Item item,string group,params string[] fields) {
            for(int i=0;i<fields.Length;i+=2) {
                string value=V(r,fields[i]);
                if(value!=null) item.Props.Add(group+fields[i+1],value);
            }
        }
    }
}
