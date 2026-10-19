using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PanelTracker.App.ViewModels;

namespace PanelTracker.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Board_SelectionChanged(object sender, SelectionChangedEventArgs e) => ViewModel?.OnSelectionChanged();

    /// <summary>Правый щелчок по невыделенной плитке выделяет только её; по выделенной — сохраняет множественное выделение.</summary>
    private void Board_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(Board, source) is ListBoxItem { IsSelected: false } item)
        {
            Board.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void Board_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(Board, source) is ListBoxItem
            && ViewModel?.OpenFileCommand.CanExecute(null) == true)
        {
            ViewModel.OpenFileCommand.Execute(null);
        }
    }
}
