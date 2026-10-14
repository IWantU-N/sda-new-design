using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SdaDesk;

public partial class LinkSteamWindow : Window
{
    private readonly string _accountsDirectory;
    private readonly string _pendingDirectory;
    private CancellationTokenSource? _cancellation;
    private bool _completed;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? CreatedPath { get; private set; }

    public LinkSteamWindow(string accountsDirectory)
    {
        InitializeComponent();
        _accountsDirectory = accountsDirectory;
        _pendingDirectory = Path.Combine(Path.GetDirectoryName(accountsDirectory)!, "pending");
    }

    private async void Link_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (username.Length == 0 || password.Length == 0)
        {
            MessageBox.Show(this, "Введи логин и пароль Steam.", "Нужны данные для входа", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusy(true);
        _cancellation = new CancellationTokenSource();
        SteamClient? steamClient = null;
        string? pendingPath = null;
        AuthenticatorLinker? linker = null;
        try
        {
            UpdateProgress("Подключаемся к Steam…");
            steamClient = new SteamClient();
            steamClient.Connect();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!steamClient.IsConnected && DateTime.UtcNow < deadline)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                await Task.Delay(250, _cancellation.Token);
            }
            if (!steamClient.IsConnected) throw new TimeoutException("Не удалось подключиться к Steam за 30 секунд.");

            UpdateProgress("Выполняем вход. Если Steam запросит код из почты, появится отдельное окно.");
            var authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = username,
                Password = password,
                IsPersistentSession = false,
                PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                ClientOSType = EOSType.Android9,
                Authenticator = new SteamPromptAuthenticator(this),
            });
            await Dispatcher.InvokeAsync(() => PasswordBox.Clear());
            password = string.Empty;

            UpdateProgress("Ожидаем подтверждение входа Steam…");
            var pollResponse = await authSession.PollingWaitForResultAsync(_cancellation.Token);
            var session = new SteamAuth.SessionData
            {
                SteamID = authSession.SteamID.ConvertToUInt64(),
                AccessToken = pollResponse.AccessToken,
                RefreshToken = pollResponse.RefreshToken,
            };
            steamClient.Disconnect();
            steamClient = null;

            linker = new AuthenticatorLinker(session);
            UpdateProgress("Проверяем возможность привязки аутентификатора…");
            var linkResult = await linker.AddAuthenticator();
            if (linkResult == AuthenticatorLinker.LinkResult.MustProvidePhoneNumber)
            {
                var phoneResult = await linker.AddPhoneNumber();
                if (phoneResult == AuthenticatorLinker.PhoneLinkResult.MustProvidePhoneNumber)
                {
                    var phone = await PromptAsync("Номер телефона", "Steam требует подтверждённый номер. Введи номер без кода страны, только национальную часть.");
                    if (phone is null) throw new OperationCanceledException("Ввод номера отменён.");
                    var countryCode = await PromptAsync("Код страны", "Введи двухбуквенный код страны номера по ISO, например UA или US.");
                    if (countryCode is null) throw new OperationCanceledException("Ввод кода страны отменён.");
                    var nationalNumber = new string(phone.Where(char.IsDigit).ToArray());
                    var isoCountryCode = countryCode.Trim().ToUpperInvariant();
                    if (nationalNumber.Length < 5 || isoCountryCode.Length != 2 || !isoCountryCode.All(char.IsLetter))
                        throw new InvalidOperationException("Проверь номер телефона и двухбуквенный код страны, например UA.");
                    linker.PhoneNumber = nationalNumber;
                    linker.PhoneCountryCode = isoCountryCode;
                    phoneResult = await linker.AddPhoneNumber();
                }
                while (phoneResult == AuthenticatorLinker.PhoneLinkResult.MustConfirmEmail)
                {
                    var proceed = await PromptAsync("Подтверди номер по почте", $"Steam отправил письмо для подтверждения номера{(string.IsNullOrWhiteSpace(linker.ConfirmationEmailAddress) ? string.Empty : $" на {linker.ConfirmationEmailAddress}")}. Открой письмо, перейди по ссылке подтверждения, затем введи ГОТОВО.");
                    if (proceed is null) throw new OperationCanceledException("Подтверждение номера отменено.");
                    phoneResult = await linker.AddPhoneNumber();
                }
                if (phoneResult == AuthenticatorLinker.PhoneLinkResult.MustConfirmSMS)
                {
                    var sms = await PromptAsync("Подтверди номер телефона", "Введи SMS-код проверки номера, отправленный Steam.");
                    if (sms is null) throw new OperationCanceledException("Проверка номера отменена.");
                    linker.PhoneSMSCode = sms;
                    phoneResult = await linker.AddPhoneNumber();
                }
                if (phoneResult != AuthenticatorLinker.PhoneLinkResult.PhoneAdded)
                    throw new InvalidOperationException($"Steam не подтвердил номер телефона: {phoneResult}.");
                UpdateProgress("Номер подтверждён. Запрашиваем данные аутентификатора…");
                linkResult = await linker.AddAuthenticator();
            }

            if (linkResult == AuthenticatorLinker.LinkResult.AuthenticatorPresent)
                throw new InvalidOperationException("На этом аккаунте уже подключён мобильный аутентификатор. Сначала управляй им через Steam.");
            if (linkResult == AuthenticatorLinker.LinkResult.MustRemovePhoneNumber)
                throw new InvalidOperationException("Steam просит удалить или повторно подтвердить номер телефона. Выполни это в официальном Steam и повтори привязку.");
            if (linkResult != AuthenticatorLinker.LinkResult.AwaitingFinalization || linker.LinkedAccount is null)
                throw new InvalidOperationException($"Steam не начал привязку. Ответ: {linkResult}.");

            var steamAccount = linker.LinkedAccount;
            if (string.IsNullOrWhiteSpace(steamAccount.RevocationCode))
                throw new InvalidOperationException("Steam не вернул revocation code; активация остановлена.");

            while (true)
            {
                var activation = await Dispatcher.InvokeAsync(() =>
                {
                    var prompt = new ActivationPromptWindow(steamAccount.RevocationCode) { Owner = this };
                    if (prompt.ShowDialog() != true) return ((string SmsCode, string RevocationCode)?)null;
                    return (prompt.SmsCode, prompt.EnteredRevocationCode);
                });
                if (activation is null)
                    throw new OperationCanceledException("Активация отменена. Steam ещё не подтвердил аутентификатор.");
                if (!string.Equals(activation.Value.RevocationCode, steamAccount.RevocationCode, StringComparison.OrdinalIgnoreCase))
                {
                    ShowMessage("Код восстановления не совпал. Запиши показанный код и введи его ещё раз.", "Проверь revocation code", MessageBoxImage.Warning);
                    continue;
                }

                var maFile = ConvertAccount(steamAccount);
                Directory.CreateDirectory(_pendingDirectory);
                pendingPath ??= UniquePath(_pendingDirectory, SafeFileName(maFile.AccountName ?? username));
                // Save recovery material before asking Steam to finalize activation.
                WriteAtomic(pendingPath, JsonSerializer.Serialize(maFile, JsonOptions));
                UpdateProgress("Сохранили временную копию секретов. Подтверждаем SMS-код в Steam…");

                var finalizeResult = await linker.FinalizeAddAuthenticator(activation.Value.SmsCode);
                if (finalizeResult == AuthenticatorLinker.FinalizeResult.BadSMSCode)
                {
                    ShowMessage("SMS-код не подошёл. Проверь сообщение Steam и попробуй ещё раз.", "Неверный SMS-код", MessageBoxImage.Warning);
                    continue;
                }
                if (finalizeResult != AuthenticatorLinker.FinalizeResult.Success)
                    throw new InvalidOperationException($"Steam не подтвердил аутентификатор: {finalizeResult}. Временные данные оставлены для восстановления: {pendingPath}");

                maFile.FullyEnrolled = true;
                maFile.Status = steamAccount.Status;
                var finalPath = UniquePath(_accountsDirectory, SafeFileName(maFile.AccountName ?? username));
                Directory.CreateDirectory(_accountsDirectory);
                WriteAtomic(pendingPath, JsonSerializer.Serialize(maFile, JsonOptions));
                File.Move(pendingPath, finalPath);
                pendingPath = null;
                CreatedPath = finalPath;
                _completed = true;
                UpdateProgress("Аутентификатор активирован, revocation code подтверждён. Аккаунт добавлен.");
                await Dispatcher.InvokeAsync(() => DialogResult = true);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            UpdateProgress(pendingPath is null ? "Привязка отменена." : $"Привязка остановлена. Временный maFile оставлен здесь: {pendingPath}");
            if (pendingPath is not null)
                ShowMessage($"Привязка остановлена. Временный файл с секретами оставлен без шифрования:\n{pendingPath}\n\nНе удаляй его, пока не убедишься, что аутентификатор не активирован в Steam.", "Требуется внимание", MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            UpdateProgress(pendingPath is null ? "Привязка не завершена." : $"Привязка не завершена. Временный файл: {pendingPath}");
            var pendingNote = pendingPath is null ? string.Empty : $"\n\nВременный maFile с секретами оставлен без шифрования:\n{pendingPath}";
            ShowMessage($"Не удалось подключить аккаунт.\n\n{ex.Message}{pendingNote}", "Ошибка привязки", MessageBoxImage.Error);
        }
        finally
        {
            Dispatcher.Invoke(() => PasswordBox.Clear());
            if (steamClient?.IsConnected == true) steamClient.Disconnect();
            _cancellation?.Dispose();
            _cancellation = null;
            if (!_completed) SetBusy(false);
        }
    }

    private static MaFile ConvertAccount(SteamGuardAccount account) => new()
    {
        AccountName = account.AccountName,
        SharedSecret = account.SharedSecret,
        IdentitySecret = account.IdentitySecret,
        RevocationCode = account.RevocationCode,
        SerialNumber = account.SerialNumber,
        Uri = account.URI,
        TokenGid = account.TokenGID,
        Secret1 = account.Secret1,
        DeviceId = account.DeviceID,
        ServerTime = account.ServerTime,
        Status = account.Status,
        FullyEnrolled = account.FullyEnrolled,
        PhoneNumberHint = account.PhoneNumberHint,
        ConfirmType = account.ConfirmType,
        Session = account.Session is null ? null : new MaFileSession
        {
            SteamId = account.Session.SteamID,
            AccessToken = account.Session.AccessToken,
            RefreshToken = account.Session.RefreshToken,
            SessionId = account.Session.SessionID,
        },
    };

    private async Task<string?> PromptAsync(string title, string description)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            var prompt = new InputPromptWindow { Owner = this };
            prompt.Configure(title, description);
            return prompt.ShowDialog() == true ? prompt.Value : null;
        });
    }

    private async Task<string> PromptAuthCodeAsync(string title, string description)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            var prompt = new InputPromptWindow { Owner = this };
            prompt.Configure(title, description);
            return prompt.ShowDialog() == true ? prompt.Value : string.Empty;
        });
    }

    private void UpdateProgress(string text)
    {
        if (Dispatcher.CheckAccess()) ProgressText.Text = text;
        else Dispatcher.Invoke(() => ProgressText.Text = text);
    }

    private void SetBusy(bool busy)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBusy(busy));
            return;
        }
        UsernameBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        LinkButton.IsEnabled = !busy;
        LinkButton.Content = busy ? "Подключаем…" : "Войти и привязать";
        CancelButton.Content = busy ? "Остановить" : "Отмена";
    }

    private void ShowMessage(string message, string title, MessageBoxImage icon)
    {
        if (Dispatcher.CheckAccess()) MessageBox.Show(this, message, title, MessageBoxButton.OK, icon);
        else Dispatcher.Invoke(() => MessageBox.Show(this, message, title, MessageBoxButton.OK, icon));
    }

    private static void WriteAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Не удалось определить папку для временного maFile.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string UniquePath(string directory, string stem)
    {
        var path = Path.Combine(directory, stem + ".maFile");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{stem}-{i}.maFile");
        return path;
    }

    private static string SafeFileName(string name)
    {
        var clean = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(clean) ? "steam-account" : clean;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null)
        {
            _cancellation.Cancel();
            UpdateProgress("Запрашиваем остановку…");
            return;
        }
        DialogResult = false;
    }

    private sealed class SteamPromptAuthenticator(LinkSteamWindow owner) : IAuthenticator
    {
        public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) => owner.PromptAuthCodeAsync(
            previousCodeWasIncorrect ? "Неверный Steam Guard-код" : "Steam Guard-код",
            "Введи текущий код из мобильного аутентификатора, подключённого к аккаунту.");
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) => owner.PromptAuthCodeAsync(
            previousCodeWasIncorrect ? "Код из письма не подошёл" : "Подтверди вход в Steam",
            $"Введи Steam Guard-код из письма{(string.IsNullOrWhiteSpace(email) ? string.Empty : $" на {email}")}.");
    }
}
