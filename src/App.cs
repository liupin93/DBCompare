using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace DBCompare {
    internal sealed partial class App {
        [STAThread] static void Main() {
            var application=new Application();
            application.Run(new App().Window);
        }
        public readonly Window Window;
        T Get<T>(string name) where T:class {return (T)Window.FindName(name);}
        internal readonly HistoryStore History;
        readonly Func<string,CancellationToken,Task<Snapshot>> sqlLoader;
        internal Task<Snapshot> LoadSnapshot(DatabaseEngine engine,string input,CancellationToken token) {
            return engine==DatabaseEngine.PostgreSql?PostgresDatabase.Load(input,token):sqlLoader(input,token);
        }
        readonly TabControl pages;
        readonly TabItem home;
        readonly TextBox search;
        readonly Dictionary<string,RadioButton> buttons=new Dictionary<string,RadioButton>();
        readonly Dictionary<string,MenuItem> modeMenus=new Dictionary<string,MenuItem>();
        bool syncing;
        int nextNumber;
        ComparisonSession lastSession;
        internal ComparisonSession Current {
            get {var tab=pages.SelectedItem as TabItem;return tab==null?null:tab.Tag as ComparisonSession;}
        }
        bool OnOverview {get{return Current!=null && Current.Tab==pages.SelectedItem;}}
        public App():this(new HistoryStore(HistoryStore.DefaultFolder)) {}
        internal App(HistoryStore history,Func<string,CancellationToken,Task<Snapshot>> loader=null,LanguageSettings settings=null) {
            languageSettings=settings??LanguageSettings.Default;
            L.Use(languageSettings.Load(System.Globalization.CultureInfo.CurrentUICulture));
            Window=(Window)XamlReader.Parse(Assets.Read("Window.xaml"));
            Window.Language=XmlLanguage.GetLanguage(L.Current);
            Get<MenuItem>("OptionsMenu").Click+=(s,e)=>{
                var dialog=OptionsDialog();dialog.Owner=Window;dialog.ShowDialog();
            };
            Get<MenuItem>("HelpContentsMenu").Click+=(s,e)=>ShowHelpWindow("帮助内容","HelpContentView",570);
            Get<MenuItem>("AboutMenu").Click+=(s,e)=>ShowHelpWindow("关于","AboutView",430);
            History=history;sqlLoader=loader??Database.Load;
            pages=Get<TabControl>("Pages");home=Get<TabItem>("Home");search=Get<TextBox>("Search");
            foreach(string label in new[]{"全部","不同","相同","仅左侧","仅右侧"}) {
                var radio=new RadioButton{GroupName="visibility",Style=(Style)Window.FindResource("FilterButton")};
                var menu=new MenuItem{IsCheckable=true};
                string caption=label=="全部"?"全部显示":label.StartsWith("仅")?label+"存在":"只显示"+label;
                L.Bind(radio,ContentControl.ContentProperty,()=>L.T(label));
                L.Bind(menu,HeaderedItemsControl.HeaderProperty,()=>L.T(caption));
                buttons.Add(label,radio);modeMenus.Add(label,menu);
                menu.Click+=(s,e)=>ChangeMode(label);radio.Checked+=(s,e)=>{if(!syncing) ChangeMode(label);};
                Get<MenuItem>("ViewMenu").Items.Add(menu);Get<StackPanel>("Modes").Children.Add(radio);
            }
            var viewMenu=Get<MenuItem>("ViewMenu");viewMenu.Items.Add(new Separator());
            foreach(bool expand in new[]{true,false}) {
                var button=Get<Button>(expand?"ExpandAll":"CollapseAll");var menu=new MenuItem();
                L.Bind(menu,HeaderedItemsControl.HeaderProperty,()=>L.T(expand?"展开全部":"折叠全部"));
                button.Click+=(s,e)=>{if(OnOverview) Current.ExpandGroups(expand);};
                menu.Click+=(s,e)=>{if(OnOverview) Current.ExpandGroups(expand);};
                viewMenu.Items.Add(menu);viewMenu.SubmenuOpened+=(s,e)=>menu.IsEnabled=OnOverview && Current.Objects.Items.Count>0;
            }
            search.TextChanged+=(s,e)=>{if(!syncing && OnOverview) {Current.Search=search.Text;Current.Filter();}};
            Get<Button>("PreviousDiff").Click+=(s,e)=>Jump(-1);Get<Button>("NextDiff").Click+=(s,e)=>Jump(1);
            Get<MenuItem>("EditConnections").Click+=(s,e)=>EditConnections();
            Get<MenuItem>("RunMenu").Click+=(s,e)=>Run();
            Get<MenuItem>("CancelMenu").Click+=(s,e)=>{if(Current!=null) Current.Cancel();};
            Get<MenuItem>("CloseMenu").Click+=(s,e)=>Close(pages.SelectedItem as TabItem);
            Get<MenuItem>("ExitMenu").Click+=(s,e)=>Window.Close();
            Get<MenuItem>("GoOverview").Click+=(s,e)=>GoOverview();
            Get<MenuItem>("OpenMenu").Click+=(s,e)=>{if(OnOverview && Current.Objects.SelectedItem!=null) Current.Open((Result)Current.Objects.SelectedItem);};
            Get<MenuItem>("NextMenu").Click+=(s,e)=>Jump(1);Get<MenuItem>("PrevMenu").Click+=(s,e)=>Jump(-1);
            Get<MenuItem>("NavMenu").SubmenuOpened+=(s,e)=>{
                Get<MenuItem>("OpenMenu").IsEnabled=OnOverview && Current.Objects.SelectedItem!=null;
                Get<MenuItem>("GoOverview").IsEnabled=Current!=null || lastSession!=null;
                Get<MenuItem>("NextMenu").IsEnabled=Get<MenuItem>("PrevMenu").IsEnabled=CurrentJump()!=null;
            };
            pages.SelectionChanged+=(s,e)=>{
                if(e.OriginalSource!=pages) return;
                if(Current!=null) lastSession=Current;
                if(pages.SelectedItem==home) RefreshHistory();
                SyncToolbar();
            };
            Window.Closing+=(s,e)=>{
                foreach(var session in pages.Items.Cast<TabItem>().Select(t=>t.Tag).OfType<ComparisonSession>().Distinct()) session.Dispose();
            };
            Window.PreviewKeyDown+=(s,e)=>{
                if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.N) NewSession(null);
                else if(e.SystemKey==Key.Home && Keyboard.Modifiers==ModifierKeys.Alt) pages.SelectedItem=home;
                else if(e.Key==Key.F5) Run();
                else if(e.Key==Key.F7) Jump(Keyboard.Modifiers==ModifierKeys.Shift?-1:1);
                else if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.L) EditConnections();
                else if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.D1) GoOverview();
                else if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.W) Close(pages.SelectedItem as TabItem);
                else return;
                e.Handled=true;
            };
            InitHome();SyncToolbar();
        }
        void ShowHelpWindow(string title,string template,double width) {
            var dialog=new Window {
                Owner=Window, Title=L.T(title), Width=width, SizeToContent=SizeToContent.Height,
                MaxHeight=SystemParameters.WorkArea.Height*0.85, ResizeMode=ResizeMode.CanResizeWithGrip,
                MinWidth=320, MinHeight=160, ShowInTaskbar=false, WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Language=Window.Language, FontFamily=Window.FontFamily, FontSize=Window.FontSize,
                Background=System.Windows.Media.Brushes.White
            };
            var body=new DockPanel{Margin=new Thickness(20)};
            var close=new Button{Content=L.T("关闭"),IsCancel=true,MinWidth=82,Padding=new Thickness(12,4,12,4),
                HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
            DockPanel.SetDock(close,Dock.Bottom);body.Children.Add(close);
            body.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=((DataTemplate)Window.FindResource(template)).LoadContent()});
            dialog.Content=body;dialog.ShowDialog();
        }
        ComparisonSession NewSession(HistoryEntry entry,DatabaseEngine engine=DatabaseEngine.SqlServer) {
            var session=new ComparisonSession(this,++nextNumber,entry,entry==null?engine:entry.Engine);
            pages.Items.Add(session.Tab);
            ActivatePage(session.Tab);
            return session;
        }
        internal void OpenAfterDoubleClick(MouseButtonEventArgs e,Action open) {
            if(e.ChangedButton!=MouseButton.Left || e.Handled) return;
            e.Handled=true;
            // Finish the source row's mouse/focus processing before activating the target page.
            Window.Dispatcher.BeginInvoke(DispatcherPriority.Normal,open);
        }
        void ActivatePage(TabItem page) {
            pages.SelectedItem=page;
            if(Window.IsActive) page.Focus();
            page.BringIntoView();
        }
        void GoOverview() {
            var session=Current??lastSession;if(session!=null && !session.Closed) pages.SelectedItem=session.Tab;
        }
        void EditConnections() {
            var session=Current??NewSession(null);pages.SelectedItem=session.Tab;session.EditConnections();
        }
        async void Run() {
            if(Current==null && SelectedHistory!=null) {PrepareSelected();return;}
            var session=Current??NewSession(null);pages.SelectedItem=session.Tab;await session.Compare();
        }
        internal TextBlock TabHeader(TabItem tab,Action closeTab) {
            var header=new StackPanel{Orientation=Orientation.Horizontal};
            var text=new TextBlock{MaxWidth=210,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
            var close=new Button{Content="×",Padding=new Thickness(4,0,4,1),Margin=new Thickness(6,0,0,0),BorderThickness=new Thickness(0)};
            L.Bind(close,FrameworkElement.ToolTipProperty,()=>L.T("关闭标签 (Ctrl+W)"));
            close.Click+=(s,e)=>{e.Handled=true;closeTab();};
            header.Children.Add(text);header.Children.Add(close);tab.Header=header;return text;
        }
        internal void CloseDetails(ComparisonSession session) {
            foreach(var tab in pages.Items.Cast<TabItem>().Where(t=>t.Tag==session && t!=session.Tab).ToList()) pages.Items.Remove(tab);
        }
        internal void Close(TabItem tab) {
            if(tab==null || tab==home) return;
            var owner=(ComparisonSession)tab.Tag;
            if(tab==owner.Tab) {owner.Dispose();CloseDetails(owner);}
            else if(pages.SelectedItem==tab) pages.SelectedItem=owner.Tab;
            pages.Items.Remove(tab);
            if(lastSession==owner && owner.Closed) lastSession=pages.Items.Cast<TabItem>().Select(t=>t.Tag).OfType<ComparisonSession>().LastOrDefault();
            SyncToolbar();
        }
        internal void OpenDetail(ComparisonSession owner,Result result,string left,string right) {
            foreach(TabItem existing in pages.Items) if(existing.Tag==owner && existing!=owner.Tab && existing.Uid==result.Key) {
                ActivatePage(existing);return;
            }
            var tab=new TabItem{Tag=owner,Uid=result.Key,ToolTip=owner.Name+"   /   "+result.Name};
            TabHeader(tab,()=>Close(tab)).Text=result.Engine==DatabaseEngine.PostgreSql?result.Name:(result.Left??result.Right).Name;
            tab.Content=DetailView.Create(result,left,right,SyncToolbar);
            pages.Items.Add(tab);ActivatePage(tab);
        }
        DetailControls ActiveDetail() {
            var tab=pages.SelectedItem as TabItem;
            var panel=tab==null?null:tab.Content as FrameworkElement;
            return panel==null?null:panel.Tag as DetailControls;
        }
        void ChangeMode(string value) {
            var detail=ActiveDetail();
            if(detail!=null) {detail.Apply(value);SyncToolbar();}
            else if(OnOverview) {Current.Mode=value;Current.Filter();}
        }
        internal void SyncToolbar() {
            var session=Current;bool onHome=session==null;
            Get<Border>("CompareToolbar").Visibility=onHome?Visibility.Collapsed:Visibility.Visible;
            Get<MenuItem>("ViewMenu").IsEnabled=!onHome;
            Get<MenuItem>("CloseMenu").IsEnabled=!onHome;
            Get<MenuItem>("CancelMenu").IsEnabled=session!=null && session.CanCancel;
            Get<MenuItem>("RunMenu").IsEnabled=session==null || !session.Busy;
            var progress=Get<ProgressBar>("RunProgress");
            progress.IsIndeterminate=OnOverview && session.Busy;
            progress.Visibility=progress.IsIndeterminate?Visibility.Visible:Visibility.Collapsed;
            var area=Get<Border>("StatusArea");
            if(OnOverview && session.StatusIsError) area.Background=System.Windows.Media.Brushes.LemonChiffon;
            else area.ClearValue(Border.BackgroundProperty);
            if(onHome) {Get<TextBlock>("StatusBar").Text=L.T("主页");return;}
            var detail=ActiveDetail();string mode=detail==null?session.Mode:detail.Mode;
            syncing=true;
            try {
                if(search.Text!=session.Search) search.Text=session.Search;
                foreach(var pair in buttons) {
                    pair.Value.IsChecked=modeMenus[pair.Key].IsChecked=pair.Key==mode;
                    pair.Value.IsEnabled=modeMenus[pair.Key].IsEnabled=detail==null || detail.Comparable || pair.Key=="全部";
                }
            } finally {syncing=false;}
            Get<StackPanel>("OverviewTools").Visibility=OnOverview?Visibility.Visible:Visibility.Collapsed;
            Get<StackPanel>("DetailTools").Visibility=OnOverview?Visibility.Collapsed:Visibility.Visible;
            Get<TextBlock>("Tally").Text=detail!=null?detail.Summary():L.F("匹配 {0} / {1} 项",session.Objects.Items.Count,session.Total);
            Get<Button>("NextDiff").IsEnabled=Get<Button>("PreviousDiff").IsEnabled=detail!=null && detail.Jump!=null;
            Get<Button>("ExpandAll").IsEnabled=Get<Button>("CollapseAll").IsEnabled=session.Objects.Items.Count>0;
            Get<TextBlock>("StatusBar").Text=OnOverview?session.Status:L.T("F7 下一处差异     Shift+F7 上一处差异     Ctrl+1 返回所属比较");
        }
        Action<int> CurrentJump() {
            if(OnOverview) return Current.Objects.Items.Cast<Result>().Any(x=>MatchMode(x.Status,"不同"))?(Action<int>)Current.JumpOverview:null;
            var detail=ActiveDetail();return detail==null?null:detail.Jump;
        }
        void Jump(int step) {var jump=CurrentJump();if(jump!=null) jump(step);}
        internal static bool MatchMode(string status,string mode) {
            return mode=="全部" || mode==status || (mode=="不同" && status!=Result.Same && status!=Result.Unknown);
        }
    }
}
