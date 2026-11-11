using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace DBCompare {
    internal sealed partial class App {
        readonly LanguageSettings languageSettings;

        internal void ChangeLanguage(string code) {
            languageSettings.Save(code);
            L.Use(code);
            Window.Language=XmlLanguage.GetLanguage(code);
            foreach(TabItem tab in pages.Items) {
                var session=tab.Tag as ComparisonSession;
                if(session!=null && session.Tab==tab) session.RefreshLanguage();
            }
            var edit=editingName;
            string pending=Get<TextBox>("SavedName").Text;
            FilterHistory();
            if(edit!=null && SelectedHistory!=null && SelectedHistory.Path==edit.Path) {
                BeginRename(SelectedHistory);Get<TextBox>("SavedName").Text=pending;
            }
            SyncToolbar();
        }
        bool ConfirmDelete(HistoryEntry entry) {
            var panel=new StackPanel{Margin=new Thickness(18)};
            var dialog=new Window{Owner=Window,Title=L.T("删除履历"),Content=panel,Width=450,SizeToContent=SizeToContent.Height,
                ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,
                Language=Window.Language,FontFamily=Window.FontFamily,FontSize=Window.FontSize};
            panel.Children.Add(new TextBlock{Text=L.F("删除「{0}」在 {1} 的履历及结构快照？",entry.Name,entry.Time),TextWrapping=TextWrapping.Wrap});
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};
            var remove=new Button{Content=L.T("删除"),MinWidth=78};
            remove.Click+=(s,e)=>dialog.DialogResult=true;
            var cancel=new Button{Content=L.T("取消"),IsCancel=true,MinWidth=78,Margin=new Thickness(6,0,0,0)};
            actions.Children.Add(remove);actions.Children.Add(cancel);panel.Children.Add(actions);
            dialog.Loaded+=(s,e)=>cancel.Focus();
            return dialog.ShowDialog()==true;
        }
        internal Window OptionsDialog() {
            var dialog=new Window{Title=L.T("选项"),Width=560,Height=310,MinWidth=450,MinHeight=240,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,FontFamily=Window.FontFamily,
                FontSize=Window.FontSize,Language=Window.Language,Background=Brushes.WhiteSmoke};
            var body=new DockPanel{Background=Brushes.WhiteSmoke};
            dialog.Content=new Border{Background=Brushes.WhiteSmoke,Padding=new Thickness(12),Child=body};
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            DockPanel.SetDock(buttons,Dock.Bottom);body.Children.Add(buttons);
            var ok=new Button{Content=L.T("确定"),MinWidth=82,IsDefault=true};
            var cancel=new Button{Content=L.T("取消"),MinWidth=82,Margin=new Thickness(8,0,0,0),IsCancel=true};
            buttons.Children.Add(ok);buttons.Children.Add(cancel);
            var tree=new TreeView{Width=140,Margin=new Thickness(0,0,12,0)};
            var general=new TreeViewItem{Header=L.T("共通设定"),IsSelected=true,Padding=new Thickness(4)};
            tree.Items.Add(general);DockPanel.SetDock(tree,Dock.Left);body.Children.Add(tree);
            var pane=new StackPanel{Margin=new Thickness(3,3,3,0)};body.Children.Add(pane);
            var combo=new ComboBox{Name="Language",Height=30,VerticalContentAlignment=VerticalAlignment.Center};
            pane.Children.Add(new Label{Content=L.T("语言"),Target=combo,Padding=new Thickness(0,0,0,6)});
            pane.Children.Add(combo);
            foreach(var item in new[]{new[]{"zh-CN","简体中文"},new[]{"ja-JP","日本語"},new[]{"en-US","English"}}) {
                var option=new ComboBoxItem{Tag=item[0],Content=item[1]};
                combo.Items.Add(option);
                if(item[0]==L.Current) combo.SelectedItem=option;
            }
            var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick,Margin=new Thickness(0,8,0,0)};
            pane.Children.Add(error);
            ok.Click+=(s,e)=>{
                try {ChangeLanguage((string)((ComboBoxItem)combo.SelectedItem).Tag);dialog.Close();}
                catch(System.IO.IOException ex) {error.Text=L.F("保存语言设定失败：{0}",ex.Message);}
                catch(UnauthorizedAccessException ex) {error.Text=L.F("保存语言设定失败：{0}",ex.Message);}
            };
            return dialog;
        }
    }
}
