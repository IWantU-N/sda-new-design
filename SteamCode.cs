using System.Security.Cryptography;
using System.Text;

namespace SdaDesk;

internal static class SteamCode
{
    private const string Alphabet = "23456789BCDFGHJKMNPQRTVWXY";

    public static string Generate(string secret, long unixTime)
    {
        byte[] key;
        try { key = Convert.FromBase64String(secret); }
        catch (FormatException) { throw new FormatException("shared_secret должен быть корректной строкой Base64."); }
        var counter = BitConverter.GetBytes(unixTime / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var output = new StringBuilder(5);
        for (var i = 0; i < 5; i++) { output.Append(Alphabet[value % Alphabet.Length]); value /= Alphabet.Length; }
        return output.ToString();
    }
}
