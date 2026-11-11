using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace DBCompare {
    internal sealed class ObjectCategory : INotifyPropertyChanged {
        public string Kind { get; private set; }
        public string DisplayKind {get{return L.T(Kind);}}
        public void RefreshLabel() {
            var handler=PropertyChanged;
            if(handler!=null) handler(this,new PropertyChangedEventArgs("DisplayKind"));
        }
        bool expanded = true;
        public bool Expanded {
            get { return expanded; }
            set {
                if (value == expanded) return;
                expanded = value;
                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("Expanded"));
            }
        }
        public ObjectCategory(string kind) { Kind = kind; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class ObjectGroups : GroupDescription {
        readonly Dictionary<string,ObjectCategory> categories;
        public ObjectGroups(IEnumerable<ObjectCategory> ordered, IEnumerable<Result> matches) {
            categories = ordered.ToDictionary(c => c.Kind);
            var present = new HashSet<string>(matches.Select(r => r.Kind));
            foreach (var category in ordered)
                if (present.Contains(category.Kind)) GroupNames.Add(category);
        }
        public override object GroupNameFromItem(object item, int level, CultureInfo culture) {
            return categories[((Result)item).Kind];
        }
    }
}
