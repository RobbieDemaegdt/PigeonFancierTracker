using FluentAssertions;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Http;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class SessionStateServiceTests
{
    [Fact]
    public void Expiring_a_session_clears_user_and_selected_fancier()
    {
        var service = new SessionStateService();

        service.SetState(SessionState.AuthenticatedReady);
        service.SetState(SessionState.SessionExpired);

        service.Current.State.Should().Be(SessionState.SessionExpired);
        service.Current.User.Should().BeNull();
        service.Current.SelectedFancier.Should().BeNull();
    }

    [Fact]
    public void Authenticated_without_a_fancier_clears_previous_selection()
    {
        var service = new SessionStateService();

        service.SetState(SessionState.AuthenticatedReady, selectedFancier: new(42, "Test fancier", null, null, null, null, null, null));
        service.SetState(SessionState.AuthenticatedNoFancier);

        service.Current.State.Should().Be(SessionState.AuthenticatedNoFancier);
        service.Current.SelectedFancier.Should().BeNull();
    }
}