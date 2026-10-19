using System.Windows;

namespace PanelTracker.App.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string label, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initialValue ?? string.Empty;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
