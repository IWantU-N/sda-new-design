using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SdaDesk;

public sealed class MaFile
{
    [JsonPropertyName("account_name")] public string? AccountName { get; set; }
    [JsonPropertyName("shared_secret")] public string? SharedSecret { get; set; }
    [JsonPropertyName("identity_secret")] public string? IdentitySecret { get; set; }
    [JsonPropertyName("revocation_code")] public string? RevocationCode { get; set; }
    [JsonPropertyName("serial_number")] public string? SerialNumber { get; set; }
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("token_gid")] public string? TokenGid { get; set; }
    [JsonPropertyName("secret_1")] public string? Secret1 { get; set; }
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("server_time")] public long? ServerTime { get; set; }
    [JsonPropertyName("status")] public int? Status { get; set; }
    [JsonPropertyName("fully_enrolled")] public bool? FullyEnrolled { get; set; }
    [JsonPropertyName("phone_number_hint")] public string? PhoneNumberHint { get; set; }
    [JsonPropertyName("confirm_type")] public int? ConfirmType { get; set; }
    [JsonPropertyName("Session")] public MaFileSession? Session { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class MaFileSession
{
    [JsonPropertyName("SteamID")] public ulong SteamId { get; set; }
    [JsonPropertyName("AccessToken")] public string? AccessToken { get; set; }
    [JsonPropertyName("RefreshToken")] public string? RefreshToken { get; set; }
    [JsonPropertyName("SessionID")] public string? SessionId { get; set; }
}

public sealed class Account : INotifyPropertyChanged
{
    private string _code = "·····";
    private int _remaining;
    public required string FilePath { get; init; }
    public required MaFile Data { get; init; }
    public string Name => string.IsNullOrWhiteSpace(Data.AccountName) ? Path.GetFileNameWithoutExtension(FilePath) : Data.AccountName!;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "S" : Name[..1].ToUpperInvariant();
    public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }
    public int Remaining { get => _remaining; set { _remaining = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
