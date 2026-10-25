using SteamAuth;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace SdaDesk;

public partial class ConfirmationsWindow : Window
{
    private readonly ObservableCollection<AccountChoice> _accounts;
    private readonly ObservableCollection<ConfirmationItem> _items = [];
    private readonly Dictionary<Account, SteamGuardAccount> _steamAccounts = [];
    private bool _busy;
    private bool _changingAccountSelection;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const int MarketPurchaseConfirmationType = 12;

    public ConfirmationsWindow(IEnumerable<Account> accounts)
    {
        InitializeComponent();
        _accounts = new ObservableCollection<AccountChoice>(accounts.Select(a => new AccountChoice(a)));
        AccountChoicesList.ItemsSource = _accounts;
        ConfirmationList.ItemsSource = _items;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        var selectedAccounts = _accounts.Where(a => a.IsSelected).Select(a => a.Account).ToArray();
        if (selectedAccounts.Length == 0)
        {
            StatusText.Text = "Выбери хотя бы один аккаунт.";
            return;
        }

        _busy = true;
        RefreshButton.IsEnabled = false;
        AccountChoicesList.IsEnabled = false;
        StatusText.Text = $"Проверяем Steam для аккаунтов: {selectedAccounts.Length}…";
        _items.Clear();
        _steamAccounts.Clear();
        var errors = new List<string>();
        var ignoredCount = 0;
        try
        {
            foreach (var account in selectedAccounts)
            {
                try
                {
                    StatusText.Text = $"Загружаем подтверждения: {account.Name}…";
                    var steamAccount = CreateSteamAccount(account);
                    if (steamAccount.Session!.IsAccessTokenExpired())
                    {
                        await steamAccount.Session.RefreshAccessToken();
                        SaveSession(account, steamAccount);
                    }
                    var confirmations = await steamAccount.FetchConfirmationsAsync();
                    _steamAccounts[account] = steamAccount;
                    ignoredCount += confirmations.Count(c => !IsActionableType(c.ConfType));
                    foreach (var confirmation in confirmations)
                        _items.Add(new ConfirmationItem(account, steamAccount, confirmation));
                }
                catch (Exception ex) { errors.Add($"{account.Name}: {ex.Message}"); }
            }

            EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfirmationList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            var actionableCount = _items.Count(i => i.IsActionable);
            var suffix = ignoredCount > 0 ? $"; только просмотр: {ignoredCount}" : string.Empty;
            StatusText.Text = $"Найдено: {_items.Count}; можно обработать: {actionableCount}{suffix}";
            if (errors.Count > 0)
            {
                StatusText.Text += $"; аккаунтов с ошибкой: {errors.Count}";
                MessageBox.Show(this, "Не удалось загрузить подтверждения для некоторых аккаунтов:\n\n" + string.Join("\n\n", errors), "Ошибки аккаунтов", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            AccountChoicesList.IsEnabled = true;
            UpdateSelectionControls();
        }
    }

    private static bool IsActionableType(Confirmation.EMobileConfirmationType type) =>
        type is Confirmation.EMobileConfirmationType.Trade
            or Confirmation.EMobileConfirmationType.MarketListing
            || (int)type == MarketPurchaseConfirmationType;

    private static SteamGuardAccount CreateSteamAccount(Account account)
    {
        var data = account.Data;
        if (string.IsNullOrWhiteSpace(data.IdentitySecret))
            throw new InvalidOperationException("В maFile нет identity_secret.");
        if (string.IsNullOrWhiteSpace(data.DeviceId))
            throw new InvalidOperationException("В maFile нет device_id.");
        if (data.Session is null || data.Session.SteamId == 0 || string.IsNullOrWhiteSpace(data.Session.AccessToken))
            throw new InvalidOperationException("В maFile нет сессии Steam. Переподключи аккаунт через SDA Desk или импортируй полный maFile из SDA.");
        return new SteamGuardAccount
        {
            AccountName = data.AccountName,
            SharedSecret = data.SharedSecret,
            IdentitySecret = data.IdentitySecret,
            RevocationCode = data.RevocationCode,
            SerialNumber = data.SerialNumber,
            TokenGID = data.TokenGid,
            Secret1 = data.Secret1,
            DeviceID = data.DeviceId,
            ServerTime = data.ServerTime ?? 0,
            Status = data.Status ?? 0,
            FullyEnrolled = data.FullyEnrolled ?? true,
            Session = CreateSession(data.Session),
        };
    }

    private static SteamAuth.SessionData CreateSession(MaFileSession session) => new()
    {
        SteamID = session.SteamId,
        AccessToken = session.AccessToken,
        RefreshToken = session.RefreshToken,
        SessionID = session.SessionId,
    };

    private static void SaveSession(Account account, SteamGuardAccount steamAccount)
    {
        if (steamAccount.Session is null) return;
        account.Data.Session = new MaFileSession
        {
            SteamId = steamAccount.Session.SteamID,
            AccessToken = steamAccount.Session.AccessToken,
            RefreshToken = steamAccount.Session.RefreshToken,
            SessionId = steamAccount.Session.SessionID,
        };
        WriteAtomic(account.FilePath, JsonSerializer.Serialize(account.Data, JsonOptions));
    }

    private static void WriteAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Не удалось определить папку maFiles.");
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

    private void AllAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = _accounts.Any(a => !a.IsSelected);
        _changingAccountSelection = true;
        foreach (var choice in _accounts) choice.IsSelected = selected;
        _changingAccountSelection = false;
        InvalidateLoadedConfirmations();
    }

    private void AccountChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_busy || _changingAccountSelection) return;
        var selectedCount = _accounts.Count(a => a.IsSelected);
        AllAccountsCheckBox.IsChecked = selectedCount == _accounts.Count ? true : selectedCount == 0 ? false : null;
        InvalidateLoadedConfirmations();
    }

    private void InvalidateLoadedConfirmations()
    {
        _items.Clear();
        _steamAccounts.Clear();
        EmptyState.Visibility = Visibility.Visible;
        ConfirmationList.Visibility = Visibility.Collapsed;
        StatusText.Text = "Обнови список для выбранных аккаунтов.";
        UpdateSelectionControls();
    }

    private void AllConfirmations_Click(object sender, RoutedEventArgs e)
    {
        var actionable = _items.Where(i => i.IsActionable).ToArray();
        var selected = actionable.Any(i => !i.IsSelected);
        foreach (var item in actionable) item.IsSelected = selected;
        UpdateSelectionControls();
    }

    private void ConfirmationChoice_Changed(object sender, RoutedEventArgs e) => UpdateSelectionControls();

    private void UpdateSelectionControls()
    {
        var actionableCount = _items.Count(i => i.IsActionable);
        var selectedCount = _items.Count(i => i.IsSelected && i.IsActionable);
        SelectedCountText.Text = $"Выбрано: {selectedCount} из {actionableCount}";
        AllConfirmationsCheckBox.IsChecked = selectedCount == 0 ? false : selectedCount == actionableCount ? true : null;
        AccountChoicesList.IsEnabled = !_busy;
        AllConfirmationsCheckBox.IsEnabled = !_busy && actionableCount > 0;
        ConfirmSelectedButton.IsEnabled = !_busy && selectedCount > 0;
        DenySelectedButton.IsEnabled = !_busy && selectedCount > 0;
        ConfirmSelectedButton.Content = _busy ? "Обрабатываем…" : "Подтвердить выбранные";
    }

    private async void ConfirmSelected_Click(object sender, RoutedEventArgs e) => await ProcessSelectedAsync(accept: true);
    private async void DenySelected_Click(object sender, RoutedEventArgs e) => await ProcessSelectedAsync(accept: false);

    private async Task ProcessSelectedAsync(bool accept)
    {
        if (_busy) return;
        var chosen = _items.Where(i => i.IsSelected).ToArray();
        if (chosen.Length == 0) return;

        var previewLines = chosen.Take(6).Select(i => $"• {i.AccountName} — {i.TypeName}: {i.Headline}").ToList();
        if (chosen.Length > previewLines.Count) previewLines.Add($"…и ещё {chosen.Length - previewLines.Count}");
        var accountCount = chosen.Select(i => i.Account).Distinct().Count();
        var action = accept ? "ПОДТВЕРДИТЬ" : "ОТКЛОНИТЬ";
        var decision = MessageBox.Show(this,
            $"{action} {chosen.Length} пункт(а) для {accountCount} аккаунт(а)?\n\n{string.Join("\n", previewLines)}\n\nПроверь полный список перед продолжением.",
            accept ? "Подтверждение для нескольких аккаунтов" : "Отклонение для нескольких аккаунтов",
            MessageBoxButton.YesNo, accept ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes) return;

        _busy = true;
        UpdateSelectionControls();
        RefreshButton.IsEnabled = false;
        AccountChoicesList.IsEnabled = false;
        var failures = new List<string>();
        var completed = 0;
        try
        {
            foreach (var group in chosen.GroupBy(i => i.Account))
            {
                try
                {
                    if (!_steamAccounts.TryGetValue(group.Key, out var steamAccount))
                        throw new InvalidOperationException("Сессия аккаунта не загружена; обнови список.");
                    var confirmations = group.Select(i => i.Raw).ToArray();
                    var success = accept
                        ? await steamAccount.AcceptMultipleConfirmations(confirmations)
                        : await steamAccount.DenyMultipleConfirmations(confirmations);
                    if (!success) throw new InvalidOperationException("Steam не подтвердил пакетное действие.");
                    completed += confirmations.Length;
                }
                catch (Exception ex) { failures.Add($"{group.Key.Name}: {ex.Message}"); }
            }
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            AccountChoicesList.IsEnabled = true;
            UpdateSelectionControls();
        }

        StatusText.Text = $"Обработано: {completed} из {chosen.Length}.";
        if (failures.Count > 0)
            MessageBox.Show(this, $"Часть действий не завершилась.\n\n{string.Join("\n\n", failures)}", "Результат обработки", MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshAsync();
    }

    private sealed class AccountChoice : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public Account Account { get; }
        public string Name => Account.Name;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public AccountChoice(Account account) => Account = account;
    }

    private sealed class ConfirmationItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public Account Account { get; }
        public SteamGuardAccount SteamAccount { get; }
        public Confirmation Raw { get; }
        public string AccountName => Account.Name;
        public string TypeName { get; }
        public string Headline { get; }
        public string SummaryText { get; }
        public string ActionabilityText => IsActionable ? string.Empty : "Только просмотр · подтверждать нельзя";
        public bool IsActionable => IsActionableType(Raw.ConfType);
        public bool IsSelected { get => _isSelected; set { _isSelected = value && IsActionable; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;

        public ConfirmationItem(Account account, SteamGuardAccount steamAccount, Confirmation raw)
        {
            Account = account;
            SteamAccount = steamAccount;
            Raw = raw;
            TypeName = (int)raw.ConfType == MarketPurchaseConfirmationType
                ? "ПОКУПКА"
                : raw.ConfType switch
                {
                    Confirmation.EMobileConfirmationType.Trade => "ОБМЕН",
                    Confirmation.EMobileConfirmationType.MarketListing => "ПРОДАЖА",
                    Confirmation.EMobileConfirmationType.Test => "ПРОВЕРКА",
                    Confirmation.EMobileConfirmationType.FeatureOptOut => "ИЗМЕНЕНИЕ ЗАЩИТЫ",
                    Confirmation.EMobileConfirmationType.PhoneNumberChange => "СМЕНА ТЕЛЕФОНА",
                    Confirmation.EMobileConfirmationType.AccountRecovery => "ВОССТАНОВЛЕНИЕ АККАУНТА",
                    _ => $"ТИП #{(int)raw.ConfType}",
                };
            _isSelected = IsActionable;
            Headline = string.IsNullOrWhiteSpace(raw.Headline) ? "Действие Steam" : raw.Headline;
            SummaryText = raw.Summary is null ? string.Empty : string.Join(Environment.NewLine, raw.Summary);
        }
    }
}
