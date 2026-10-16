using System.Windows.Controls;
using System.Windows.Input;
using DrawingFlow.Desktop.ViewModels;

namespace DrawingFlow.Desktop.Views.Pages;

public partial class DocumentsView : UserControl
{
    public DocumentsView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DocumentsViewModel viewModel && viewModel.OpenSelectedCommand.CanExecute(null))
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is DocumentsViewModel viewModel && !string.IsNullOrEmpty(e.Column.SortMemberPath))
        {
            viewModel.SortByCommand.Execute(e.Column.SortMemberPath);
        }
    }
}
