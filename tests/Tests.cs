using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DBCompare {
    internal static class Tests {
        static int checks;
        static void Expect(bool value,string label) {
            if(!value) throw new Exception(label);
            checks++;
        }
        static void TextTests() {
            string[,] cases={
                {"SELECT 1","SELECT 1",Result.Same},
                {"SELECT COUNT(*) FROM dbo.T","  SELECT  COUNT ( * )\r\nFROM dbo.T\n",Result.Format},
                {"SELECT 1\n","SELECT 1\r\n",Result.Format},
                {"SELECT N'a  b'","SELECT N'a b'",Result.Changed},
                {"SELECT N'x'' y'","SELECT N'x''  y'",Result.Changed},
                {"SELECT 'a\nb'","SELECT 'a b'",Result.Changed},
                {"SELECT [a]] b]","SELECT [a]]  b]",Result.Changed},
                {"SELECT \"a b\"","SELECT \"a  b\"",Result.Changed},
                {"SELECT 12","SELECT 1 2",Result.Changed},
                {"SELECT N'x'","SELECT N 'x'",Result.Changed},
                {"SELECT a FROM t","SELECT aFROM t",Result.Changed},
                {"WHERE a<=0","WHERE a< =0",Result.Changed},
                {"SELECT 1 -- x\n,2","SELECT 1 -- x ,2",Result.Changed},
                {"/*x /* y */ z*/SELECT 1","/*x /* y */ z*/\nSELECT 1",Result.Format},
                {"SELECT 1","SELECT 2",Result.Changed}
            };
            for(int i=0;i<cases.GetLength(0);i++) Expect(SqlDiff.Classify(cases[i,0],cases[i,1])==cases[i,2],"lex "+i);
            var lines=SqlDiff.Detail("SELECT\n  a\nFROM t","SELECT\n    b\nFROM t");
            Expect(lines.Count==3 && !lines[0].Different && !lines[2].Different,"unchanged context");
            Expect(lines[1].B.Parts.Any(p=>p.Tint==1) && lines[1].B.Parts.Any(p=>p.Tint==2),"mixed highlights");
            var format=SqlDiff.Detail("SELECT a FROM t","SELECT\n    a\nFROM t");
            Expect(format.Count==3 && format.Any(x=>x.Different),"aligned line break");
            Expect(format.Where(p=>p.B!=null).All(p=>p.B.Level<2),"no yellow in formatting-only difference");
            var inserted=SqlDiff.Detail("SELECT 1\nRETURN","SELECT 1\nPRINT 2\nRETURN");
            Expect(inserted.Count==3 && inserted[1].A==null && inserted[1].B.Number==2,"insertion alignment");
            Expect(SqlDiff.Detail(null,"SELECT 5")[0].B.Level==2,"single side yellow");
            var digit=SqlDiff.Detail("SELECT 950","SELECT 940").Single();
            Expect(digit.A.Parts.Single(p=>p.Tint==2).Text=="5" && digit.B.Parts.Single(p=>p.Tint==2).Text=="4","intraline digit highlights");
            Expect(Result.PropertyStatus("字段 [x] / 默认值","((1))","( (1) )")==Result.Format,"expression formatting");
        }
        static Item Obj(string schema,string name,string kind,string sql) {
            return new Item{Schema=schema,Name=name,Kind=kind,Sql=sql};
        }
        static DataTable Set(string fields) {
            var table=new DataTable();
            foreach(var field in fields.Split(' ')) table.Columns.Add(field,field=="object_id"?typeof(int):typeof(object));
            return table;
        }
        static void Add(DataTable table,int id,params object[] values) {
            var row=table.NewRow();row["object_id"]=id;
            for(int i=0;i<values.Length;i+=2) row[(string)values[i]]=values[i+1];
            table.Rows.Add(row);
        }
        static Snapshot Catalog(int id,bool changed) {
            var objects=Set("object_id schema_name name kind definition uses_ansi_nulls uses_quoted_identifier is_schema_bound principal_name execute_as_principal_id is_memory_optimized durability_desc is_disabled is_instead_of_trigger is_not_for_replication parent_schema parent_name");
            Add(objects,id,"schema_name","sales","name","Orders","kind","U","is_memory_optimized",false);
            Add(objects,id+1,"schema_name","sales","name","NewOrder","kind","P","definition","CREATE PROC sales.NewOrder AS SELECT 1");
            Add(objects,id+2,"schema_name","hr","name","Unavailable","kind","V");
            var ddl=Set("object_id name definition uses_ansi_nulls uses_quoted_identifier is_disabled principal_name");
            Add(ddl,id+3,"name","Watch","definition","CREATE TRIGGER Watch ON DATABASE FOR CREATE_TABLE AS RETURN","is_disabled",false);
            var col=Set("object_id name position type_name max_length precision scale is_nullable collation_name is_rowguidcol is_sparse is_column_set is_filestream is_ansi_padded is_identity is_computed seed_value increment_value is_not_for_replication expression is_persisted default_name default_value is_xml_document xml_collection");
            Add(col,id,"name","OrderId","position",1,"type_name","[sys].[int]","max_length",4,"is_identity",true,"seed_value",1,"increment_value",1);
            Add(col,id,"name","Amount","position",2,"type_name","[sys].[decimal]","precision",changed?16:12,"scale",2,"is_nullable",false,"default_name","DF_Amount","default_value","((0))");
            var indexes=Set("object_id name type_desc is_unique is_primary_key is_unique_constraint is_disabled has_filter filter_definition ignore_dup_key fill_factor is_padded allow_row_locks allow_page_locks bucket_count");
            Add(indexes,id,"name","PK_Orders","type_desc","CLUSTERED","is_primary_key",true,"is_unique",true);
            var indexCols=Set("object_id index_name column_name index_column_id key_ordinal is_descending_key is_included_column partition_ordinal");
            Add(indexCols,id,"index_name","PK_Orders","column_name","OrderId","key_ordinal",1,"is_descending_key",changed);
            var fk=Set("object_id name ref_schema ref_table delete_referential_action_desc update_referential_action_desc is_disabled is_not_trusted is_not_for_replication constraint_column_id local_column ref_column");
            Add(fk,id,"name","FK_Orders","ref_schema","dbo","ref_table","Customer","delete_referential_action_desc",changed?"CASCADE":"NO_ACTION","constraint_column_id",1,"local_column","Buyer","ref_column","Id");
            var check=Set("object_id name definition is_disabled is_not_trusted is_not_for_replication bound_column");
            Add(check,id,"name","CK_Amount","definition",changed?"[Amount]>=0":"[Amount]>0");
            var ev=Set("object_id type_desc is_first is_last");
            Add(ev,id+3,"type_desc","CREATE_TABLE","is_first",false,"is_last",false);
            var snapshot=new Snapshot();
            using(var reader=new DataTableReader(new[]{objects,ddl,col,indexes,indexCols,fk,check,ev}))
                Database.ReadCatalog(reader,snapshot,CancellationToken.None);
            return snapshot;
        }
        static void CatalogTests() {
            var a=Catalog(1,false);var b=Catalog(100,false);
            Expect(Result.Compare(a,b).Count(x=>x.Status==Result.Same)==3,"database-local IDs");
            var rows=Result.Compare(a,Catalog(230,true));
            Expect(rows.Single(x=>x.Kind=="表").Detail=="4 项属性","column, index, FK and check changes");
            Expect(rows.Single(x=>x.Kind=="视图").Status==Result.Unknown,"unreadable metadata");
            var table=rows.Single(x=>x.Kind=="表").Left.Props;
            Expect(table["索引 [PK_Orders] / 主键"]=="是" && table["字段 [OrderId] / 标识种子"]=="1","key and identity attributes");
            Expect(table["字段 [Amount] / 默认值"]=="((0))","default expression");
            Expect(rows.Single(x=>x.Kind=="数据库触发器").Right.Props.ContainsKey("触发事件 CREATE_TABLE / 首先执行"),"DDL trigger events");
            bool failed=false;
            try { using(var reader=new DataTableReader(Set("object_id"))) Database.ReadCatalog(reader,new Snapshot(),CancellationToken.None); }
            catch(InvalidOperationException) {failed=true;}
            Expect(failed,"partial catalog fails");
            failed=false;
            try { Database.Load("Server=localhost",CancellationToken.None).GetAwaiter().GetResult(); }
            catch(ArgumentException) {failed=true;}
            Expect(failed,"missing database rejected before connection");
        }
        static void Demo(out Snapshot a,out Snapshot b) {
            a=new Snapshot{Name="dev-sql / Sales"};b=new Snapshot{Name="test-sql / Sales"};
            foreach(var name in new[]{"Customer","OrderHeader","OrderLine","Product","Store","Region","Invoice","Shipment"}) {
                var x=Obj("dbo",name,"表",null);var y=Obj("dbo",name,"表",null);
                x.Props["字段 [Name] / 最大长度 (字节)"]="100";y.Props["字段 [Name] / 最大长度 (字节)"]=name=="Customer"?"200":"100";
                if(name!="Shipment") a.Add(x);if(name!="Invoice") b.Add(y);
            }
            a.Add(Obj("sales","GetOrder","存储过程","CREATE PROCEDURE sales.GetOrder @id int\nAS\nBEGIN\n  SELECT OrderId, Total\n  FROM dbo.Orders\n  WHERE OrderId = @id;\nEND"));
            b.Add(Obj("sales","GetOrder","存储过程","CREATE PROCEDURE sales.GetOrder @id int\nAS\nBEGIN\n    SELECT OrderId, Subtotal\n    FROM dbo.Orders\n    WHERE OrderId = @id;\nEND"));
            a.Add(Obj("hr","GetOrder","存储过程","SELECT 1"));b.Add(Obj("hr","GetOrder","存储过程","SELECT\n1"));
            var unreadable=Obj("dbo","secret","视图",null);unreadable.Unknown=true;a.Add(unreadable);b.Add(unreadable);
            a.Add(Obj("sales.a","b]","表",null));b.Add(Obj("sales","a.b]","表",null));
        }
        static void UI() {
            var app=new Application();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Snapshot a,b;Demo(out a,out b);var results=Result.Compare(a,b);
            Expect(results.Count==13,"schema and escaped identifiers");
            Expect(results.Count(r=>r.Status==Result.Format)==1 && results.Count(r=>r.Status==Result.Changed)==2,"compared states");
            Expect(results.Count(r=>r.Status==Result.Unknown)==1 && results.Count(r=>r.Status==Result.Same)==5,"unreadable distinct from same");
            Expect(results.Count(r=>App.MatchMode(r.Status,"不同"))==7,"different includes both one-sided states");
            string folder=Path.Combine(Environment.CurrentDirectory,".history-test-"+Guid.NewGuid().ToString("N"));
            var history=new HistoryStore(folder);
            string languagePath=Path.Combine(folder,"language.txt");
            var settings=new LanguageSettings(languagePath);
            Expect(settings.Load(new System.Globalization.CultureInfo("zh-TW"))=="zh-CN","first run uses Chinese Windows UI language");
            int calls=0;
            bool failLoad=false,waitForCancel=false;
            TaskCompletionSource<bool> releaseRead=null;
            var readConnections=new List<string>();
            var ui=new App(history,(connection,token)=>{
                lock(readConnections) readConnections.Add(connection);
                Interlocked.Increment(ref calls);
                if(failLoad) throw new InvalidOperationException("simulated connection failure");
                if(waitForCancel) {
                    var pending=new TaskCompletionSource<Snapshot>();
                    token.Register(()=>pending.TrySetCanceled());
                    return pending.Task;
                }
                var snapshot=connection.Contains("dev-sql")?a:b;
                return releaseRead==null?Task.FromResult(snapshot):releaseRead.Task.ContinueWith(t=>snapshot,TaskScheduler.Default);
            },settings);
            var tabs=(TabControl)ui.Window.FindName("Pages");
            var home=(TabItem)ui.Window.FindName("Home");
            var tree=(TreeView)ui.Window.FindName("HistoryTree");
            var status=(TextBlock)ui.Window.FindName("StatusBar");
            var progress=(ProgressBar)ui.Window.FindName("RunProgress");
            Expect(tabs.SelectedItem==home && tabs.Items.Count==1,"only home opens at startup");
            var newButton=(Button)ui.Window.FindName("NewComparison");
            newButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var firstSession=ui.Current;
            Expect(firstSession!=null && tabs.Items.Count==2 && firstSession.Total==0,"new independent tab");
            var ca=firstSession.Get<TextBox>("LeftConn");var cb=firstSession.Get<TextBox>("RightConn");
            ca.Text="Server=dev-sql;Database=Sales;User ID=test;Pwd=\"Secret;ForTest\";";
            cb.Text="Server=test-sql;Database=Sales;Integrated Security=True";
            firstSession.Get<TextBox>("ComparisonName").Text="销售库发布核对";
            var run=firstSession.Get<Button>("Compare");
            run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>run.IsEnabled);
            int skipped;var restarted=new HistoryStore(folder);
            var first=restarted.List(out skipped).Single();
            Expect(calls==2 && first.Name==firstSession.Name && first.Name=="销售库发布核对","named run saved");
            Expect(first.LeftConnection==ca.Text && System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(first.Path)).Contains("Secret;ForTest"),"exact plaintext connection persisted");
            var secret=history.Save(a,b,results,ca.Text,cb.Text,"销售库结构快照");
            var reloaded=restarted.Open(secret);
            Expect(Result.Compare(a,reloaded[0]).All(x=>x.Status==Result.Same || x.Status==Result.Unknown) &&
                Result.Compare(b,reloaded[1]).All(x=>x.Status==Result.Same || x.Status==Result.Unknown),"snapshot round trip");
            tabs.SelectedItem=home;
            var find=(TextBox)ui.Window.FindName("HistorySearch");find.Text="no such db";
            var savedBranch=(TreeViewItem)tree.Items[1];
            Expect(!((TreeViewItem)savedBranch.Items[0]).IsEnabled,"empty search tree");
            find.Text="销售库发布";savedBranch=(TreeViewItem)tree.Items[1];
            var dateBranch=(TreeViewItem)savedBranch.Items[0];var leaf=(TreeViewItem)dateBranch.Items[0];
            Expect(dateBranch.Items.Count==1 && ((HistoryEntry)leaf.Tag).Path==first.Path,"name search and date grouping");
            leaf.IsSelected=true;
            ((Button)ui.Window.FindName("RerunHistory")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rerun=ui.Current;
            var startAgain=rerun.Get<Button>("Compare");
            Expect(rerun!=firstSession && !rerun.Busy && startAgain.IsEnabled && calls==2 && history.List(out skipped).Count==2,
                "history action only prepares a new comparison without connecting or recording");
            Expect(rerun.Get<TextBox>("LeftConn").Text==ca.Text && rerun.Get<TextBox>("RightConn").Text==cb.Text &&
                rerun.Get<Expander>("Connections").IsExpanded && status.Text.Contains("开始比较"),"restored settings ready to run unchanged");
            releaseRead=new TaskCompletionSource<bool>();
            startAgain.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(()=>calls==4);
            Expect(rerun.Busy && !startAgain.IsEnabled && progress.IsIndeterminate &&
                rerun.Get<Button>("Cancel").IsEnabled && status.Text.Contains("正在读取"),"manual start shows progress in footer");
            startAgain.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            releaseRead.SetResult(true);PumpUntil(()=>!rerun.Busy);releaseRead=null;
            Expect(calls==4 && readConnections.Skip(2).OrderBy(x=>x).SequenceEqual(new[]{ca.Text,cb.Text}.OrderBy(x=>x)) &&
                history.List(out skipped).Count==3,"one manual run uses both unchanged connections exactly once");
            Expect(startAgain.IsEnabled && status.Text.Contains("比较完成") &&
                progress.Visibility==Visibility.Collapsed && rerun.View.FindName("RunMessage")==null,"completion status only in footer");
            find.Clear();tabs.SelectedItem=firstSession.Tab;
            failLoad=true;run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>run.IsEnabled);
            Expect(firstSession.Total==13 && history.List(out skipped).Count==3 && status.Text.Contains("比较失败"),"failed refresh shows error in footer and preserves snapshots");
            failLoad=false;waitForCancel=true;
            int beforeConcurrent=calls;
            run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(()=>calls==beforeConcurrent+2);
            tabs.SelectedItem=rerun.Tab;
            Expect(firstSession.Busy && !rerun.Busy && rerun.Get<Button>("Compare").IsEnabled,"background comparison does not lock another tab");
            waitForCancel=false;
            var independent=rerun.Compare();PumpUntil(()=>independent.IsCompleted);independent.GetAwaiter().GetResult();
            Expect(firstSession.Busy && history.List(out skipped).Count==4,"second comparison finishes while first one is running");
            firstSession.Cancel();PumpUntil(()=>run.IsEnabled);
            Expect(tabs.SelectedItem==rerun.Tab && history.List(out skipped).Count==4,"cancel does not switch tabs or save history");
            waitForCancel=false;
            int callsBeforeOpen=calls;
            var open=ui.OpenStored(secret);PumpUntil(()=>open.IsCompleted);open.GetAwaiter().GetResult();
            var session=ui.Current;var overview=session.Tab;var grid=session.Objects;
            Expect(calls==callsBeforeOpen && session.Name==secret.Name && grid.Items.Count==13 && tabs.Items.Count==4,"offline history gets a named independent tab");
            Expect(session.Get<TextBox>("LeftConn").Text==ca.Text,"raw password available for editing");
            var shared=results.First(r=>r.Kind=="表");
            session.Open(shared);var firstDetail=tabs.SelectedItem;
            rerun.Open(shared);Expect(tabs.SelectedItem!=firstDetail,"same object in separate sessions has distinct details");
            ui.Close(rerun.Tab);Expect(tabs.Items.Contains(firstDetail) && tabs.Items.Contains(firstSession.Tab),"closing comparison only closes its own details");
            ui.Close((TabItem)firstDetail);
            tabs.SelectedItem=firstSession.Tab;
            ((TextBox)ui.Window.FindName("Search")).Text="Order";
            tabs.SelectedItem=overview;
            Expect(((TextBox)ui.Window.FindName("Search")).Text=="" && firstSession.Search=="Order","search state isolated by session");
            ui.Close(firstSession.Tab);
            var groups=grid.Items.Groups.Cast<CollectionViewGroup>().ToList();
            Expect(grid.Items.Count==13 && groups.Count==3 && groups[0].ItemCount==10,"paired grid groups all object types");
            var tableGroup=(ObjectCategory)groups[0].Name;
            var collapse=(Button)ui.Window.FindName("CollapseAll");var expand=(Button)ui.Window.FindName("ExpandAll");
            collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Expect(groups.All(g=>!((ObjectCategory)g.Name).Expanded),"collapse all");
            tableGroup.Expanded=true;
            Expect(!((ObjectCategory)groups[1].Name).Expanded,"independent category");
            var menu=(MenuItem)ui.Window.FindName("ViewMenu");
            var modes=(StackPanel)ui.Window.FindName("Modes");
            int[] counts={13,7,5,2,2};
            for(int i=0;i<counts.Length;i++) {
                ((MenuItem)menu.Items[i]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Expect(grid.Items.Count==counts[i] && ((RadioButton)modes.Children[i]).IsChecked==true,"menu and top toolbar filter "+i);
            }
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(!((ObjectCategory)((CollectionViewGroup)grid.Items.Groups[1]).Name).Expanded,"remember fold through filtering");
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var search=(TextBox)ui.Window.FindName("Search");search.Text="Customer";
            Expect(grid.Items.Count==1,"search");grid.SelectedIndex=0;
            ((MenuItem)ui.Window.FindName("OpenMenu")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(tabs.Items.Count==3 && tabs.SelectedIndex==2,"open table detail");
            Expect(((RadioButton)modes.Children[1]).IsChecked==true,"table differs filter on top");
            ((RadioButton)modes.Children[2]).IsChecked=true;
            Expect(tabs.SelectedIndex==2 && ((MenuItem)menu.Items[2]).IsChecked,"filter properties without leaving detail");
            session.Open((Result)grid.Items[0]);Expect(tabs.Items.Count==3,"reuse tab");
            ((MenuItem)ui.Window.FindName("GoOverview")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(search.Text=="Customer" && grid.SelectedIndex==0 && ((RadioButton)modes.Children[0]).IsChecked==true,"preserve overview");
            search.Clear();
            var proc=results.First(r=>r.Kind=="存储过程" && r.Status==Result.Changed);
            session.Open(proc);var detail=(DockPanel)((TabItem)tabs.SelectedItem).Content;
            var controls=(DetailControls)detail.Tag;
            Expect(controls.Jump!=null && controls.Mode=="全部","SQL navigation attached");
            controls.Jump(1);
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(controls.Mode=="不同" && tabs.SelectedItem!=overview,"filter SQL rows from View menu");
            ((RadioButton)modes.Children[0]).IsChecked=true;
            ((MenuItem)ui.Window.FindName("CloseMenu")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(tabs.Items.Count==3,"close detail");
            session.ShowResults(a,b,results);Expect(tabs.Items.Count==2 && tabs.Items.Contains(home),"discard obsolete detail tabs");
            // Exercise layout and data templates without opening a desktop window.
            var host=(FrameworkElement)ui.Window.Content;ui.Window.Content=null;host.Resources.MergedDictionaries.Add(ui.Window.Resources);
            host.SetValue(TextBlock.FontSizeProperty,ui.Window.FontSize);
            host.SetValue(TextBlock.FontFamilyProperty,ui.Window.FontFamily);
            Layout(host,"overview");
            var groupExpanders=Visuals<Expander>(grid).ToList();
            Expect(groupExpanders.Count>=3,"group headers rendered");
            groupExpanders[0].IsExpanded=false;host.UpdateLayout();
            Expect(!tableGroup.Expanded,"category header updates saved state");
            var groupBody=(FrameworkElement)groupExpanders[0].Template.FindName("Body",groupExpanders[0]);
            Expect(groupBody.Visibility==Visibility.Collapsed,"collapsed objects hidden");
            Layout(host,"groups");
            var row=Visuals<DataGridRow>(grid).First(r=>((Result)r.Item).Key==proc.Key);
            DoubleClick(Visuals<DataGridCell>(row).First(),MouseButton.Right);
            Expect(tabs.SelectedItem==overview && tabs.Items.Count==2,"right double-click keeps overview");
            DoubleClick(Visuals<DataGridCell>(row).First(),MouseButton.Left);
            var opened=(TabItem)tabs.SelectedItem;
            Expect(opened!=overview && opened.Uid==proc.Key && tabs.SelectedContent==opened.Content,"double-click displays the clicked object's page");
            tabs.SelectedItem=overview;host.UpdateLayout();
            row=Visuals<DataGridRow>(grid).First(r=>((Result)r.Item).Key==proc.Key);
            DoubleClick(Visuals<DataGridCell>(row).First(),MouseButton.Left);
            Expect(tabs.Items.Count==3 && tabs.SelectedItem==opened && tabs.SelectedContent==opened.Content,"double-click brings existing detail back to front");
            Layout(host,"sql");
            var panes=Visuals<DataGrid>(host).Where(g=>g.Name=="LeftLines" || g.Name=="RightLines").ToList();
            Expect(panes.Count==2 && panes.All(g=>g.FrozenColumnCount==1),"two code panes with frozen gutter");
            panes[1].SelectedIndex=3;
            Expect(panes[0].SelectedIndex==3 && panes[1].SelectedIndex==3,"aligned selection");
            Layout(host,"sql");
            var inspector=Visuals<Expander>(host).Single(x=>Object.Equals(x.Header,"当前行对照"));
            var inspected=Visuals<TextBlock>(inspector).Select(t=>t.Text.Trim()).ToList();
            Expect(inspected.Contains("SELECT OrderId, Total") && inspected.Contains("SELECT OrderId, Subtotal"),"bottom inspector shows both selected lines");
            string common=String.Join("\n",Enumerable.Range(1,90).Select(i=>"PRINT "+i));
            var leftProc=Obj("dbo","Changes","存储过程","SELECT 950\nBEGIN\nPRINT 'left only'\n"+common+"\nEND\nRETURN 0");
            var rightProc=Obj("dbo","Changes","存储过程","SELECT 940\nBEGIN\n"+common+"\nPRINT 'right only'\nEND\nRETURN 1");
            session.Open(new Result{Left=leftProc,Right=rightProc,Status=Result.Changed});
            Layout(host,"navigation");
            var active=(DetailControls)((FrameworkElement)((TabItem)tabs.SelectedItem).Content).Tag;
            var lineGrid=Visuals<DataGrid>(host).Single(g=>g.Name=="LeftLines");
            active.Jump(1);Expect(lineGrid.SelectedIndex==2,"jump to deletion");
            active.Jump(1);Expect(lineGrid.SelectedIndex==93,"jump to insertion across long unchanged region");
            active.Jump(-1);Expect(lineGrid.SelectedIndex==2,"previous difference");
            ((MenuItem)menu.Items[3]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(lineGrid.Items.Count==1 && ((CodeRow)lineGrid.Items[0]).Line.Text=="PRINT 'left only'","SQL left-only row filter");
            ((MenuItem)menu.Items[4]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(lineGrid.Items.Count==1 && ((CodeRow)lineGrid.Items[0]).Line==null,"SQL right-only with left placeholder");
            ((MenuItem)menu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Expect(lineGrid.Items.Count==92 && active.Jump==null,"SQL same filter excludes differences");
            ui.Close(overview);tabs.SelectedItem=home;
            ((TreeViewItem)tree.Items[0]).IsSelected=true;Layout(host,"home");
            Expect(tabs.Items.Count==1,"return to home after closing the last comparison");
            var historyDay=(TreeViewItem)((TreeViewItem)tree.Items[1]).Items[0];
            var historyLeaf=historyDay.Items.Cast<TreeViewItem>().Single(n=>((HistoryEntry)n.Tag).Path==first.Path);
            DoubleClick((TextBlock)((StackPanel)historyLeaf.Header).Children[1],MouseButton.Left);
            Expect(ui.Current!=null && ui.Current.Name==first.Name && tabs.SelectedContent==ui.Current.View,"history double-click displays snapshot page");
            var reading=ui.Current;PumpUntil(()=>!reading.Busy);host.UpdateLayout();
            Expect(tabs.SelectedItem==reading.Tab && Visuals<DataGrid>(host).Contains(reading.Objects),"snapshot page remains visible after loading");
            ui.Close(reading.Tab);tabs.SelectedItem=home;host.UpdateLayout();
            var newBranch=(TreeViewItem)tree.Items[0];newBranch.IsExpanded=true;host.UpdateLayout();
            var newLeaf=(TreeViewItem)newBranch.Items[0];
            DoubleClick((TextBlock)((StackPanel)newLeaf.Header).Children[1],MouseButton.Left);
            Expect(tabs.Items.Count==2 && ui.Current!=null && tabs.SelectedContent==ui.Current.View &&
                Visuals<TextBox>(host).Contains(ui.Current.Get<TextBox>("LeftConn")),"home double-click displays the new comparison form");
            ui.Close(ui.Current.Tab);tabs.SelectedItem=home;
            savedBranch=(TreeViewItem)tree.Items[1];dateBranch=(TreeViewItem)savedBranch.Items[0];
            var target=dateBranch.Items.Cast<TreeViewItem>().Single(n=>((HistoryEntry)n.Tag).Path==secret.Path);
            var other=dateBranch.Items.Cast<TreeViewItem>().Single(n=>((HistoryEntry)n.Tag).Path==first.Path);
            other.IsSelected=true;
            Expect(dateBranch.ContextMenu==null && target.ContextMenu.Items.Count==2,"only records offer rename and delete");
            ((MenuItem)target.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var nameInput=(TextBox)ui.Window.FindName("SavedName");
            var saveName=(Button)ui.Window.FindName("SaveName");
            Expect(nameInput.Text==secret.Name && tree.SelectedItem==target,"rename targets invoked record");
            nameInput.Text="  ";
            Expect(!saveName.IsEnabled,"blank names cannot be saved");
            nameInput.Text="归档核对";
            saveName.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(()=>((StackPanel)ui.Window.FindName("RenameForm")).IsEnabled);
            var renamed=restarted.List(out skipped).Single(h=>h.Path==secret.Path);
            Expect(renamed.Name=="归档核对" && renamed.CompletedUtc==secret.CompletedUtc && renamed.LeftConnection==secret.LeftConnection,"saved name survives reload with original metadata");
            Expect(((TextBlock)ui.Window.FindName("HistoryTitle")).Text==renamed.Name && restarted.List(out skipped).Single(h=>h.Path==first.Path).Name==first.Name,"home refreshed only targeted name");
            var renamedSnapshots=restarted.Open(renamed);
            Expect(Result.Compare(a,renamedSnapshots[0]).All(r=>r.Status==Result.Same || r.Status==Result.Unknown) &&
                Result.Compare(b,renamedSnapshots[1]).All(r=>r.Status==Result.Same || r.Status==Result.Unknown),"renamed snapshot data preserved");
            history.Delete(secret);
            Expect(restarted.List(out skipped).Count==3,"delete only the selected run");
            string damaged=Path.Combine(folder,"damaged.dbch");File.WriteAllBytes(damaged,new byte[]{1,0});
            Expect(history.List(out skipped).Count==3 && skipped==1,"invalid record isolated from other history");
            File.Delete(damaged);
            // Construct an original version-1 file with an omitted password and two empty object sets.
            string legacyPath=Path.Combine(folder,"legacy.dbch");
            using(var file=File.Create(legacyPath)) {
                using(var writer=new BinaryWriter(file,System.Text.Encoding.UTF8,true)) {
                    writer.Write("DBCompare");writer.Write(1);writer.Write(DateTime.UtcNow.Ticks);
                    writer.Write(a.Name);writer.Write(b.Name);writer.Write(0);writer.Write(0);writer.Write(0);
                    writer.Write("Server=dev-sql;Database=Sales;UID=test");writer.Write(cb.Text);
                    writer.Write(true);writer.Write(false);
                }
                using(var zipped=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Compress))
                using(var writer=new BinaryWriter(zipped)) {writer.Write(0);writer.Write(0);}
            }
            var legacy=history.List(out skipped).Single(entry=>entry.Path==legacyPath);
            history.Rename(legacy,"旧版快照");
            legacy=restarted.List(out skipped).Single(entry=>entry.Path==legacyPath);
            var reopen=ui.OpenStored(legacy);PumpUntil(()=>reopen.IsCompleted);reopen.GetAwaiter().GetResult();
            var oldSession=ui.Current;int priorCalls=calls;
            var retry=oldSession.Compare();PumpUntil(()=>retry.IsCompleted);
            Expect(legacy.LeftNeedsPassword && status.Text.Contains("左侧") && oldSession.Status.Contains("补全") &&
                oldSession.Get<Expander>("Connections").IsExpanded && calls==priorCalls,"v1 snapshot opens and identifies which side needs a password");
            var another=ui.OpenStored(first);PumpUntil(()=>another.IsCompleted);another.GetAwaiter().GetResult();
            Expect(tabs.Items.Count==3 && ui.Current.Name==first.Name && oldSession.Name=="旧版快照","renamed legacy snapshot retains name and can open");
            Layout(host,"named-tabs");
            LanguageTests(ui,host,tabs,settings,first,proc,languagePath);
            foreach(var saved in restarted.List(out skipped)) history.Delete(saved);
            File.Delete(languagePath);
            Directory.Delete(folder);
            ui.Window.Close();
        }
        static void LanguageTests(App ui,FrameworkElement host,TabControl tabs,LanguageSettings settings,
            HistoryEntry saved,Result proc,string path) {
            Expect(LanguageSettings.SystemLanguage(new System.Globalization.CultureInfo("ja-JP"))=="ja-JP" &&
                LanguageSettings.SystemLanguage(new System.Globalization.CultureInfo("de-DE"))=="en-US","Japanese detection and English fallback");
            var session=ui.Current;
            var table=session.Objects.Items.Cast<Result>().First(r=>r.Kind=="表" && r.Status==Result.Changed);
            session.Open(table);
            var tableTab=(TabItem)tabs.SelectedItem;
            session.Open(proc);
            var sqlTab=(TabItem)tabs.SelectedItem;
            var states=(DetailControls)((FrameworkElement)sqlTab.Content).Tag;
            states.Apply("不同");
            var options=ui.OptionsDialog();var panel=(FrameworkElement)options.Content;
            panel.Measure(new Size(540,250));panel.Arrange(new Rect(0,0,540,250));panel.UpdateLayout();
            RenderPreview(panel,"options");
            var choice=Visuals<ComboBox>(panel).Single();
            Expect(choice.Items.Count==3 && (string)((ComboBoxItem)choice.SelectedItem).Tag=="zh-CN" &&
                ((TreeViewItem)Visuals<TreeView>(panel).Single().SelectedItem).Header.Equals("共通设定"),"options opens on General with current language");
            choice.SelectedIndex=1;
            Visuals<Button>(panel).Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(host,"japanese-detail");
            Expect(L.Current=="ja-JP" && File.ReadAllText(path)=="ja-JP" && ui.Window.Title.Contains("構造比較") &&
                ((MenuItem)ui.Window.FindName("OptionsMenu")).Header.Equals("オプション…"),"OK updates resources and persists Japanese");
            Expect(tabs.SelectedItem==sqlTab && states.Mode=="不同" && states.Summary().Contains("箇所") &&
                Visuals<Expander>(host).Any(e=>Object.Equals(e.Header,"選択行の比較")),"already-open SQL detail changes language and retains filter");
            var dismissed=ui.OptionsDialog();var dp=(FrameworkElement)dismissed.Content;
            dp.Measure(new Size(540,250));dp.Arrange(new Rect(0,0,540,250));dp.UpdateLayout();
            Visuals<ComboBox>(dp).Single().SelectedIndex=2;dismissed.Close();
            Expect(L.Current=="ja-JP" && File.ReadAllText(path)=="ja-JP","dismissed choice leaves language untouched");
            ui.ChangeLanguage("en-US");tabs.SelectedItem=tableTab;Layout(host,"english-properties");
            var props=Visuals<DataGrid>(host).Single();
            Expect(props.Columns[0].Header.Equals("Schema properties") &&
                Visuals<TextBlock>(props).Any(t=>t.Text.Contains("Maximum length (bytes)")),"open table property headers and cells translated");
            Expect(L.PropertyName("字段 [主键 / 表]]a] / 允许 NULL")=="Column [主键 / 表]]a] / Allow NULL" &&
                L.PropertyValue("字段 [表] / 允许 NULL","是")=="Yes" && L.PropertyValue("外键 [x] / 引用表","是")=="是","identifiers and non-boolean values are verbatim");
            tabs.SelectedItem=session.Tab;Layout(host,"english-overview");
            Expect(session.Name==saved.Name && session.Status.Contains("Saved snapshot") && table.DisplayStatus=="Content changed" &&
                Visuals<TextBlock>(session.Objects).Any(t=>t.Text=="Tables") && session.Objects.Columns[1].Header.Equals("Result"),"history overview translates and keeps saved name");
            Expect(session.Objects.Items.Cast<Result>().Count(x=>App.MatchMode(x.Status,"不同"))==7,"language does not change comparisons");
            var restart=new App(new HistoryStore(Path.GetDirectoryName(path)),null,settings);
            Expect(L.Current=="en-US" && restart.Window.Title.Contains("Schema Compare") &&
                settings.Load(new System.Globalization.CultureInfo("zh-CN"))=="en-US","next launch prefers saved language over system");
            restart.Window.Close();
            ui.ChangeLanguage("zh-CN");
            Expect(table.Detail.Contains("项属性") && session.Status.Contains("历史快照"),"round-trip to Chinese");
        }
        static void PumpUntil(Func<bool> done) {
            var until=DateTime.UtcNow.AddSeconds(12);
            while(!done()) {
                if(DateTime.UtcNow>until) throw new TimeoutException("Timed out waiting for history UI");
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
                Thread.Sleep(2);
            }
        }
        static void DoubleClick(UIElement hit,MouseButton button) {
            var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,button);
            // A real input device supplies ClickCount before WPF routes the second mouse-down.
            typeof(MouseButtonEventArgs).GetProperty("ClickCount").GetSetMethod(true).Invoke(e,new object[]{2});
            e.RoutedEvent=Mouse.PreviewMouseDownEvent;
            hit.RaiseEvent(e);
            if(!e.Handled) {e.RoutedEvent=Mouse.MouseDownEvent;hit.RaiseEvent(e);}
            hit.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
        }
        static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
                var child=VisualTreeHelper.GetChild(root,i);
                if(child is T) yield return (T)child;
                foreach(var item in Visuals<T>(child)) yield return item;
            }
        }
        static void Layout(FrameworkElement root,string name) {
            var size=new Size(1150,680);root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
            root.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>{}));
            root.UpdateLayout();
            Expect(root.ActualWidth>800 && root.ActualHeight>500,"layout "+name);
            RenderPreview(root,name);
        }
        static void RenderPreview(FrameworkElement root,string name) {
            var png=Environment.GetEnvironmentVariable("DBCOMPARE_TEST_RENDER");
            if(String.IsNullOrEmpty(png)) return;
            var image=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using(var file=File.Create(Path.Combine(png,name+".png"))) encoder.Save(file);
        }
        [STAThread] static int Main() {
            try {TextTests();CatalogTests();UI();Console.WriteLine("Passed "+checks+" checks.");return 0;}
            catch(Exception e) {Console.Error.WriteLine(e.GetType().Name+" "+e.Message);return 1;}
        }
    }
}
