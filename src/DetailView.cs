using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace DBCompare {
    internal sealed class DetailControls {
        public string Mode="全部";
        public Func<string> Summary=()=>"";
        public bool Comparable=true;
        public Action<string> Apply;
        public Action<int> Jump;
    }
    internal static class DetailView {
        internal static readonly Brush Sky=Brushes.LightSkyBlue, Yellow=Brushes.Khaki;
        static TextBlock Label(Func<string> text,double size) {
            var label=new TextBlock{FontSize=size,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
            L.Bind(label,TextBlock.TextProperty,text);return label;
        }
        static void Top(DockPanel panel,UIElement element) {DockPanel.SetDock(element,Dock.Top);panel.Children.Add(element);}
        static Border Tag(string text,Brush color) {return new Border{Background=color,Padding=new Thickness(8,3,8,3),Margin=new Thickness(5,0,0,0),CornerRadius=new CornerRadius(3),Child=Label(()=>L.T(text),12)};}

        public static UIElement Create(Result r,string leftName,string rightName,Action viewChanged) {
            var body=new DockPanel{Margin=new Thickness(3)};
            var header=new DockPanel{Margin=new Thickness(7,3,7,4)};
            var legend=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            legend.Children.Add(Tag("格式差异",Sky));legend.Children.Add(Tag("内容差异",Yellow));
            DockPanel.SetDock(legend,Dock.Right);header.Children.Add(legend);
            var name=Label(()=>L.T(r.Kind)+"   /   "+r.Name+"     ·     "+r.DisplayStatus,14);name.FontWeight=FontWeights.SemiBold;header.Children.Add(name);Top(body,header);
            if((r.Left!=null && r.Left.Unknown)||(r.Right!=null && r.Right.Unknown)) {
                var note=Label(()=>L.T("部分定义不可读（可能已加密或权限受限）。下方仅显示已读取的内容。"),13);
                note.Background=Brushes.LemonChiffon;Top(body,note);
            }
            var properties=Properties(r,leftName,rightName);
            if(r.Kind=="表" || ((r.Left==null || r.Left.Sql==null) && (r.Right==null || r.Right.Sql==null))) {
                body.Children.Add(properties);
                body.Tag=properties.Tag;
            } else {
                var tabs=new TabControl{Padding=new Thickness(0)};
                var sql=new TabItem{Padding=new Thickness(12,5,12,5)};
                L.Bind(sql,HeaderedContentControl.HeaderProperty,()=>L.T("SQL 定义"));
                sql.Content=new SqlComparison(r,leftName,rightName).Body;tabs.Items.Add(sql);
                var props=new TabItem{Content=properties,Padding=new Thickness(12,5,12,5)};
                L.Bind(props,HeaderedContentControl.HeaderProperty,()=>L.T("结构属性"));tabs.Items.Add(props);
                tabs.SelectionChanged+=(s,e)=>{
                    var item=tabs.SelectedItem as TabItem;
                    if(e.OriginalSource==tabs && item!=null) {
                        body.Tag=((FrameworkElement)item.Content).Tag;
                        viewChanged();
                    }
                };
                tabs.SelectedIndex=r.Left!=null && r.Right!=null && r.Left.Sql==r.Right.Sql && r.Status!=Result.Same?1:0;
                body.Children.Add(tabs);
            }
            return body;
        }
        sealed class PropertyRow {
            public string Key {get;set;} public string A {get;set;} public string B {get;set;}
            public string Status {get;set;}
            public bool Changed {get{return Status!=Result.Same;}}
        }
        static DataGridTextColumn Column(Func<string> name,string key,double weight) {
            var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));
            var column=new DataGridTextColumn{Binding=L.PropertyBinding(key),SortMemberPath=key,ElementStyle=style,Width=new DataGridLength(weight,DataGridLengthUnitType.Star)};
            L.Bind(column,DataGridColumn.HeaderProperty,name);return column;
        }
        static FrameworkElement Properties(Result r,string leftName,string rightName) {
            var body=new DockPanel();
            var a=r.Left==null?new SortedDictionary<string,string>():r.Left.Props;
            var b=r.Right==null?new SortedDictionary<string,string>():r.Right.Props;
            var rows=a.Keys.Union(b.Keys,StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal).Select(k=>{
                string av,bv;bool hasA=a.TryGetValue(k,out av),hasB=b.TryGetValue(k,out bv);
                return new PropertyRow{Key=k,A=hasA?av:"—",B=hasB?bv:"—",Status=hasA && hasB?Result.PropertyStatus(k,av,bv,r.Engine):hasA?Result.OnlyL:Result.OnlyR};
            }).ToList();
            var grid=new DataGrid();grid.Columns.Add(Column(()=>L.T("结构属性"),"Key",1.2));grid.Columns.Add(Column(()=>L.T("左侧")+" · "+leftName,"A",1));grid.Columns.Add(Column(()=>L.T("右侧")+" · "+rightName,"B",1));
            var style=new Style(typeof(DataGridRow));
            var trigger=new DataTrigger{Binding=new Binding("Changed"),Value=true};trigger.Setters.Add(new Setter(Control.BackgroundProperty,Yellow));style.Triggers.Add(trigger);
            var fmt=new DataTrigger{Binding=new Binding("Status"),Value=Result.Format};fmt.Setters.Add(new Setter(Control.BackgroundProperty,Sky));style.Triggers.Add(fmt);
            grid.RowStyle=style;
            var controls=new DetailControls();
            Action<int> jump=step=>{
                int n=grid.Items.Count,start=grid.SelectedIndex<0?(step>0?-1:0):grid.SelectedIndex;
                for(int i=1;i<=n;i++) {
                    int idx=(start+step*i+n)%n;
                    if(((PropertyRow)grid.Items[idx]).Changed) {grid.SelectedIndex=idx;grid.ScrollIntoView(grid.SelectedItem);return;}
                }
            };
            controls.Apply=value=>{
                var visible=rows.Where(p=>App.MatchMode(p.Status,value)).ToList();
                controls.Mode=value;grid.ItemsSource=visible;
                controls.Summary=()=>L.F("匹配 {0} / {1} 项属性",visible.Count,rows.Count);
                controls.Jump=visible.Any(p=>p.Changed)?jump:null;
            };
            controls.Apply(rows.Any(p=>p.Changed)?"不同":"全部");body.Tag=controls;
            body.Children.Add(grid);return body;
        }
    }
}
