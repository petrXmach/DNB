using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace DNBridge.Wpf;

/// <summary>
/// Reusable master/detail traffic log. Master lists one summary line per frame
/// (colored by direction); clicking a line shows its full detail in the pane below.
/// Bounded to MaxItems so memory/cost stay constant under load.
/// </summary>
public partial class TrafficLogView : UserControl
{
    private const int MaxItems = 500;

    private readonly ObservableCollection<TrafficItem> _items = new();

    public TrafficLogView()
    {
        InitializeComponent();
        MasterList.ItemsSource = _items;
    }

    /// <summary>Appends an item, auto-scrolls to it, and trims the oldest beyond MaxItems.</summary>
    public void Append(TrafficItem item)
    {
        _items.Add(item);

        while (_items.Count > MaxItems)
            _items.RemoveAt(0);

        // Auto-scroll to the newest entry unless the user is inspecting an older one.
        if (MasterList.SelectedItem == null)
            MasterList.ScrollIntoView(item);
    }

    private void MasterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailBox.Text = (MasterList.SelectedItem as TrafficItem)?.Detail ?? "";
    }
}
