using System.Text.Json;
using System.Windows;

namespace SdaDesk;

public partial class CreateMaFileWindow : Window
{
    public MaFile? Result { get; private set; }
    public CreateMaFileWindow() => InitializeComponent();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = AccountNameBox.Text.Trim();
        var secret = SharedSecretBox.Text.Trim();
        if (name.Length == 0) { MessageBox.Show(this, "Укажи имя аккаунта.", "Проверь данные", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (secret.Length == 0) { MessageBox.Show(this, "Укажи shared_secret.", "Проверь данные", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            _ = Convert.FromBase64String(secret);
            Result = new MaFile { AccountName = name, SharedSecret = secret, IdentitySecret = EmptyToNull(IdentitySecretBox.Text), RevocationCode = EmptyToNull(RevocationCodeBox.Text), SerialNumber = EmptyToNull(SerialNumberBox.Text) };
            DialogResult = true;
        }
        catch (FormatException) { MessageBox.Show(this, "shared_secret должен быть корректной строкой Base64.", "Проверь данные", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
