using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DroneFlasher.Helpers;

public static class ListBoxBehavior
{
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(ListBoxBehavior),
            new PropertyMetadata(false, OnAutoScrollChanged));

    public static bool GetAutoScroll(DependencyObject obj) => (bool)obj.GetValue(AutoScrollProperty);
    public static void SetAutoScroll(DependencyObject obj, bool value) => obj.SetValue(AutoScrollProperty, value);

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;

        if ((bool)e.NewValue)
        {
            void Subscribe(INotifyCollectionChanged col)
            {
                col.CollectionChanged += (_, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add && listBox.Items.Count > 0)
                    {
                        listBox.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                        {
                            if (listBox.Items.Count > 0)
                            {
                                listBox.ScrollIntoView(listBox.Items[^1]);
                            }
                        });
                    }
                };
            }

            if (listBox.ItemsSource is INotifyCollectionChanged currentCollection)
            {
                Subscribe(currentCollection);
            }
            else
            {
                listBox.Loaded += (_, _) =>
                {
                    if (listBox.ItemsSource is INotifyCollectionChanged loadedCollection)
                    {
                        Subscribe(loadedCollection);
                    }
                };
            }
        }
    }
}