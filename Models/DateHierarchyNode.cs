using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NihongoVocab.Models
{
    public partial class DateHierarchyNode : ObservableObject
    {
        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private bool? _isChecked = false;

        [ObservableProperty]
        private bool _isExpanded = true;

        public List<int> WordIds { get; } = new();

        public ObservableCollection<DateHierarchyNode> Children { get; } = new();

        public DateHierarchyNode? Parent { get; set; }

        private bool _isUpdating = false;

        partial void OnIsCheckedChanged(bool? value)
        {
            if (!_isUpdating)
            {
                SetChecked(value);
            }
        }

        public void SetChecked(bool? value, bool updateChildren = true, bool updateParent = true)
        {
            if (_isUpdating) return;
            _isUpdating = true;

            IsChecked = value;

            if (updateChildren && value.HasValue)
            {
                foreach (var child in Children)
                {
                    child.SetChecked(value.Value, true, false);
                }
            }

            _isUpdating = false;

            if (updateParent && Parent != null)
            {
                Parent.RecalculateParentCheckState();
            }
        }

        public void RecalculateParentCheckState()
        {
            if (Children.Count == 0) return;

            bool allTrue = Children.All(c => c.IsChecked == true);
            bool allFalse = Children.All(c => c.IsChecked == false);

            if (allTrue)
            {
                IsChecked = true;
            }
            else if (allFalse)
            {
                IsChecked = false;
            }
            else
            {
                IsChecked = null; // 三态：部分选中
            }

            Parent?.RecalculateParentCheckState();
        }

        public void SetExpandedRecursive(bool expanded)
        {
            IsExpanded = expanded;
            foreach (var child in Children)
            {
                child.SetExpandedRecursive(expanded);
            }
        }

        public IEnumerable<int> GetAllSelectedWordIds()
        {
            var result = new HashSet<int>();
            CollectSelectedIds(this, result);
            return result;
        }

        public IEnumerable<int> GetAllContainedWordIds()
        {
            var result = new HashSet<int>();
            CollectAllIds(this, result);
            return result;
        }

        private static void CollectSelectedIds(DateHierarchyNode node, HashSet<int> set)
        {
            if (node.IsChecked == true)
            {
                foreach (var id in node.WordIds)
                {
                    set.Add(id);
                }
                foreach (var child in node.Children)
                {
                    CollectAllIds(child, set);
                }
            }
            else
            {
                foreach (var child in node.Children)
                {
                    CollectSelectedIds(child, set);
                }
            }
        }

        private static void CollectAllIds(DateHierarchyNode node, HashSet<int> set)
        {
            foreach (var id in node.WordIds) set.Add(id);
            foreach (var child in node.Children) CollectAllIds(child, set);
        }
    }
}
