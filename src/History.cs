using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DBCompare {
    internal sealed class HistoryEntry {
        public DatabaseEngine Engine {get;internal set;}
        internal string Path, LeftConnection, RightConnection;
        internal bool LeftNeedsPassword, RightNeedsPassword;
        public DateTime CompletedUtc { get; internal set; }
        public string Time { get { return CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); } }
        public string LeftName { get; internal set; }
        public string RightName { get; internal set; }
        public string Name { get; internal set; }
        public static string PairName(string left,string right) {
            string a=left.Split(new[]{" / "},StringSplitOptions.None).Last();
            string b=right.Split(new[]{" / "},StringSplitOptions.None).Last();
            return a==b?left+" ↔ "+right:a+" ↔ "+b;
        }
        public int Total { get; internal set; }
        public int Different { get; internal set; }
        public int Unreadable { get; internal set; }
        public string Summary { get { return L.F("{0} 项不同",Different)+(Unreadable==0?"":" · "+L.F("{0} 项无法判定",Unreadable)); } }
    }

    // Each completed run is a separate file. The home page reads only its header.
    internal sealed class HistoryStore {
        readonly string folder;
        const int Version=4;
        public HistoryStore(string folder) { this.folder=folder; }
        public static string DefaultFolder {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DBCompare","History"); }
        }
        public HistoryEntry Save(Snapshot left,Snapshot right,List<Result> results,string ca,string cb,string name=null) {
            if(left.Engine!=right.Engine) throw new InvalidOperationException(L.T("数据库类型不匹配。"));
            var entry=new HistoryEntry {Engine=left.Engine,CompletedUtc=DateTime.UtcNow,LeftName=left.Name,RightName=right.Name,
                Total=results.Count,Different=results.Count(r=>App.MatchMode(r.Status,"不同")),Unreadable=results.Count(r=>r.Status==Result.Unknown)};
            entry.Name=String.IsNullOrWhiteSpace(name)?HistoryEntry.PairName(left.Name,right.Name):name;
            entry.LeftConnection=ca;entry.RightConnection=cb;
            Directory.CreateDirectory(folder);
            entry.Path=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+".dbch");
            string temp=entry.Path+".tmp";
            try {
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    WriteHeader(file,entry);
                    using(var zip=new GZipStream(file,CompressionLevel.Fastest,true))
                    using(var w=new BinaryWriter(zip)) {WriteObjects(w,left);WriteObjects(w,right);}
                    file.Flush(true);
                }
                File.Move(temp,entry.Path);
            } finally { if(File.Exists(temp)) File.Delete(temp); }
            return entry;
        }
        static void WriteHeader(Stream stream,HistoryEntry entry) {
            // Version 3 retains the missing-password flags when naming an older snapshot.
            int version=entry.Engine!=DatabaseEngine.SqlServer?Version:entry.LeftNeedsPassword || entry.RightNeedsPassword?3:2;
            using(var w=new BinaryWriter(stream,Encoding.UTF8,true)) {
                w.Write("DBCompare");w.Write(version);w.Write(entry.CompletedUtc.Ticks);
                w.Write(entry.LeftName);w.Write(entry.RightName);w.Write(entry.Total);w.Write(entry.Different);w.Write(entry.Unreadable);
                w.Write(entry.LeftConnection);w.Write(entry.RightConnection);w.Write(entry.Name);
                if(version==3) {w.Write(entry.LeftNeedsPassword);w.Write(entry.RightNeedsPassword);}
                if(version==4) w.Write((byte)entry.Engine);
            }
        }
        static void WriteObjects(BinaryWriter w,Snapshot snapshot) {
            w.Write(snapshot.Objects.Count);
            foreach(var obj in snapshot.Objects.Values) {
                w.Write(obj.Kind);w.Write(obj.Schema!=null);if(obj.Schema!=null) w.Write(obj.Schema);
                w.Write(obj.Name);w.Write(obj.Unknown);w.Write(obj.Sql!=null);if(obj.Sql!=null) w.Write(obj.Sql);
                w.Write(obj.Props.Count);
                foreach(var pair in obj.Props) {w.Write(pair.Key);w.Write(pair.Value);}
            }
        }
        static int Count(BinaryReader r) {
            int count=r.ReadInt32();
            if(count<0) throw new InvalidDataException("无效的履历计数。");
            return count;
        }
        static Snapshot ReadObjects(BinaryReader r,string name,DatabaseEngine engine) {
            var snapshot=new Snapshot{Name=name,Engine=engine};
            int objects=Count(r);
            for(int i=0;i<objects;i++) {
                var obj=new Item{Kind=r.ReadString(),Schema=r.ReadBoolean()?r.ReadString():null,Name=r.ReadString(),
                    Unknown=r.ReadBoolean(),Sql=r.ReadBoolean()?r.ReadString():null};
                int properties=Count(r);
                for(int j=0;j<properties;j++) obj.Props.Add(r.ReadString(),r.ReadString());
                snapshot.Add(obj);
            }
            return snapshot;
        }
        static HistoryEntry ReadHeader(BinaryReader r,string path) {
            if(r.ReadString()!="DBCompare") throw new InvalidDataException("无法识别履历。");
            int version=r.ReadInt32();
            if(version<1 || version>Version) throw new InvalidDataException("无法识别履历版本。");
            var entry=new HistoryEntry{Path=path,CompletedUtc=new DateTime(r.ReadInt64(),DateTimeKind.Utc),
                LeftName=r.ReadString(),RightName=r.ReadString(),Total=Count(r),Different=Count(r),Unreadable=Count(r),
                LeftConnection=r.ReadString(),RightConnection=r.ReadString()};
            entry.Name=version==1?HistoryEntry.PairName(entry.LeftName,entry.RightName):r.ReadString();
            if(version==1 || version==3) {
                entry.LeftNeedsPassword=r.ReadBoolean();entry.RightNeedsPassword=r.ReadBoolean();
            }
            if(version==4) {
                entry.Engine=(DatabaseEngine)r.ReadByte();
                if(entry.Engine!=DatabaseEngine.PostgreSql) throw new InvalidDataException("无法识别履历版本。");
            }
            return entry;
        }
        public List<HistoryEntry> List(out int skipped) {
            skipped=0;
            var entries=new List<HistoryEntry>();
            if(!Directory.Exists(folder)) return entries;
            foreach(string path in Directory.EnumerateFiles(folder,"*.dbch")) {
                try {using(var r=new BinaryReader(OpenFile(path))) entries.Add(ReadHeader(r,path));}
                catch(IOException) {skipped++;}
                catch(InvalidDataException) {skipped++;}
                catch(ArgumentException) {skipped++;}
                catch(FormatException) {skipped++;}
                catch(UnauthorizedAccessException) {skipped++;}
            }
            return entries.OrderByDescending(e=>e.CompletedUtc).ToList();
        }
        public Snapshot[] Open(HistoryEntry entry) {
            using(var file=OpenFile(entry.Path))
            using(var header=new BinaryReader(file,Encoding.UTF8,true)) {
                var info=ReadHeader(header,entry.Path);
                using(var zip=new GZipStream(file,CompressionMode.Decompress))
                using(var r=new BinaryReader(zip)) {
                    var pair=new[]{ReadObjects(r,info.LeftName,info.Engine),ReadObjects(r,info.RightName,info.Engine)};
                    if(zip.ReadByte()!=-1) throw new InvalidDataException("履历包含多余的数据。");
                    return pair;
                }
            }
        }
        static FileStream OpenFile(string path) {
            return new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        }
        public void Rename(HistoryEntry entry,string value) {
            if(String.IsNullOrWhiteSpace(value)) throw new ArgumentException("请输入保存名称。");
            string name=value.Trim();
            string temp=entry.Path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var input=OpenFile(entry.Path))
                using(var reader=new BinaryReader(input,Encoding.UTF8,true)) {
                    var info=ReadHeader(reader,entry.Path);
                    info.Name=name;
                    using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        WriteHeader(output,info);
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                }
                File.Replace(temp,entry.Path,null);
                entry.Name=name;
            } finally {if(File.Exists(temp)) File.Delete(temp);}
        }
        public void Delete(HistoryEntry entry) {File.Delete(entry.Path);}
    }
}
