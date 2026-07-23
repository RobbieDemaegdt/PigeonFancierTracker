namespace PigeonFancierTracker.Core.Domain;

public enum SessionState
{
    LoggedOut,
    LoginPageOpen,
    AuthenticatedNoFancier,
    AuthenticatedReady,
    SessionExpired,
    TransportUnavailable,
}