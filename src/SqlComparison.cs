using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DBCompare {
    internal sealed class SqlComparison {
        public readonly DockPanel Body = new DockPanel();
        readonly DetailControls controls = new DetailControls();
        readonly List<SqlDiff.Pair> pairs;
        readonly List<int> starts = new List<int>();
        readonly DataGrid left, right;
        readonly TextBlock leftPreview = new TextBlock(), rightPreview = new TextBlock();
        readonly TextBlock leftLabel = new TextBlock(), rightLabel = new TextBlock();
        readonly DifferenceMap map;
        List<int> visible = new List<int>();
        bool selecting;
        int current = -1;

        public SqlComparison(Result result, string leftName, string rightName) {
            string a = result.Left == null ? null : result.Left.Sql;
            string b = result.Right == null ? null : result.Right.Sql;
            controls.Comparable = result.Left == null || result.Right == null || (a != null && b != null);
            pairs = controls.Comparable ? SqlDiff.Detail(a,b) : SqlDiff.Detail(a ?? b,b ?? a);
            if (!controls.Comparable) foreach (var pair in pairs) {
                if (a == null) pair.A = null; else pair.B = null;
            }
            for (int i=0; i<pairs.Count; i++)
                if (controls.Comparable && pairs[i].Different && (i==0 || !pairs[i-1].Different)) starts.Add(i);

            var preview = new DockPanel { Background=Brushes.WhiteSmoke, LastChildFill=true };
            var labels = new StackPanel { Margin=new Thickness(5,2,6,2) };
            foreach (var label in new[]{leftLabel,rightLabel}) {
                label.Height=23; label.Foreground=Brushes.SlateGray; labels.Children.Add(label);
            }
            DockPanel.SetDock(labels,Dock.Left); preview.Children.Add(labels);
            var text = new StackPanel { Margin=new Thickness(2), Background=Brushes.White };
            foreach (var line in new[]{leftPreview,rightPreview}) {
                line.Height=23; line.FontFamily=new FontFamily("Consolas, Microsoft YaHei UI"); text.Children.Add(line);
            }
            preview.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility=ScrollBarVisibility.Disabled, Content=text });
            var inspector = new Expander { IsExpanded=true, Content=preview };
            L.Bind(inspector,HeaderedContentControl.HeaderProperty,()=>L.T("当前行对照"));
            DockPanel.SetDock(inspector,Dock.Bottom); Body.Children.Add(inspector);

            map=new DifferenceMap(pairs,controls.Comparable,GoToMapRow);
            DockPanel.SetDock(map,Dock.Left); Body.Children.Add(map);
            var sides=new Grid();
            sides.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=90});
            sides.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(5)});
            sides.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=90});
            var splitter=new GridSplitter {Width=5,ResizeDirection=GridResizeDirection.Columns,
                HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.LightGray};
            Grid.SetColumn(splitter,1); sides.Children.Add(splitter);
            left=Pane(sides,0,"左侧",leftName,result.Left);
            right=Pane(sides,2,"右侧",rightName,result.Right);
            Body.Children.Add(sides);
            left.SelectionChanged+=(s,e)=>Select(left.SelectedIndex);
            right.SelectionChanged+=(s,e)=>Select(right.SelectedIndex);
            left.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((s,e)=>SyncScroll(right,e)));
            right.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((s,e)=>SyncScroll(left,e)));
            controls.Apply=Filter; Body.Tag=controls; Filter("全部");
        }

        static DataGrid Pane(Grid sides,int col,string side,string database,Item item) {
            Func<string> title=()=>L.T(side)+"   "+database+(item==null?"    ("+L.T("无此对象")+")":item.Sql==null?"    ("+L.T("定义不可读")+")":"    /    "+item.FullName);
            var panel=new DockPanel();
            var heading=new Border {Background=Brushes.WhiteSmoke,Padding=new Thickness(8,5,8,5),
                Child=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis}};
            L.Bind(heading.Child,TextBlock.TextProperty,title);L.Bind(heading.Child,FrameworkElement.ToolTipProperty,title);
            DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
            var grid=new DataGrid { Name=col==0?"LeftLines":"RightLines", HeadersVisibility=DataGridHeadersVisibility.None,
                GridLinesVisibility=DataGridGridLinesVisibility.None, RowHeight=23, FrozenColumnCount=1,
                CanUserSortColumns=false, CanUserResizeRows=false, CanUserResizeColumns=false,
                IsReadOnly=true, CanUserAddRows=false, AutoGenerateColumns=false, SelectionMode=DataGridSelectionMode.Single,
                FontFamily=new FontFamily("Consolas, Microsoft YaHei UI"),FontSize=14,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,VerticalScrollBarVisibility=ScrollBarVisibility.Visible };
            grid.SetResourceReference(DataGrid.CellStyleProperty,"SqlCell");
            var rowStyle=new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.BackgroundProperty,new Binding("Shade")));
            grid.RowStyle=rowStyle;
            var gutter=new Style(typeof(TextBlock));
            gutter.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.SlateGray));
            gutter.Setters.Add(new Setter(TextBlock.BackgroundProperty,Brushes.WhiteSmoke));
            gutter.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Right));
            gutter.Setters.Add(new Setter(TextBlock.PaddingProperty,new Thickness(1,0,5,0)));
            grid.Columns.Add(new DataGridTextColumn{Width=63,Binding=new Binding("Gutter"),ElementStyle=gutter});
            var cell=new FrameworkElementFactory(typeof(CodeLineBlock));
            var code=new DataGridTemplateColumn{Width=DataGridLength.SizeToCells,CellTemplate=new DataTemplate{VisualTree=cell}};
            grid.Columns.Add(code);
            grid.SizeChanged+=(s,e)=>code.MinWidth=Math.Max(25,e.NewSize.Width-65-SystemParameters.VerticalScrollBarWidth);
            panel.Children.Add(grid);Grid.SetColumn(panel,col);sides.Children.Add(panel);
            return grid;
        }
        static ScrollViewer Scroll(DependencyObject root) {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
                var child=VisualTreeHelper.GetChild(root,i);
                var found=child as ScrollViewer ?? Scroll(child);
                if(found!=null) return found;
            }
            return null;
        }
        static void SyncScroll(DataGrid target,ScrollChangedEventArgs e) {
            if(e.HorizontalChange==0 && e.VerticalChange==0) return;
            var other=Scroll(target);
            if(other==null) return;
            if(e.VerticalChange!=0 && Math.Abs(e.VerticalOffset-other.VerticalOffset)>.01)
                other.ScrollToVerticalOffset(e.VerticalOffset);
            if(e.HorizontalChange!=0 && e.HorizontalOffset<=other.ScrollableWidth && Math.Abs(e.HorizontalOffset-other.HorizontalOffset)>.01)
                other.ScrollToHorizontalOffset(e.HorizontalOffset);
        }
        string Status(SqlDiff.Pair pair) {
            if(!controls.Comparable) return Result.Unknown;
            if(pair.A==null) return Result.OnlyR;
            if(pair.B==null) return Result.OnlyL;
            return pair.Different?Result.Changed:Result.Same;
        }
        void Filter(string mode) {
            controls.Mode=mode;
            visible=Enumerable.Range(0,pairs.Count).Where(i=>App.MatchMode(Status(pairs[i]),mode)).ToList();
            var a=new List<CodeRow>(); var b=new List<CodeRow>();
            foreach(int i in visible) {
                var pair=pairs[i];
                bool start=starts.BinarySearch(i)>=0;
                byte level=controls.Comparable?CodeRow.Level(pair):(byte)0;
                a.Add(new CodeRow(pair.A,level,start)); b.Add(new CodeRow(pair.B,level,start));
            }
            selecting=true;
            try {left.ItemsSource=a;right.ItemsSource=b;} finally {selecting=false;}
            controls.Summary=()=>controls.Comparable?L.F("{0} 处差异   ·   {1} / {2} 行",starts.Count,visible.Count,pairs.Count):L.T("定义不完整，无法比较");
            controls.Jump=controls.Comparable && visible.Any(i=>pairs[i].Different)?(Action<int>)Jump:null;
            int keep=visible.IndexOf(current);
            Select(keep>=0?keep:visible.Count==0?-1:0);
        }
        void Select(int index) {
            if(selecting) return;
            selecting=true;
            try {
                left.SelectedIndex=right.SelectedIndex=index;
                current=index<0?-1:visible[index];
                map.SetCurrent(current);
                var pair=current<0?null:pairs[current];
                Preview(leftLabel,leftPreview,"左",pair==null?null:pair.A);
                Preview(rightLabel,rightPreview,"右",pair==null?null:pair.B);
            } finally {selecting=false;}
        }
        static void Preview(TextBlock label,TextBlock text,string side,SqlDiff.Line line) {
            L.Bind(label,TextBlock.TextProperty,()=>L.T(side)+"   "+(line==null?"—":line.Number.ToString()));
            CodeLineBlock.Write(text,line);
        }
        void GoTo(int index) {
            if(index<0 || index>=visible.Count) return;
            Select(index);
            left.ScrollIntoView(left.Items[index]);right.ScrollIntoView(right.Items[index]);
        }
        void GoToMapRow(int row) {
            if(visible.Count==0) return;
            int index=visible.BinarySearch(row);
            GoTo(index>=0?index:Math.Min(~index,visible.Count-1));
        }
        void Jump(int step) {
            var stops=new List<int>(); int previousBlock=-1;
            for(int i=0;i<visible.Count;i++) {
                int row=visible[i];
                if(!pairs[row].Different) continue;
                int block=starts.BinarySearch(row);
                if(block<0) block=~block-1;
                if(block!=previousBlock) stops.Add(i);
                previousBlock=block;
            }
            if(stops.Count==0) return;
            int selected=left.SelectedIndex;
            int next=step>0?stops.FindIndex(i=>i>selected):stops.FindLastIndex(i=>i<selected);
            GoTo(stops[next>=0?next:step>0?0:stops.Count-1]);
        }
    }

    internal sealed class CodeRow {
        public readonly SqlDiff.Line Line;
        public string Gutter {get;private set;}
        public Brush Shade {get;private set;}
        static readonly Brush FormatRow=new SolidColorBrush(Color.FromRgb(232,246,255));
        static readonly Brush ChangedRow=new SolidColorBrush(Color.FromRgb(255,249,216));
        public CodeRow(SqlDiff.Line line,byte level,bool start) {
            Line=line;
            Gutter=(start?"▸ ":"  ")+(line==null?"·":line.Number.ToString());
            Shade=level==2?ChangedRow:level==1?FormatRow:Brushes.White;
        }
        public static byte Level(SqlDiff.Pair pair) {
            byte level=Math.Max(pair.A==null?(byte)0:pair.A.Level,pair.B==null?(byte)0:pair.B.Level);
            return level==0 && pair.Different?(byte)1:level;
        }
    }
    public sealed class CodeLineBlock : TextBlock {
        public CodeLineBlock() {
            Foreground=Brushes.Black;Padding=new Thickness(4,0,2,0);
            DataContextChanged+=(s,e)=>{var row=e.NewValue as CodeRow;Write(this,row==null?null:row.Line);};
        }
        internal static void Write(TextBlock block,SqlDiff.Line line) {
            block.Inlines.Clear();
            if(line==null) return;
            foreach(var part in line.Parts) {
                var run=new Run(part.Text.Replace("\t","    "));
                if(part.Tint!=0) run.Background=part.Tint==1?DetailView.Sky:DetailView.Yellow;
                block.Inlines.Add(run);
            }
        }
    }
    internal sealed class DifferenceMap : FrameworkElement {
        readonly int total;
        readonly List<Tuple<int,int,byte>> bands=new List<Tuple<int,int,byte>>();
        int current=-1;
        public DifferenceMap(List<SqlDiff.Pair> pairs,bool comparable,Action<int> go) {
            total=pairs.Count; Width=21; Cursor=Cursors.Hand;
            L.Bind(this,FrameworkElement.ToolTipProperty,()=>L.T("点击定位到对应行"));
            if(comparable) for(int i=0;i<total;) {
                byte level=CodeRow.Level(pairs[i]);int start=i++;
                while(i<total && CodeRow.Level(pairs[i])==level) i++;
                if(level>0) bands.Add(Tuple.Create(start,i,level));
            }
            MouseLeftButtonDown+=(s,e)=>go((int)(e.GetPosition(this).Y*Math.Max(1,total)/Math.Max(1,ActualHeight)));
        }
        public void SetCurrent(int row) {current=row;InvalidateVisual();}
        protected override void OnRender(DrawingContext dc) {
            dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(RenderSize));
            double scale=ActualHeight/Math.Max(1,total);
            foreach(var band in bands) dc.DrawRectangle(band.Item3==1?DetailView.Sky:DetailView.Yellow,null,
                new Rect(3,band.Item1*scale,ActualWidth-6,Math.Max(2,(band.Item2-band.Item1)*scale)));
            if(current>=0) dc.DrawRectangle(Brushes.SlateGray,null,new Rect(1,current*scale,ActualWidth-2,2));
        }
    }
}
