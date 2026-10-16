using System.Windows;

namespace DrawingFlow.Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(PasswordInput.Password))
            {
                PasswordInput.Focus();
            }
        };
    }
}
