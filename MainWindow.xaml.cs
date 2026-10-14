using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SdaDesk;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Account> _accounts = [];
    private readonly ICollectionView _accountView;
    private readonly string _dataDirectory = Path.Combine(AppContext.BaseDirectory, "maFiles");
    private readonly string _pendingDirectory = Path.Combine(AppContext.BaseDirectory, "pending");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Account? _selected;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MainWindow()
    {
        InitializeComponent();
        _accountView = CollectionViewSource.GetDefaultView(_accounts);
        AccountList.ItemsSource = _accountView;
        _timer.Tick += (_, _) => RefreshCodes();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeWindowButton.Content = maximized ? "❐" : "□";
        MaximizeWindowButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DataPathText.Text = _dataDirectory;
        Directory.CreateDirectory(_dataDirectory);
        var unreadable = 0;
        foreach (var path in Directory.EnumerateFiles(_dataDirectory, "*.maFile"))
        {
            try { AddFromPath(path); }
            catch { unreadable++; }
        }

        var pendingCount = Directory.Exists(_pendingDirectory)
            ? Directory.EnumerateFiles(_pendingDirectory, "*.maFile").Count()
            : 0;
        if (pendingCount > 0)
        {
            PendingStatusText.Text = $"Незавершённых привязок: {pendingCount}. Проверь статус Steam Guard, прежде чем повторять настройку или удалять временный файл.";
            PendingStatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Проверьте статус незавершённой привязки";
        }
        else if (unreadable > 0)
        {
            StatusText.Text = $"Не удалось прочитать maFile: {unreadable}";
        }

        AccountList.SelectedIndex = _accounts.Count > 0 ? 0 : -1;
        UpdateView();
        _timer.Start();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Импортировать Steam maFile", Filter = "Steam maFile (*.maFile;*.json)|*.maFile;*.json|Все файлы (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;

        var imported = 0;
        var duplicates = 0;
        var failures = new List<string>();
        foreach (var source in dialog.FileNames)
        {
            try
            {
                var data = ParseMaFile(File.ReadAllText(source));
                var duplicate = FindDuplicate(data, Path.GetFileNameWithoutExtension(source));
                if (duplicate is not null)
                {
                    duplicates++;
                    continue;
                }

                var target = UniqueFilePath(SafeFileName(data.AccountName ?? Path.GetFileNameWithoutExtension(source)));
                WriteAtomic(target, JsonSerializer.Serialize(data, JsonOptions));
                AddFromPath(target);
                imported++;
            }
            catch (Exception ex) { failures.Add($"{Path.GetFileName(source)}: {ex.Message}"); }
        }

        if (imported > 0)
        {
            SearchBox.Clear();
            AccountList.SelectedItem = _accounts[^1];
        }
        StatusText.Text = $"Импортировано: {imported}; уже были добавлены: {duplicates}; ошибок: {failures.Count}";
        UpdateView();
        if (failures.Count > 0)
            MessageBox.Show(this, "Некоторые файлы не удалось импортировать:\n\n" + string.Join("\n\n", failures), "Результат импорта", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateMaFileWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        try
        {
            var duplicate = FindDuplicate(dialog.Result, dialog.Result.AccountName);
            if (duplicate is not null)
            {
                MessageBox.Show(this, $"Аккаунт «{duplicate.Name}» уже есть в списке.", "Аккаунт уже добавлен", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Directory.CreateDirectory(_dataDirectory);
            var path = UniqueFilePath(SafeFileName(dialog.Result.AccountName!));
            WriteAtomic(path, JsonSerializer.Serialize(dialog.Result, JsonOptions));
            AddFromPath(path);
            SearchBox.Clear();
            AccountList.SelectedItem = _accounts[^1];
            StatusText.Text = $"Создан открытый maFile: {Path.GetFileName(path)}";
            UpdateView();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось создать файл", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void LinkSteam_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LinkSteamWindow(_dataDirectory) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.CreatedPath is null) return;
        try
        {
            AddFromPath(dialog.CreatedPath);
            SearchBox.Clear();
            AccountList.SelectedItem = _accounts.LastOrDefault(a => string.Equals(a.FilePath, dialog.CreatedPath, StringComparison.OrdinalIgnoreCase));
            StatusText.Text = "Steam Guard активирован и добавлен";
            UpdateView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Steam подтвердил привязку, но SDA Desk не смог открыть сохранённый файл:\n{dialog.CreatedPath}\n\n{ex.Message}", "Файл сохранён", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Confirmations_Click(object sender, RoutedEventArgs e)
    {
        if (_accounts.Count == 0)
        {
            MessageBox.Show(this, "Сначала добавь хотя бы один аккаунт Steam.", "Нужен аккаунт", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new ConfirmationsWindow(_accounts.ToArray()) { Owner = this };
        dialog.ShowDialog();
    }

    private void AddFromPath(string path)
    {
        var data = ParseMaFile(File.ReadAllText(path));
        if (_accounts.Any(a => string.Equals(a.FilePath, path, StringComparison.OrdinalIgnoreCase))) return;
        _accounts.Add(new Account { FilePath = path, Data = data });
    }

    private Account? FindDuplicate(MaFile data, string? fallbackName)
    {
        var steamId = data.Session?.SteamId ?? 0;
        var name = string.IsNullOrWhiteSpace(data.AccountName) ? fallbackName : data.AccountName;
        return _accounts.FirstOrDefault(account =>
            (steamId != 0 && account.Data.Session?.SteamId == steamId) ||
            (!string.IsNullOrWhiteSpace(name) && string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static MaFile ParseMaFile(string json)
    {
        var data = JsonSerializer.Deserialize<MaFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException("Файл maFile пустой или повреждён.");
        if (string.IsNullOrWhiteSpace(data.SharedSecret)) throw new FormatException("В файле отсутствует shared_secret.");
        _ = Convert.FromBase64String(data.SharedSecret);
        return data;
    }

    private string UniqueFilePath(string stem)
    {
        var candidate = Path.Combine(_dataDirectory, stem + ".maFile");
        for (var index = 2; File.Exists(candidate); index++) candidate = Path.Combine(_dataDirectory, $"{stem}-{index}.maFile");
        return candidate;
    }

    private static string SafeFileName(string name)
    {
        var clean = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(clean) ? "steam-account" : clean;
    }

    private static void WriteAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Не удалось определить папку maFiles.");
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

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = AccountList.SelectedItem as Account;
        UpdateView();
        RefreshCodes();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var query = SearchBox.Text.Trim();
        SearchClearButton.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _accountView.Filter = item => item is Account account && account.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        _accountView.Refresh();
        if (_selected is not null && !AccountList.Items.Contains(_selected))
            AccountList.SelectedIndex = AccountList.Items.Count > 0 ? 0 : -1;
        UpdateView();
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void UpdateView()
    {
        AccountCountText.Text = _accounts.Count.ToString();
        EmptyState.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoSearchResults.Visibility = _accounts.Count > 0 && AccountList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CodeCard.Visibility = _selected is null || !AccountList.Items.Contains(_selected) ? Visibility.Collapsed : Visibility.Visible;
        if (_selected is not null && AccountList.Items.Contains(_selected))
        {
            SelectedName.Text = _selected.Name;
            SelectedInitial.Text = _selected.Initial;
            SelectedDescription.Text = CanConfirm(_selected.Data)
                ? "Steam Guard · код и подтверждения доступны"
                : "Steam Guard · генерация кодов доступна";
        }
        else if (_accounts.Count > 0 && AccountList.Items.Count > 0) StatusText.Text = "Выберите аккаунт в списке";
    }

    private static bool CanConfirm(MaFile data) =>
        !string.IsNullOrWhiteSpace(data.IdentitySecret) &&
        !string.IsNullOrWhiteSpace(data.DeviceId) &&
        data.Session is { SteamId: not 0 } session &&
        !string.IsNullOrWhiteSpace(session.AccessToken);

    private void RefreshCodes()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var remaining = 30 - (int)(now % 30);
        CountdownText.Text = $"{remaining} сек";
        CountdownBar.Value = remaining;
        foreach (var account in _accounts)
        {
            account.Remaining = remaining;
            try { account.Code = SteamCode.Generate(account.Data.SharedSecret!, now); }
            catch { account.Code = "ERROR"; }
        }
        if (_selected is null || !AccountList.Items.Contains(_selected)) return;
        CodeText.Text = _selected.Code;
        AccountStatus.Text = _selected.Code == "ERROR" ? "Не удалось прочитать shared_secret" : "Код обновляется автоматически";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Code is "ERROR" or "·····") return;
        try
        {
            Clipboard.SetText(_selected.Code);
            StatusText.Text = "Код скопирован в буфер обмена";
        }
        catch (Exception ex) { MessageBox.Show(this, $"Не удалось скопировать код.\n\n{ex.Message}", "Буфер обмена недоступен", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new SaveFileDialog { Title = "Экспортировать открытый maFile", FileName = Path.GetFileName(_selected.FilePath), Filter = "Steam maFile (*.maFile)|*.maFile", AddExtension = true, DefaultExt = ".maFile" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.Copy(_selected.FilePath, dialog.FileName, true);
            StatusText.Text = "maFile экспортирован без шифрования";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var result = MessageBox.Show(this,
            $"Удалить локальный файл {Path.GetFileName(_selected.FilePath)}?\n\nЭто удалит копию из SDA Desk, но не отключит аутентификатор в Steam. Убедись, что у тебя есть резервная копия.",
            "Удалить локальную копию", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        var index = AccountList.SelectedIndex;
        try
        {
            File.Delete(_selected.FilePath);
            _accounts.Remove(_selected);
            _selected = null;
            AccountList.SelectedIndex = _accounts.Count == 0 ? -1 : Math.Min(index, _accounts.Count - 1);
            StatusText.Text = "Локальная копия удалена";
            UpdateView();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
