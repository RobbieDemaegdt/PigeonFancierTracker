using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App;

internal sealed record SessionStatusPresentation(
    string Title,
    string Description,
    string ActionText,
    string Severity,
    int Step)
{
    public static SessionStatusPresentation From(SessionSnapshot snapshot)
    {
        return snapshot.State switch
        {
            SessionState.AuthenticatedReady => new(
                "Klaar om te synchroniseren",
                $"De tracker kan alleen-lezen data verzamelen voor {snapshot.SelectedFancier?.DisplayName ?? "de geselecteerde melker"}.",
                "Nu synchroniseren",
                "Success",
                3),
            SessionState.AuthenticatedNoFancier => new(
                "Selecteer een melker",
                "Je bent aangemeld. Selecteer het melkeraccount dat je wilt volgen.",
                "Selecteer een melker",
                "Warning",
                2),
            SessionState.LoginPageOpen => new(
                "Aanmelden vereist",
                "Voer je e-mailadres en wachtwoord in om aan te melden.",
                "Aanmelden",
                "Warning",
                1),
            SessionState.SessionExpired => new(
                "Sessie verlopen",
                "Je opgeslagen sessie is verlopen. Je lokaal verzamelde geschiedenis is veilig.",
                "Opnieuw aanmelden",
                "Danger",
                1),
            SessionState.TransportUnavailable => new(
                "Verbinding niet beschikbaar",
                "De Pigeon Fancier API kon niet worden bereikt. Controleer je netwerkverbinding en probeer het opnieuw.",
                "Opnieuw proberen",
                "Danger",
                1),
            _ => new(
                "Verbind je account",
                "Voer je e-mailadres en wachtwoord in om te beginnen.",
                "Aanmelden",
                "Neutral",
                1),
        };
    }
}
