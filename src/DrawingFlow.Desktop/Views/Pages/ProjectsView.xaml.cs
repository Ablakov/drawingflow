using System.Windows.Controls;
using System.Windows.Input;
using DrawingFlow.Desktop.ViewModels;

namespace DrawingFlow.Desktop.Views.Pages;

public partial class ProjectsView : UserControl
{
    public ProjectsView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProjectsViewModel viewModel && viewModel.OpenSelectedCommand.CanExecute(null))
        {
            viewModel.OpenSelectedCommand.Execute(null);
        }
    }

    /// <summary>Сортировка выполняется на сервере: перехватываем щелчок по заголовку и передаём поле модели.</summary>
    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is ProjectsViewModel viewModel && !string.IsNullOrEmpty(e.Column.SortMemberPath))
        {
            viewModel.SortByCommand.Execute(e.Column.SortMemberPath);
        }
    }
}
