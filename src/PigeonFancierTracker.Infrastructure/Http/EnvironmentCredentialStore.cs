using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class EnvironmentCredentialStore : ICredentialStore
{
    public Task SaveAsync(string email, string password) => Task.CompletedTask;

    public Task<(string Email, string Password)?> TryLoadAsync()
    {
        var email = Environment.GetEnvironmentVariable("PF_EMAIL");
        var password = Environment.GetEnvironmentVariable("PF_PASSWORD");

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<(string, string)?>((email, password));
        }

        return Task.FromResult<(string, string)?>(null);
    }

    public Task ClearAsync() => Task.CompletedTask;
}
