using System.Windows;

namespace SdaDesk;

public partial class ActivationPromptWindow : Window
{
    public string SmsCode => SmsCodeBox.Text.Trim();
    public string EnteredRevocationCode => RevocationCodeBox.Text.Trim();
    public ActivationPromptWindow(string revocationCode)
    {
        InitializeComponent();
        RevocationCodeText.Text = revocationCode;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SmsCodeBox.Text) || string.IsNullOrWhiteSpace(RevocationCodeBox.Text)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
