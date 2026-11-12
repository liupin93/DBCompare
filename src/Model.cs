using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DBCompare {
    internal enum DatabaseEngine : byte { SqlServer, PostgreSql }
    internal static class Assets {
        public static string Read(string name) {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(s)) return r.ReadToEnd();
        }
    }

    internal sealed class Item {
        public DatabaseEngine Engine;
        public string Kind, Schema, Name, Sql;
        public bool Unknown;
        public readonly SortedDictionary<string,string> Props = new SortedDictionary<string,string>(StringComparer.Ordinal);
        public string FullName {
            get {
                if(Engine==DatabaseEngine.SqlServer) return Schema==null?Quote(Name):Quote(Schema)+"."+Quote(Name);
                string parent,signature;
                string qualified=PgQuote(Schema)+"."+(Props.TryGetValue("触发器 / 所属关系",out parent)?PgQuote(parent)+".":"")+PgQuote(Name);
                return Props.TryGetValue("例程 / 输入参数类型",out signature)?qualified+"("+signature+")":qualified;
            }
        }
        static string PgQuote(string name) {return "\""+name.Replace("\"","\"\"")+"\"";}
        public bool SqlRoutineBody {
            get {string lang;return Props.TryGetValue("例程 / 语言",out lang) && (lang=="sql" || lang=="plpgsql");}
        }
        public string Key { get { return Kind + "/" + FullName; } }
        public static string Quote(string name) { return "[" + name.Replace("]", "]]") + "]"; }
    }
    internal sealed class Snapshot {
        public DatabaseEngine Engine;
        public string Name;
        public readonly Dictionary<string,Item> Objects = new Dictionary<string,Item>(StringComparer.Ordinal);
        public void Add(Item item) { item.Engine=Engine;Objects.Add(item.Key, item); }
    }
    internal sealed class Result {
        public Item Left, Right;
        public DatabaseEngine Engine {get{return (Left??Right).Engine;}}
        public bool SqlRoutineBody {get{return (Left==null || Left.SqlRoutineBody) && (Right==null || Right.SqlRoutineBody);}}
        public string Status { get; set; }
        public string Kind { get { return (Left ?? Right).Kind; } }
        public string Name { get { return (Left ?? Right).FullName; } }
        public string LeftName { get { return Left==null?"—":Left.FullName; } }
        public string RightName { get { return Right==null?"—":Right.FullName; } }
        public string Key { get { return (Left ?? Right).Key; } }
        int propertyChanges;
        string definitionChange;
        public string DisplayStatus {get{return L.T(Status);}}
        public string Detail {
            get {
                string sql=definitionChange==null?"":L.T(definitionChange);
                if(propertyChanges>0) return L.F("{0} 项属性",propertyChanges)+(sql.Length==0?"":" · "+sql);
                return sql.Length==0?L.T("一致"):sql;
            }
        }
        public const string Same="相同", Format="仅格式不同", Changed="内容不同", OnlyL="仅左侧", OnlyR="仅右侧", Unknown="定义不可读";
        public static string PropertyStatus(string key,string a,string b,DatabaseEngine engine=DatabaseEngine.SqlServer) {
            if(a==b) return Same;
            if(key.EndsWith(" / 默认值",StringComparison.Ordinal) || key.EndsWith(" / 计算表达式",StringComparison.Ordinal)
                || key.EndsWith(" / 过滤条件",StringComparison.Ordinal) || key.EndsWith(" / 条件",StringComparison.Ordinal)
                || key.EndsWith(" / SQL 定义",StringComparison.Ordinal))
                return SqlDiff.Classify(a,b,engine);
            return Changed;
        }

        public static List<Result> Compare(Snapshot a, Snapshot b) {
            if(a.Engine!=b.Engine) throw new InvalidOperationException(L.T("数据库类型不匹配。"));
            var results = new List<Result>();
            foreach (var key in a.Objects.Keys.Union(b.Objects.Keys, StringComparer.Ordinal)) {
                var r = new Result();
                a.Objects.TryGetValue(key,out r.Left);
                b.Objects.TryGetValue(key,out r.Right);
                if (r.Left==null || r.Right==null) {
                    r.Status = r.Left==null ? OnlyR : OnlyL;
                    r.definitionChange = (r.Left ?? r.Right).Unknown ? Unknown : "对象存在性";
                } else {
                    int count = 0, formatting = 0;
                    foreach (var k in r.Left.Props.Keys.Union(r.Right.Props.Keys, StringComparer.Ordinal)) {
                        string l,v;
                        if (!r.Left.Props.TryGetValue(k,out l) || !r.Right.Props.TryGetValue(k,out v)) {count++;continue;}
                        string prop=PropertyStatus(k,l,v,r.Engine);
                        if(prop==Changed) count++;
                        if(prop==Format) formatting++;
                    }
                    bool readable = !r.Left.Unknown && !r.Right.Unknown;
                    string sql = readable ? SqlDiff.Classify(r.Left.Sql,r.Right.Sql,r.Engine,r.SqlRoutineBody) : Unknown;
                    r.Status = count>0 || sql==Changed ? Changed : sql==Same && formatting>0 ? Format : sql;
                    r.propertyChanges=count+formatting;
                    r.definitionChange=sql==Same ? null : sql==Changed ? "SQL 定义" : sql==Format ? "缩进 / 空白 / 换行" : Unknown;
                }
                results.Add(r);
            }
            return results.OrderBy(r=>r.Kind,StringComparer.Ordinal).ThenBy(r=>r.Name,StringComparer.Ordinal).ToList();
        }
    }
}
