using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DBCompare {
    internal sealed partial class App {
        List<HistoryEntry> entries=new List<HistoryEntry>();
        readonly HashSet<DateTime> expandedDates=new HashSet<DateTime>();
        bool firstHistory=true, autoExpanded=true;
        HistoryEntry editingName;
        TreeView Tree {get{return Get<TreeView>("HistoryTree");}}
        HistoryEntry SelectedHistory {get{var node=Tree.SelectedItem as TreeViewItem;return node==null?null:node.Tag as HistoryEntry;}}

        void InitHome() {
            Get<MenuItem>("GoHome").Click+=(s,e)=>pages.SelectedItem=home;
            Get<MenuItem>("NewMenu").Click+=(s,e)=>NewSession(null);
            Get<Button>("NewComparison").Click+=(s,e)=>NewSession(null);
            Get<Button>("AddSession").Click+=(s,e)=>NewSession(null);
            Get<Button>("OpenHistory").Click+=async (s,e)=>await OpenStored(SelectedHistory);
            Get<Button>("RerunHistory").Click+=(s,e)=>PrepareSelected();
            Get<Button>("DeleteHistory").Click+=(s,e)=>DeleteRecord(SelectedHistory);
            Get<Button>("SaveName").Click+=async (s,e)=>await SaveRecordName();
            Get<Button>("CancelRename").Click+=(s,e)=>EndRename();
            var name=Get<TextBox>("SavedName");
            name.TextChanged+=(s,e)=>Get<Button>("SaveName").IsEnabled=!String.IsNullOrWhiteSpace(name.Text);
            name.PreviewKeyDown+=async (s,e)=>{
                if(e.Key==Key.Enter) {e.Handled=true;await SaveRecordName();}
                else if(e.Key==Key.Escape) {e.Handled=true;EndRename();}
            };
            Tree.SelectedItemChanged+=(s,e)=>ShowSelected();
            Tree.PreviewKeyDown+=async (s,e)=>{
                if(e.Key!=Key.Enter) return;
                if(SelectedHistory!=null) {e.Handled=true;await OpenStored(SelectedHistory);}
                else if(Tree.SelectedItem is TreeViewItem && Object.Equals(((TreeViewItem)Tree.SelectedItem).Tag,"newSql")) {
                    e.Handled=true;NewSession(null);
                }
            };
            Get<TextBox>("HistorySearch").TextChanged+=(s,e)=>FilterHistory();
            RefreshHistory();
        }
        void DeleteRecord(HistoryEntry entry) {
            if(entry==null) return;
            if(!ConfirmDelete(entry)) return;
            try {History.Delete(entry);RefreshHistory();}
            catch(Exception ex) {HistoryNotice(()=>L.F("删除失败：{0}",L.T(ex.Message)));}
        }
        void BeginRename(HistoryEntry entry) {
            editingName=entry;
            Get<TextBlock>("HistoryTitle").Visibility=Visibility.Collapsed;
            Get<StackPanel>("RenameForm").Visibility=Visibility.Visible;
            var input=Get<TextBox>("SavedName");
            input.Text=entry.Name;input.Focus();input.SelectAll();
        }
        void EndRename() {
            editingName=null;
            Get<StackPanel>("RenameForm").Visibility=Visibility.Collapsed;
            Get<TextBlock>("HistoryTitle").Visibility=Visibility.Visible;
        }
        async Task SaveRecordName() {
            var entry=editingName;
            var form=Get<StackPanel>("RenameForm");
            string value=Get<TextBox>("SavedName").Text.Trim();
            if(entry==null || !form.IsEnabled || value.Length==0) return;
            form.IsEnabled=false;
            try {
                await Task.Run(()=>History.Rename(entry,value));
                RefreshHistory();
            } catch(Exception ex) {HistoryNotice(()=>L.F("重命名失败：{0}",L.T(ex.Message)));}
            finally {form.IsEnabled=true;}
        }
        void AttachRecordMenu(TreeViewItem node,HistoryEntry entry) {
            var menu=new ContextMenu();
            var rename=new MenuItem{Header=L.T("重命名…")};
            var delete=new MenuItem{Header=L.T("删除履历…")};
            node.PreviewMouseRightButtonDown+=(s,e)=>{node.IsSelected=true;node.Focus();};
            rename.Click+=(s,e)=>{node.IsSelected=true;BeginRename(entry);};
            delete.Click+=(s,e)=>DeleteRecord(entry);
            menu.Items.Add(rename);menu.Items.Add(delete);node.ContextMenu=menu;
        }
        void HistoryNotice(Func<string> message) {
            L.Bind(Get<TextBlock>("HistoryNotice"),TextBlock.TextProperty,message);
        }
        internal void RefreshHistory() {
            try {
                int skipped;entries=History.List(out skipped);
                HistoryNotice(()=>skipped==0?"":L.F("{0} 条履历无法读取。",skipped));
            } catch(Exception ex) {HistoryNotice(()=>L.F("履历读取失败：{0}",L.T(ex.Message)));}
            FilterHistory();
        }
        const string FolderIcon="M1,4 L1,2 L6,2 L8,4 L15,4 L15,13 L1,13 Z M1,5 L15,5";
        const string FileIcon="M3,1 L10,1 L14,5 L14,15 L3,15 Z M10,1 L10,5 L14,5 M5,8 L12,8 M5,11 L12,11";
        static TreeViewItem Node(string name,string shape,Brush fill) {
            var header=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(1,2,0,2)};
            header.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(shape),Fill=fill,Stroke=Brushes.SlateGray,
                Width=16,Height=16,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0)});
            header.Children.Add(new TextBlock{Text=name});
            return new TreeViewItem{Header=header,ToolTip=name};
        }
        void FilterHistory() {
            var previous=SelectedHistory;
            string term=Get<TextBox>("HistorySearch").Text.Trim();
            Tree.Items.Clear();
            var create=Node(L.T("新建"),FolderIcon,Brushes.DarkSeaGreen);
            var sql=Node(L.T("SQL Server 结构比较"),FileIcon,Brushes.LightBlue);
            sql.Tag="newSql";sql.PreviewMouseDoubleClick+=(s,e)=>OpenAfterDoubleClick(e,()=>NewSession(null));
            create.Items.Add(sql);Tree.Items.Add(create);
            var saved=Node(L.T("自动保存"),FolderIcon,Brushes.Wheat);
            Tree.Items.Add(saved);
            saved.IsExpanded=term.Length>0 || autoExpanded;
            saved.Expanded+=(s,e)=>{if(e.OriginalSource==saved && term.Length==0) autoExpanded=true;};
            saved.Collapsed+=(s,e)=>{if(e.OriginalSource==saved && term.Length==0) autoExpanded=false;};
            var matches=entries.Where(x=>term.Length==0 || new[]{x.Name,x.LeftName,x.RightName}.Any(t=>t.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0));
            foreach(var group in matches.GroupBy(x=>x.CompletedUtc.ToLocalTime().Date)) {
                var date=group.Key;
                string label=date==DateTime.Today?L.T("今天"):date==DateTime.Today.AddDays(-1)?L.T("昨天"):date.ToString("yyyy-MM-dd");
                var day=Node(label+"  ("+group.Count()+")",FolderIcon,Brushes.Wheat);
                if(firstHistory && date==DateTime.Today) expandedDates.Add(date);
                day.IsExpanded=term.Length>0 || expandedDates.Contains(date);
                day.Expanded+=(s,e)=>{if(e.OriginalSource==day && term.Length==0) expandedDates.Add(date);};
                day.Collapsed+=(s,e)=>{if(e.OriginalSource==day && term.Length==0) expandedDates.Remove(date);};
                saved.Items.Add(day);
                foreach(var entry in group) {
                    var node=Node(entry.CompletedUtc.ToLocalTime().ToString("HH:mm:ss")+"   "+entry.Name,FileIcon,Brushes.WhiteSmoke);
                    node.Tag=entry;node.ToolTip=entry.Time+"   "+entry.LeftName+" ↔ "+entry.RightName;
                    AttachRecordMenu(node,entry);
                    node.PreviewMouseDoubleClick+=(s,e)=>OpenAfterDoubleClick(e,async ()=>await OpenStored(entry));
                    day.Items.Add(node);
                    if(previous!=null && entry.Path==previous.Path) node.IsSelected=true;
                }
            }
            if(saved.Items.Count==0) saved.Items.Add(new TreeViewItem{Header=L.T(term.Length==0?"暂无比较履历":"没有匹配的履历"),IsEnabled=false});
            if(entries.Count>0) firstHistory=false;
            ShowSelected();
        }
        void ShowSelected() {
            EndRename();
            var entry=SelectedHistory;
            Get<Button>("DeleteHistory").IsEnabled=entry!=null;
            Get<StackPanel>("Launcher").Visibility=entry==null?Visibility.Visible:Visibility.Collapsed;
            Get<StackPanel>("HistoryInfo").Visibility=entry==null?Visibility.Collapsed:Visibility.Visible;
            if(entry==null) return;
            Get<TextBlock>("HistoryTitle").Text=entry.Name;
            Get<TextBlock>("HistoryTime").Text=entry.Time;
            Get<TextBlock>("HistorySides").Text=L.T("左侧")+"   "+entry.LeftName+"\n"+L.T("右侧")+"   "+entry.RightName;
            Get<TextBlock>("HistorySummary").Text=L.F("{0} 个对象",entry.Total)+"   ·   "+entry.Summary;
        }
        void PrepareSelected() {
            var entry=SelectedHistory;
            if(entry==null) return;
            var session=NewSession(entry);
            session.EditConnections();
            session.SetStatus("已带入履历设置。点击「开始比较」读取两个数据库的最新结构。");
        }
        internal async Task OpenStored(HistoryEntry entry) {
            if(entry!=null) await NewSession(entry).ReadHistory();
        }
    }
}
