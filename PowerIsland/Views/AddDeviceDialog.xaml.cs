using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerIsland.Models;

namespace PowerIsland.Views;

public partial class AddDeviceDialog : Window
{
    private readonly List<CatalogRow> _all = new();
    private readonly ObservableCollection<CatalogRow> _visible = new();

    public List<string> SelectedIds { get; private set; } = new();

    public AddDeviceDialog(IEnumerable<string> selected)
    {
        InitializeComponent();

        var set = new HashSet<string>(selected);
        foreach (CatalogEntry entry in DeviceCatalog.All)
        {
            var row = new CatalogRow(entry) { IsSelected = set.Contains(entry.Id) };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CatalogRow.IsSelected)) UpdateCount();
            };
            _all.Add(row);
        }

        DeviceList.ItemsSource = _visible;
        ApplyFilter(string.Empty);
        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void ApplyFilter(string keyword)
    {
        keyword = keyword.Trim().ToLowerInvariant();
        _visible.Clear();
        foreach (CatalogRow row in _all)
        {
            if (keyword.Length == 0 || row.Entry.SearchText.Contains(keyword)) _visible.Add(row);
        }

        SearchHint.Visibility = keyword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCount()
    {
        int selected = _all.Count(r => r.IsSelected);
        CountText.Text = selected == 0
            ? "未选择 · 将自动识别所有已插入的外设"
            : $"已选 {selected} 台设备";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { /* 忽略 */ }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SelectedIds = _all.Where(r => r.IsSelected).Select(r => r.Entry.Id).ToList();
        DialogResult = true;
        Close();
    }
}

public sealed class CatalogRow : INotifyPropertyChanged
{
    private bool _selected;

    public CatalogRow(CatalogEntry entry) => Entry = entry;

    public CatalogEntry Entry { get; }
    public string ArtKey => Entry.ArtKey;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
