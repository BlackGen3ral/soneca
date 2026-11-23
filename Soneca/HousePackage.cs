using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Soneca;

internal static class HousePackage
{
    private static ReadOnlySpan<byte> Magic => "ARTHOC01"u8;
    private const int HeaderSize = 36;
    private const int TagSize = 16;
    private const int MaxSize = 1_500_000;
    internal static string PathOnDisk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Soneca", "House", "MinhaCasa.arthocasa");

    internal static byte[] Read()
    {
        using var stream = new FileStream(PathOnDisk, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length is <= HeaderSize + TagSize or > MaxSize) throw new InvalidDataException();
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        if (!data.AsSpan(0, 8).SequenceEqual(Magic)) throw new InvalidDataException();
        return data;
    }

    internal static string Unlock(string password)
    {
        byte[] bytes = Read();
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, bytes.AsSpan(8, 16), 600_000, HashAlgorithmName.SHA256, 32);
        var html = new byte[bytes.Length - HeaderSize - TagSize];
        try
        {
            using var cipher = new AesGcm(key, TagSize);
            cipher.Decrypt(bytes.AsSpan(24, 12), bytes.AsSpan(HeaderSize, html.Length),
                bytes.AsSpan(^TagSize), html, bytes.AsSpan(0, HeaderSize));
            return Encoding.UTF8.GetString(html);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(html);
        }
    }
}
