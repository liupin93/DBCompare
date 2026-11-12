using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DBCompare {
    internal sealed class ComparisonSession {
        readonly App app;
        public readonly FrameworkElement View;
        public readonly TabItem Tab;
        public readonly DataGrid Objects;
        public string Search="", Mode="全部";
        Func<string> statusText=()=>L.T("请输入两个数据库的连接字符串。");
        public string Status {get{return statusText();}}
        public bool Closed {get;private set;}
        public bool StatusIsError {get;private set;}
        bool hasStatus;
        public bool Loading {get;private set;}
        public bool Busy {get{return running!=null || Loading;}}
        public bool CanCancel {get{return running!=null && Get<Button>("Cancel").IsEnabled;}}
        public int Total {get{return results.Count;}}
        public string Name {get {string text=Get<TextBox>("ComparisonName").Text.Trim();return text.Length==0?defaultName:text;}}
        readonly int number;
        string defaultName {get{return left==null?L.F("新比较 {0}",number):HistoryEntry.PairName(left.Name,right.Name);}}
        readonly HistoryEntry origin;
        readonly TextBlock caption;
        Snapshot left,right;
        List<Result> results=new List<Result>();
        CancellationTokenSource running;
        public readonly DatabaseEngine Engine;
        readonly ObjectCategory[] categories;
        public T Get<T>(string name) where T:class {return (T)View.FindName(name);}
        public ComparisonSession(App app,int number,HistoryEntry entry,DatabaseEngine engine) {
            this.app=app;origin=entry;this.number=number;
            Engine=engine;
            var kinds=engine==DatabaseEngine.PostgreSql?new[]{"表","视图","物化视图","存储过程","函数","触发器"}:
                new[]{"表","视图","存储过程","函数","触发器","数据库触发器"};
            categories=kinds.Select(kind=>new ObjectCategory(kind)).ToArray();
            View=(FrameworkElement)((DataTemplate)app.Window.FindResource("ComparisonView")).LoadContent();
            L.Bind(Get<Expander>("Connections"),HeaderedContentControl.HeaderProperty,
                ()=>L.T("数据库连接")+"   ·   "+(Engine==DatabaseEngine.PostgreSql?"PostgreSQL":"SQL Server"));
            if(engine==DatabaseEngine.PostgreSql) {
                foreach(var name in new[]{"LeftConn","RightConn"}) Get<TextBox>(name).ToolTip="Host=localhost;Port=5432;Database=sales;Username=compare;Password=...";
            }
            Tab=new TabItem{Content=View,Tag=this};
            caption=app.TabHeader(Tab,()=>app.Close(Tab));
            Get<TextBox>("ComparisonName").TextChanged+=(s,e)=>Rename();
            if(entry!=null) {
                Get<TextBox>("ComparisonName").Text=entry.Name;
                Get<TextBox>("LeftConn").Text=entry.LeftConnection;Get<TextBox>("RightConn").Text=entry.RightConnection;
            }
            Rename();
            Objects=Get<DataGrid>("Objects");
            foreach(var category in categories) category.PropertyChanged+=(s,e)=>{
                var selected=Objects.SelectedItem as Result;
                if(e.PropertyName=="Expanded" && !category.Expanded && selected!=null && selected.Kind==category.Kind) Objects.SelectedIndex=-1;
            };
            Objects.PreviewMouseDoubleClick+=(s,e)=>{
                var source=e.OriginalSource as DependencyObject;
                var row=source==null?null:ItemsControl.ContainerFromElement(Objects,source) as DataGridRow;
                var result=row==null?null:row.Item as Result;
                if(result!=null) app.OpenAfterDoubleClick(e,()=>{if(!Closed) Open(result);});
            };
            Objects.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Enter && Objects.SelectedItem!=null){Open((Result)Objects.SelectedItem);e.Handled=true;}};
            Get<Button>("Compare").Click+=async (s,e)=>await Compare();
            Get<Button>("Cancel").Click+=(s,e)=>Cancel();
            Filter();
        }
        void Rename() {caption.Text=Name;Tab.ToolTip=Name;}
        public void EditConnections() {Get<Expander>("Connections").IsExpanded=true;Get<TextBox>("LeftConn").Focus();}
        public void ExpandGroups(bool value) {foreach(var category in categories) category.Expanded=value;}
        public void Cancel() {if(running!=null) running.Cancel();}
        public void Dispose() {Closed=true;Cancel();}
        public void Open(Result result) {if(left!=null && right!=null) app.OpenDetail(this,result,left.Name,right.Name);}
        public void SetStatus(string message,bool error=false) {
            SetStatus(()=>L.T(message),error);
        }
        void SetStatus(Func<string> message,bool error=false) {
            statusText=message;
            hasStatus=true;StatusIsError=error;
            if(left==null) Get<StackPanel>("Empty").Visibility=Visibility.Collapsed;
            app.SyncToolbar();
        }
        void PromptForConnection(TextBox field,Func<string> message) {
            Get<Expander>("Connections").IsExpanded=true;
            field.Focus();
            SetStatus(message,true);
        }
        public void JumpOverview(int step) {
            int n=Objects.Items.Count;
            int start=Objects.SelectedIndex<0?(step>0?-1:0):Objects.SelectedIndex;
            for(int i=1;i<=n;i++) {
                int idx=(start+step*i+n)%n;
                if(App.MatchMode(((Result)Objects.Items[idx]).Status,"不同")) {
                    categories.First(c=>c.Kind==((Result)Objects.Items[idx]).Kind).Expanded=true;
                    Objects.UpdateLayout();
                    Objects.SelectedIndex=idx;Objects.ScrollIntoView(Objects.SelectedItem);return;
                }
            }
        }
        public void Filter() {
            var selected=Objects.SelectedItem as Result;
            var filtered=results.Where(x=>App.MatchMode(x.Status,Mode) && x.Name.IndexOf(Search,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            var view=new ListCollectionView(filtered);
            view.GroupDescriptions.Add(new ObjectGroups(categories,filtered));
            Objects.ItemsSource=view;
            if(selected!=null && view.Contains(selected)) Objects.SelectedItem=selected;
            bool showEmpty=filtered.Count==0 && (left!=null || !hasStatus);
            Get<StackPanel>("Empty").Visibility=showEmpty?Visibility.Visible:Visibility.Collapsed;
            UpdateEmptyLabels();
            app.SyncToolbar();
        }
        void UpdateEmptyLabels() {
            Get<TextBlock>("EmptyTitle").Text=L.T(left==null?"等待比较":results.Count==0?"数据库中没有可比较的对象":"没有匹配的对象");
            Get<TextBlock>("EmptyHint").Text=L.T(left==null?"在上方粘贴两个库的连接字符串，然后点击「开始比较」。":results.Count==0?"":"可以切换顶部显示条件，或修改查找内容。");
        }
        async Task<Snapshot> Side(string connection,string side,CancellationTokenSource active) {
            try {return await app.LoadSnapshot(Engine,connection,active.Token).ConfigureAwait(false);}
            catch(Exception e) {
                if(active.IsCancellationRequested) throw new OperationCanceledException(active.Token);
                active.Cancel();
                if(Engine==DatabaseEngine.PostgreSql) throw new ConnectionReadException(()=>L.F("{0}：{1}",L.T(side),PostgresDatabase.ErrorMessage(e)),e);
                // Connection string parser errors can echo user input. Never display their raw message.
                var sql=e as SqlException;
                Func<string> message=()=>sql==null ? (e is ArgumentException ? L.T("请检查连接字符串的格式、Server 和 Database。") : L.F("读取数据库结构失败（{0}）。",e.GetType().Name)) :
                    sql.Number==50001 ? L.T("当前账号缺少数据库级 VIEW DEFINITION 权限。") :
                    sql.Number==18456 ? L.T("登录失败，请检查身份验证方式和凭据。") :
                    sql.Number==4060 ? L.T("无法打开指定数据库，请检查库名和访问权限。") :
                    sql.Number==-2 ? L.T("操作超时，请检查网络或稍后重试。") :
                    L.F("SQL Server 错误 {0}：{1}",sql.Number,sql.Errors[0].Message);
                throw new ConnectionReadException(()=>L.F("{0}：{1}",L.T(side),message()),e);
            }
        }
        public async Task Compare() {
            if(Busy || Closed) return;
            var a=Get<TextBox>("LeftConn");var b=Get<TextBox>("RightConn");
            string ca=a.Text,cb=b.Text;
            if(String.IsNullOrWhiteSpace(ca) || String.IsNullOrWhiteSpace(cb)) {
                bool missingLeft=String.IsNullOrWhiteSpace(ca);
                PromptForConnection(missingLeft?a:b,()=>L.F("请输入{0}数据库的连接字符串。",L.T(missingLeft?"左侧":"右侧")));
                return;
            }
            if(origin!=null) {
                bool missingLeft=NeedsLegacyPassword(ca,origin.LeftNeedsPassword);
                if(missingLeft || NeedsLegacyPassword(cb,origin.RightNeedsPassword)) {
                    PromptForConnection(missingLeft?a:b,()=>L.F("这条旧履历未保存登录密码，请补全{0}连接字符串中的 Password 后开始比较。",L.T(missingLeft?"左侧":"右侧")));
                    return;
                }
            }
            var active=running=new CancellationTokenSource();
            string comparisonName=Get<TextBox>("ComparisonName").Text.Trim();
            SetBusy(true);SetStatus("正在读取两个数据库的结构…");
            try {
                var pair=await Task.WhenAll(Task.Run(()=>Side(ca,"左侧",active)),Task.Run(()=>Side(cb,"右侧",active)));
                active.Token.ThrowIfCancellationRequested();
                SetStatus("已读取两侧数据库，正在比较结构…");
                var compared=await Task.Run(()=>Result.Compare(pair[0],pair[1]));
                active.Token.ThrowIfCancellationRequested();
                Get<Button>("Cancel").IsEnabled=false;
                ShowResults(pair[0],pair[1],compared);
                string name=comparisonName.Length==0?defaultName:comparisonName;
                try {
                    await Task.Run(()=>app.History.Save(pair[0],pair[1],compared,ca,cb,name));
                    app.RefreshHistory();
                } catch(Exception ex) {if(!Closed) {
                    var previous=statusText;SetStatus(()=>previous()+"   ·   "+L.F("履历保存失败：{0}",ex.Message),true);
                }}
            } catch(OperationCanceledException) {if(!Closed) SetStatus("比较已取消。");}
            catch(Exception ex) {if(!Closed) {
                Get<Expander>("Connections").IsExpanded=true;
                SetStatus(()=>L.F("比较失败。{0}",ex.Message)+(left==null?"":" "+L.T("下方保留上次比较结果。")),true);
            }}
            finally {running=null;active.Dispose();if(!Closed) SetBusy(false);}
        }
        static bool NeedsLegacyPassword(string input,bool required) {
            if(!required) return false;
            try {var conn=new SqlConnectionStringBuilder(input);return !conn.IntegratedSecurity && conn.Password.Length==0;}
            catch(ArgumentException) {return false;}
        }
        void SetBusy(bool busy) {
            Get<Button>("Compare").IsEnabled=Get<TextBox>("LeftConn").IsEnabled=Get<TextBox>("RightConn").IsEnabled=
                Get<TextBox>("ComparisonName").IsEnabled=!busy;
            var cancel=Get<Button>("Cancel");cancel.IsEnabled=busy && running!=null;
            cancel.Visibility=cancel.IsEnabled?Visibility.Visible:Visibility.Collapsed;
            Get<Button>("Compare").Content=L.T(busy?(Loading?"读取快照中…":"比较中…"):"开始比较");
            app.SyncToolbar();
        }
        public async Task ReadHistory() {
            Loading=true;SetBusy(true);SetStatus("正在读取历史快照…");
            try {
                var pair=await Task.Run(()=>app.History.Open(origin));
                if(Closed) return;
                var compared=await Task.Run(()=>Result.Compare(pair[0],pair[1]));
                if(Closed) return;
                ShowResults(pair[0],pair[1],compared);
            } catch(Exception ex) {if(!Closed) SetStatus(()=>L.F("无法打开历史快照：{0}",L.T(ex.Message)),true);}
            finally {Loading=false;if(!Closed) SetBusy(false);}
        }
        internal void ShowResults(Snapshot a,Snapshot b,List<Result> compared) {
            left=a;right=b;results=compared;
            app.CloseDetails(this);Filter();RefreshLabels();
            Get<Expander>("Connections").IsExpanded=false;
            int unreadable=results.Count(r=>r.Status==Result.Unknown);
            string completed=Loading?"历史快照":"比较完成", time=Loading?origin.Time:DateTime.Now.ToString("HH:mm:ss");
            int total=results.Count,different=results.Count(r=>App.MatchMode(r.Status,"不同")),same=results.Count(r=>r.Status==Result.Same);
            SetStatus(()=>L.T(completed)+"   "+time+"   ·   "+L.F("共 {0} 个对象   ·   {1} 项不同   ·   {2} 项相同",total,different,same)+
                (unreadable>0?"   ·   "+L.F("{0} 项无法判定",unreadable):"")+"      "+L.T("双击对象查看明细"));
        }
        internal void RefreshLanguage() {
            foreach(var category in categories) category.RefreshLabel();
            Objects.Items.Refresh();
            UpdateEmptyLabels();RefreshLabels();
        }
        void RefreshLabels() {
            Rename();
            Objects.Columns[0].Header=L.T("左侧对象")+(left==null?"":"    ·    "+left.Name);
            Objects.Columns[2].Header=L.T("右侧对象")+(right==null?"":"    ·    "+right.Name);
            Get<Button>("Compare").Content=L.T(Busy?(Loading?"读取快照中…":"比较中…"):"开始比较");
        }
        sealed class ConnectionReadException : Exception {
            readonly Func<string> text;
            public ConnectionReadException(Func<string> text,Exception cause):base(null,cause) {this.text=text;}
            public override string Message {get{return text();}}
        }
    }
}
