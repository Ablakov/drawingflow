using System.Windows;
using DrawingFlow.Desktop.ViewModels.Dialogs;

namespace DrawingFlow.Desktop.Views.Dialogs;

public partial class DialogWindow : Window
{
    private DialogViewModelBase? _viewModel;

    public DialogWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        if (e.NewValue is DialogViewModelBase viewModel)
        {
            _viewModel = viewModel;
            _viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(bool result)
    {
        DialogResult = result;
        Close();
    }

    private void Detach()
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel = null;
        }
    }
}
