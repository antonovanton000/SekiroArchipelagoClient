using SekiroAPClient.ViewModels;
using System.Windows;
using System.Windows.Controls;
using SekiroAPClient.Models;

namespace SekiroAPClient.Views;

public partial class ItemTrackerWindow : Window
{
    public ItemTrackerWindow()
    {
        InitializeComponent();
        DataContextChanged += ItemTrackerWindow_DataContextChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        DataContextChanged -= ItemTrackerWindow_DataContextChanged;
        if (DataContext is ItemTrackerViewModel viewModel)
            viewModel.RecentlyPickedEntryRequested -= ViewModel_RecentlyPickedEntryRequested;

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }

    private void ItemTrackerWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ItemTrackerViewModel oldViewModel)
            oldViewModel.RecentlyPickedEntryRequested -= ViewModel_RecentlyPickedEntryRequested;

        if (e.NewValue is ItemTrackerViewModel newViewModel)
            newViewModel.RecentlyPickedEntryRequested += ViewModel_RecentlyPickedEntryRequested;
    }

    private async void ViewModel_RecentlyPickedEntryRequested(ItemTrackerEntry entry)
    {
        await Dispatcher.InvokeAsync(() => ScrollToEntry(entry));

        await Dispatcher.InvokeAsync(() => entry.IsRecentlyPicked = false);
        await Task.Delay(50);
        await Dispatcher.InvokeAsync(() => entry.IsRecentlyPicked = true);

        await Task.Delay(1800);
        await Dispatcher.InvokeAsync(() => entry.IsRecentlyPicked = false);
    }

    private void ScrollToEntry(ItemTrackerEntry entry)
    {
        ActiveEntriesGrid.UpdateLayout();

        if (!ActiveEntriesGrid.Items.Contains(entry))
            return;

        ActiveEntriesGrid.ScrollIntoView(entry);
        ActiveEntriesGrid.UpdateLayout();

        if (ActiveEntriesGrid.ItemContainerGenerator.ContainerFromItem(entry) is DataGridRow row)
            row.BringIntoView();
    }

}
