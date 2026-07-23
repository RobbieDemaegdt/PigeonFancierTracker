using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Http;

[SupportedOSPlatform("windows")]
public sealed class CredentialStore(string appDataDirectory) : ICredentialStore
{
    private readonly string filePath = Path.Combine(appDataDirectory, "credentials.dat");

    public Task SaveAsync(string email, string password)
    {
        var json = JsonSerializer.Serialize(new StoredCredentials(email, password));
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, encrypted);
        return Task.CompletedTask;
    }

    public Task<(string Email, string Password)?> TryLoadAsync()
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult<(string, string)?>(null);
        }

        try
        {
            var encrypted = File.ReadAllBytes(filePath);
            var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            var credentials = JsonSerializer.Deserialize<StoredCredentials>(json);
            if (credentials is { Email.Length: > 0, Password.Length: > 0 })
            {
                return Task.FromResult<(string, string)?>((credentials.Email, credentials.Password));
            }
        }
        catch (CryptographicException)
        {
            // Data was protected by a different user or is corrupt — discard.
            TryDeleteFile();
        }
        catch (JsonException)
        {
            TryDeleteFile();
        }

        return Task.FromResult<(string, string)?>(null);
    }

    public Task ClearAsync()
    {
        TryDeleteFile();
        return Task.CompletedTask;
    }

    private void TryDeleteFile()
    {
        try { File.Delete(filePath); } catch { }
    }

    private sealed record StoredCredentials(string Email, string Password);
}
