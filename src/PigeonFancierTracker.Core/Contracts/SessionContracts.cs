using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Contracts;

public sealed record SessionSnapshot(
    SessionState State,
    UserDto? User,
    SelectedFancierDto? SelectedFancier,
    DateTimeOffset LastCheckedAtUtc,
    string? ErrorMessage);

public interface ISessionStateService
{
    SessionSnapshot Current { get; }

    event EventHandler<SessionSnapshot>? Changed;

    void SetState(
        SessionState state,
        UserDto? user = null,
        SelectedFancierDto? selectedFancier = null,
        string? errorMessage = null);
}