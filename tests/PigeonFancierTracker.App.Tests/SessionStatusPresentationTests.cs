using FluentAssertions;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App.Tests;

public sealed class SessionStatusPresentationTests
{
    [Fact]
    public void AuthenticatedReady_with_fancier_shows_fancier_name()
    {
        var fancier = new SelectedFancierDto(42, "Pieter", null, null, null, null, null, null);
        var snapshot = CreateSnapshot(SessionState.AuthenticatedReady, fancier);

        var result = SessionStatusPresentation.From(snapshot);

        result.Title.Should().Be("Klaar om te synchroniseren");
        result.Severity.Should().Be("Success");
        result.Step.Should().Be(3);
        result.Description.Should().Contain("Pieter");
    }

    [Fact]
    public void AuthenticatedReady_without_fancier_uses_fallback_description()
    {
        var snapshot = CreateSnapshot(SessionState.AuthenticatedReady);

        var result = SessionStatusPresentation.From(snapshot);

        result.Description.Should().Contain("de geselecteerde melker");
    }

    [Fact]
    public void AuthenticatedNoFancier_shows_select_prompt()
    {
        var snapshot = CreateSnapshot(SessionState.AuthenticatedNoFancier);

        var result = SessionStatusPresentation.From(snapshot);

        result.Title.Should().Be("Selecteer een melker");
        result.Severity.Should().Be("Warning");
        result.Step.Should().Be(2);
    }

    [Fact]
    public void LoginPageOpen_shows_login_required()
    {
        var snapshot = CreateSnapshot(SessionState.LoginPageOpen);

        var result = SessionStatusPresentation.From(snapshot);

        result.Title.Should().Be("Aanmelden vereist");
        result.Severity.Should().Be("Warning");
        result.Step.Should().Be(1);
    }

    [Fact]
    public void SessionExpired_shows_danger()
    {
        var snapshot = CreateSnapshot(SessionState.SessionExpired);

        var result = SessionStatusPresentation.From(snapshot);

        result.Title.Should().Be("Sessie verlopen");
        result.Severity.Should().Be("Danger");
        result.Step.Should().Be(1);
    }

    [Fact]
    public void TransportUnavailable_shows_danger()
    {
        var snapshot = CreateSnapshot(SessionState.TransportUnavailable);

        var result = SessionStatusPresentation.From(snapshot);

        result.Title.Should().Be("Verbinding niet beschikbaar");
        result.Severity.Should().Be("Danger");
        result.Step.Should().Be(1);
    }

    [Fact]
    public void LoggedOut_shows_neutral_connect_prompt()
    {
        var snapshot = CreateSnapshot(SessionState.LoggedOut);

        var result = SessionStatusPresentation.From(snapshot);

        result.Title.Should().Be("Verbind je account");
        result.Severity.Should().Be("Neutral");
        result.Step.Should().Be(1);
    }

    private static SessionSnapshot CreateSnapshot(
        SessionState state,
        SelectedFancierDto? fancier = null) =>
        new(state, null, fancier, DateTimeOffset.UtcNow, null);
}
