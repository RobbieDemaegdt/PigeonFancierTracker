using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Infrastructure.Http;

public sealed class SessionStateService : ISessionStateService
{
    private SessionSnapshot current = new(
        SessionState.LoggedOut,
        null,
        null,
        DateTimeOffset.UtcNow,
        null);

    public SessionSnapshot Current => current;

    public event EventHandler<SessionSnapshot>? Changed;

    public void SetState(
        SessionState state,
        UserDto? user = null,
        SelectedFancierDto? selectedFancier = null,
        string? errorMessage = null)
    {
        var clearSession = state is SessionState.LoggedOut or SessionState.SessionExpired;
        var clearFancier = clearSession || state is SessionState.AuthenticatedNoFancier;
        var next = new SessionSnapshot(
            state,
            clearSession ? null : user ?? current.User,
            clearFancier ? null : selectedFancier ?? current.SelectedFancier,
            DateTimeOffset.UtcNow,
            errorMessage);

        var changed = current.State != next.State
            || current.User != next.User
            || current.SelectedFancier != next.SelectedFancier
            || current.ErrorMessage != next.ErrorMessage;
        current = next;

        if (changed)
        {
            Changed?.Invoke(this, current);
        }
    }
}
