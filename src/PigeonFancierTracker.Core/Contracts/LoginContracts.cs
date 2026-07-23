namespace PigeonFancierTracker.Core.Contracts;

public sealed record LoginResult(bool Success, string? ErrorMessage = null);

public interface ILoginService
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<LoginResult> SelectFancierAsync(int fancierId, CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    Task SaveAsync(string email, string password);
    Task<(string Email, string Password)?> TryLoadAsync();
    Task ClearAsync();
}
