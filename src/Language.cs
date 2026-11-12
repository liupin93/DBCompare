using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace DBCompare {
    internal sealed class LanguageSettings {
        readonly string path;
        public LanguageSettings(string path) { this.path=path; }
        public static LanguageSettings Default {
            get {return new LanguageSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DBCompare","language.txt"));}
        }
        public static string SystemLanguage(CultureInfo culture) {
            switch(culture.TwoLetterISOLanguageName) {
                case "zh": return "zh-CN";
                case "ja": return "ja-JP";
                default: return "en-US";
            }
        }
        public string Load(CultureInfo system) {
            string initial=SystemLanguage(system);
            try {
                if(File.Exists(path)) {
                    var code=File.ReadAllText(path).Trim();
                    if(L.Supported(code)) return code;
                }
                Save(initial);
            } catch(IOException) {} catch(UnauthorizedAccessException) {}
            return initial;
        }
        public void Save(string code) {
            if(!L.Supported(code)) throw new ArgumentOutOfRangeException("code");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(tmp,code);
                if(!File.Exists(path)) {
                    try {File.Move(tmp,path);return;} catch(IOException) {if(!File.Exists(path)) throw;}
                }
                File.Replace(tmp,path,null);
            } finally {if(File.Exists(tmp)) File.Delete(tmp);}
        }
    }

    // Canonical keys and saved metadata stay independent of the display language.
    internal sealed class L : INotifyPropertyChanged {
        static readonly Dictionary<string,string[]> rows=new Dictionary<string,string[]>(StringComparer.Ordinal);
        static readonly Dictionary<string,string> resourceKeys=new Dictionary<string,string>(StringComparer.Ordinal);
        static readonly L source=new L();
        static volatile string code="zh-CN";
        public string Code {get{return code;}}
        public string this[string key] {get{return T(resourceKeys[key]);}}
        public static string Current {get{return code;}}
        public event PropertyChangedEventHandler PropertyChanged;
        static L() {
            using(var reader=new StringReader(Assets.Read("UiText.tsv"))) {
                string line;
                while((line=reader.ReadLine())!=null) {
                    if(line.Length==0) continue;
                    var cells=line.Split('\t');rows.Add(cells[0],cells);
                    if(cells.Length==4) resourceKeys.Add(cells[3],cells[0]);
                }
            }
        }
        public static bool Supported(string value) {return value=="zh-CN" || value=="ja-JP" || value=="en-US";}
        public static void Use(string value) {
            if(!Supported(value)) throw new ArgumentOutOfRangeException("value");
            code=value;
            var culture=CultureInfo.GetCultureInfo(value);
            CultureInfo.DefaultThreadCurrentUICulture=culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture=culture;
            Application.Current.Resources["UiStrings"]=source;
            var handler=source.PropertyChanged;
            if(handler!=null) {
                handler(source,new PropertyChangedEventArgs("Code"));
                handler(source,new PropertyChangedEventArgs("Item[]"));
            }
        }
        public static string T(string key) {
            string[] row;
            if(!rows.TryGetValue(key,out row)) return key;
            return row[code=="ja-JP"?1:code=="en-US"?2:0];
        }
        public static string F(string key,params object[] args) {
            return String.Format(CultureInfo.GetCultureInfo(code),T(key),args);
        }
        sealed class TextConverter : IValueConverter {
            readonly Func<string> text;
            public TextConverter(Func<string> text) {this.text=text;}
            public object Convert(object v,Type type,object parameter,CultureInfo culture) {return text();}
            public object ConvertBack(object v,Type type,object parameter,CultureInfo culture) {throw new NotSupportedException();}
        }
        // WPF owns the binding lifetime, including controls in inactive tabs.
        public static void Bind(DependencyObject target,DependencyProperty property,Func<string> text) {
            BindingOperations.SetBinding(target,property,new Binding("Code"){Source=source,Mode=BindingMode.OneWay,Converter=new TextConverter(text)});
        }
        sealed class PropertyConverter : IMultiValueConverter {
            public object Convert(object[] values,Type type,object field,CultureInfo culture) {
                var key=values[0] as string;
                if(key==null) return "";
                return (string)field=="Key"?PropertyName(key):PropertyValue(key,values[1] as string);
            }
            public object[] ConvertBack(object value,Type[] types,object field,CultureInfo culture) {throw new NotSupportedException();}
        }
        public static BindingBase PropertyBinding(string field) {
            var binding=new MultiBinding{Mode=BindingMode.OneWay,Converter=new PropertyConverter(),ConverterParameter=field};
            binding.Bindings.Add(new Binding("Key"));binding.Bindings.Add(new Binding(field));
            binding.Bindings.Add(new Binding("Code"){Source=source});
            return binding;
        }

        static string Segment(string segment) {
            if(rows.ContainsKey(segment)) return T(segment);
            int i=segment.IndexOf(' ');
            return i<0?segment:T(segment.Substring(0,i))+segment.Substring(i);
        }
        public static string PropertyName(string key) {
            if(code=="zh-CN") return key;
            var result=new StringBuilder();int start=0;bool quoted=false;
            for(int i=0;i<key.Length;i++) {
                if(!quoted && key[i]=='[') quoted=true;
                else if(quoted && key[i]==']') {
                    if(i+1<key.Length && key[i+1]==']') i++; else quoted=false;
                } else if(!quoted && i+2<key.Length && key[i]==' ' && key[i+1]=='/' && key[i+2]==' ') {
                    result.Append(Segment(key.Substring(start,i-start))).Append(" / ");i+=2;start=i+1;
                }
            }
            return result.Append(Segment(key.Substring(start))).ToString();
        }
        static readonly HashSet<string> flags=new HashSet<string>(StringComparer.Ordinal) {
            "内存优化","ANSI_NULLS","QUOTED_IDENTIFIER","SCHEMABINDING","已禁用","INSTEAD OF","NOT FOR REPLICATION","允许 NULL","ROWGUIDCOL",
            "稀疏列","列集","FILESTREAM","ANSI_PADDING","标识列","计算列","持久化","XML DOCUMENT","唯一","主键","唯一约束","过滤索引",
            "忽略重复键","PAD_INDEX","行锁","页锁","降序","包含列","未受信任","首先执行","最后执行","序列循环","已验证","可延迟","初始延迟"
        };
        public static string PropertyValue(string key,string value) {
            if(value!="是" && value!="否") return value;
            int split=key.LastIndexOf(" / ",StringComparison.Ordinal);
            return flags.Contains(split<0?key:key.Substring(split+3))?T(value):value;
        }
    }
}
