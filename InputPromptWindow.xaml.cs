using System.Windows;

namespace SdaDesk;

public partial class InputPromptWindow : Window
{
    public string Value => ValueBox.Text.Trim();
    public InputPromptWindow() => InitializeComponent();

    public void Configure(string title, string description, string initialValue = "")
    {
        PromptTitle.Text = title;
        PromptDescription.Text = description;
        ValueBox.Text = initialValue;
        ValueBox.SelectAll();
        ValueBox.Focus();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
