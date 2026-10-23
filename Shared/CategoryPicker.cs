using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    /// <summary>Filterable check list with All / None / Invert. Check state survives filtering.</summary>
    internal class CategoryPicker : UserControl
    {
        readonly TextBox _filter = new TextBox { Dock = DockStyle.Fill };
        readonly CheckedListBox _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
        readonly Label _count = new Label { AutoSize = true, ForeColor = System.Drawing.SystemColors.GrayText };
        readonly HashSet<string> _checked = new HashSet<string>();
        List<CatItem> _all = new List<CatItem>();
        bool _busy;

        public CategoryPicker()
        {
            Dock = DockStyle.Fill;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(_filter, 0, 0);
            grid.Controls.Add(Ui.Btn("All", (s, e) => SetVisible(k => true)), 1, 0);
            grid.Controls.Add(Ui.Btn("None", (s, e) => SetVisible(k => false)), 2, 0);
            grid.Controls.Add(Ui.Btn("Invert", (s, e) => SetVisible(k => !_checked.Contains(k))), 3, 0);
            grid.Controls.Add(_list, 0, 1);
            grid.SetColumnSpan(_list, 4);
            grid.Controls.Add(_count, 0, 2);
            grid.SetColumnSpan(_count, 4);
            Controls.Add(grid);

            new ToolTip().SetToolTip(_filter, "Type to filter the list (name or OST_ key)");
            _filter.TextChanged += (s, e) => Rebuild();
            _list.ItemCheck += OnItemCheck;
        }

        public void SetItems(IEnumerable<CatItem> items)
        {
            _all = items.ToList();
            Rebuild();
        }

        public void SetChecked(IEnumerable<string> keys)
        {
            _checked.Clear();
            foreach (string k in keys) _checked.Add(k);
            Rebuild();
        }

        public List<string> CheckedKeys { get { return _checked.ToList(); } }

        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_busy) return;
            var item = (CatItem)_list.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _checked.Add(item.Key); else _checked.Remove(item.Key);
            BeginInvoke(new Action(UpdateCount));
        }

        void Rebuild()
        {
            _busy = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            string q = _filter.Text.Trim().ToLowerInvariant();
            foreach (CatItem it in _all)
            {
                if (q.Length > 0 && it.Name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0 && it.Key.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                int i = _list.Items.Add(it);
                _list.SetItemChecked(i, _checked.Contains(it.Key));
            }
            _list.EndUpdate();
            _busy = false;
            UpdateCount();
        }

        void SetVisible(Func<string, bool> newState)
        {
            var keys = _list.Items.Cast<CatItem>().Select(i => i.Key).ToList();
            foreach (string k in keys) { if (newState(k)) _checked.Add(k); else _checked.Remove(k); }
            Rebuild();
        }

        void UpdateCount()
        {
            _count.Text = _checked.Count + " checked  -  " + _list.Items.Count + " shown  -  " + _all.Count + " in list";
        }
    }
}
