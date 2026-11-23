using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Soneca;

internal static class HouseCredential
{
    private static readonly byte[] Entropy = "Artho AI / Minha casa"u8.ToArray();

    private static string Location(bool demo) => Path.Combine(Path.GetDirectoryName(HousePackage.PathOnDisk)!,
        demo ? "unlock-demo.dat" : "unlock.dat");

    internal static bool Exists(bool demo) => File.Exists(Location(demo));

    internal static string? Read(bool demo)
    {
        byte[]? plain = null;
        try
        {
            using var file = File.OpenRead(Location(demo));
            if (file.Length is <= 0 or > 4096) return null;
            var secret = new byte[(int)file.Length];
            file.ReadExactly(secret);
            plain = ProtectedData.Unprotect(secret, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    internal static void Save(string password, bool demo)
    {
        var path = Location(demo);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        var plain = Encoding.UTF8.GetBytes(password);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
