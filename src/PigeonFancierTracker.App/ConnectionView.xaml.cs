using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.App;

public partial class ConnectionView : UserControl
{
    private readonly ILoginService loginService;
    private readonly ICredentialStore credentialStore;
    private readonly ISessionStateService sessionState;
    private readonly ISyncCoordinator syncCoordinator;
    private readonly AuthenticatedReadTransportProxy transportProxy;
    private readonly HttpClientReadTransport readTransport;
    private readonly PigeonFancierApiClient apiClient;
    private readonly CookieContainer cookieContainer;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public event EventHandler<SyncRunResult>? SyncCompleted;

    public ConnectionView(
        ILoginService loginService,
        ICredentialStore credentialStore,
        ISessionStateService sessionState,
        ISyncCoordinator syncCoordinator,
        AuthenticatedReadTransportProxy transportProxy,
        HttpClientReadTransport readTransport,
        PigeonFancierApiClient apiClient,
        CookieContainer cookieContainer)
    {
        InitializeComponent();
        this.loginService = loginService;
        this.credentialStore = credentialStore;
        this.sessionState = sessionState;
        this.syncCoordinator = syncCoordinator;
        this.transportProxy = transportProxy;
        this.readTransport = readTransport;
        this.apiClient = apiClient;
        this.cookieContainer = cookieContainer;
        Loaded += ConnectionView_Loaded;
        sessionState.Changed += SessionState_Changed;
        syncCoordinator.ProgressChanged += SyncCoordinator_ProgressChanged;
        UpdatePresentation(sessionState.Current);
        UpdateSyncControls();
    }

    public async Task TryRestoreSessionAsync()
    {
        var credentials = await credentialStore.TryLoadAsync();
        if (credentials is not var (email, password))
        {
            return;
        }

        transportProxy.SetTransport(readTransport);
        var result = await loginService.LoginAsync(email, password);
        if (!result.Success)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            EmailTextBox.Text = email;
            RememberCredentialsCheckBox.IsChecked = true;
        });

        await RefreshSessionAsync();
    }

    private async void ConnectionView_Loaded(object sender, RoutedEventArgs e)
    {
        transportProxy.SetTransport(readTransport);

        if (sessionState.Current.State is SessionState.AuthenticatedReady or SessionState.AuthenticatedNoFancier)
        {
            await RefreshSessionAsync();
            return;
        }

        var credentials = await credentialStore.TryLoadAsync();
        if (credentials is var (email, _))
        {
            EmailTextBox.Text = email;
            RememberCredentialsCheckBox.IsChecked = true;
        }
    }

    private void SessionState_Changed(object? sender, SessionSnapshot snapshot)
    {
        Dispatcher.Invoke(() => UpdatePresentation(snapshot));
    }

    private async Task RefreshSessionAsync()
    {
        if (!await refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var userResponse = await apiClient.GetJsonAsync("/api/user");
            if (userResponse.StatusCode == 401)
            {
                sessionState.SetState(SessionState.SessionExpired, errorMessage: "De Pigeon Fancier sessie is verlopen.");
                SetSessionText("Sessie verlopen. Meld je opnieuw aan.");
                SetTechnicalDetails("GET /api/user returned HTTP 401.");
                return;
            }

            if (!userResponse.IsSuccessStatusCode)
            {
                var state = userResponse.StatusCode == 0
                    ? SessionState.TransportUnavailable
                    : SessionState.LoginPageOpen;
                sessionState.SetState(state, errorMessage: $"GET /api/user returned HTTP {userResponse.StatusCode}.");
                SetSessionText(userResponse.StatusCode == 0
                    ? $"Kan de Pigeon Fancier API niet bereiken{FormatTransportError(userResponse)}."
                    : $"Wachten op aanmelding… GET /api/user returned HTTP {userResponse.StatusCode}{FormatTransportError(userResponse)}.");
                SetTechnicalDetails($"GET /api/user returned HTTP {userResponse.StatusCode}{FormatTransportError(userResponse)}.");
                return;
            }

            var user = Deserialize<UserDto>(userResponse.Body);

            var selectedResponse = await apiClient.GetJsonAsync("/api/fancier/selected");
            if (selectedResponse.StatusCode == 401)
            {
                sessionState.SetState(SessionState.SessionExpired, errorMessage: "De Pigeon Fancier sessie is verlopen.");
                SetSessionText("Sessie verlopen. Meld je opnieuw aan.");
                SetTechnicalDetails("GET /api/fancier/selected returned HTTP 401.");
                return;
            }

            if (selectedResponse.IsSuccessStatusCode)
            {
                var selectedFancier = Deserialize<SelectedFancierDto>(selectedResponse.Body);
                if (selectedFancier?.Id is not null)
                {
                    sessionState.SetState(SessionState.AuthenticatedReady, user, selectedFancier);
                    SetSessionText("Geauthenticeerd en klaar om te synchroniseren.");
                    SetFancierText($"Geselecteerd: {selectedFancier.DisplayName ?? selectedFancier.Id.ToString()} (ID {selectedFancier.Id})");
                    SetTechnicalDetails("De geauthenticeerde gebruiker en geselecteerde melker zijn bevestigd.");
                    return;
                }

                sessionState.SetState(SessionState.AuthenticatedNoFancier, user: user);
                SetSessionText("Geauthenticeerd. Selecteer een melker om verder te gaan.");
                SetFancierText("Geen melker geselecteerd");
                SetTechnicalDetails("Het antwoord van de geselecteerde melker bevatte geen ID.");
                await LoadFancierListAsync();
            }
            else
            {
                if (selectedResponse.StatusCode == 0)
                {
                    sessionState.SetState(SessionState.TransportUnavailable, user: user, errorMessage: "Het API-verzoek voor de geselecteerde melker kon niet worden voltooid.");
                }
                else
                {
                    sessionState.SetState(SessionState.AuthenticatedNoFancier, user: user, errorMessage: $"GET /api/fancier/selected returned HTTP {selectedResponse.StatusCode}.");
                    await LoadFancierListAsync();
                }

                SetSessionText($"Geauthenticeerd, maar GET /api/fancier/selected returned HTTP {selectedResponse.StatusCode}{FormatTransportError(selectedResponse)}.");
                SetTechnicalDetails($"GET /api/fancier/selected returned HTTP {selectedResponse.StatusCode}{FormatTransportError(selectedResponse)}.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            sessionState.SetState(SessionState.TransportUnavailable, errorMessage: exception.Message);
            SetSessionText($"Kon de geauthenticeerde sessie niet controleren: {exception.Message}");
            SetTechnicalDetails(exception.Message);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task LoadFancierListAsync()
    {
        try
        {
            var response = await apiClient.GetJsonAsync("/api/fancier");
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var fanciers = Deserialize<SelectedFancierDto[]>(response.Body);
            if (fanciers is { Length: > 0 })
            {
                Dispatcher.Invoke(() =>
                {
                    FancierComboBox.ItemsSource = fanciers;
                    FancierComboBox.SelectedIndex = 0;
                    FancierSelectorPanel.Visibility = Visibility.Visible;
                });
            }
        }
        catch
        {
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        await PerformLoginAsync();
    }

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformLoginAsync();
        }
    }

    private async Task PerformLoginAsync()
    {
        var email = EmailTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowLoginError("Vul je e-mailadres en wachtwoord in.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginErrorText.Visibility = Visibility.Collapsed;
        SetSessionText("Aanmelden…");

        transportProxy.SetTransport(readTransport);
        var result = await loginService.LoginAsync(email, password);

        if (!result.Success)
        {
            LoginButton.IsEnabled = true;
            ShowLoginError(result.ErrorMessage ?? "Aanmelding mislukt.");
            SetSessionText("Aanmelding mislukt.");
            SetTechnicalDetails(result.ErrorMessage ?? "Unknown error.");
            return;
        }

        if (RememberCredentialsCheckBox.IsChecked == true)
        {
            await credentialStore.SaveAsync(email, password);
        }
        else
        {
            await credentialStore.ClearAsync();
        }

        LoginButton.IsEnabled = true;
        SetSessionText("Aangemeld. Sessie controleren…");
        await RefreshSessionAsync();
    }

    private async void SelectFancier_Click(object sender, RoutedEventArgs e)
    {
        if (FancierComboBox.SelectedItem is not SelectedFancierDto fancier || fancier.Id is not int fancierId)
        {
            return;
        }

        SelectFancierButton.IsEnabled = false;
        SetSessionText($"Melker {fancier.DisplayName ?? fancierId.ToString()} selecteren…");

        try
        {
            var result = await loginService.SelectFancierAsync(fancierId);
            if (result.Success)
            {
                await RefreshSessionAsync();
            }
            else
            {
                SetSessionText(result.ErrorMessage ?? "Kan melker niet selecteren.");
                SetTechnicalDetails(result.ErrorMessage ?? "Fancier selection failed.");
            }
        }
        catch (Exception exception)
        {
            SetSessionText($"Kan melker niet selecteren: {exception.Message}");
            SetTechnicalDetails(exception.Message);
        }
        finally
        {
            SelectFancierButton.IsEnabled = true;
        }
    }

    public void StartSync()
    {
        RunSync_Click(this, new RoutedEventArgs());
    }

    private async void RunSync_Click(object sender, RoutedEventArgs e)
    {
        if (sessionState.Current.State != SessionState.AuthenticatedReady || syncCoordinator.IsRunning)
        {
            return;
        }

        SyncProgressBar.Value = 0;
        SyncProgressText.Text = "Snelle sync starten…";
        SyncDetailText.Text = "De tracker bereidt de alleen-lezen eindpuntenlijst voor.";
        SyncSummaryText.Text = string.Empty;
        UpdateSyncControls();

        try
        {
            var result = await syncCoordinator.SyncAsync(SyncProfile.Quick);
            var successful = result.EndpointResults.Count(x => x.IsSuccess);
            var failed = result.EndpointResults.Count - successful;
            SyncProgressBar.Value = SyncProgressBar.Maximum;
            SyncProgressText.Text = $"Snelle sync voltooid in {(result.CompletedAtUtc - result.StartedAtUtc).TotalSeconds:N1}s.";
            SyncDetailText.Text = $"{successful} eindpunt(en) geslaagd; {failed} eindpunt(en) vereisen aandacht.";
            SyncSummaryText.Text = failed == 0
                ? "Alle gevraagde data is lokaal vastgelegd."
                : "Geslaagde antwoorden zijn bewaard. Open de verbindingspagina als de sessie verlopen is en probeer het later opnieuw voor de mislukte eindpunten.";
            SyncCompleted?.Invoke(this, result);
        }
        catch (InvalidOperationException exception)
        {
            SyncProgressText.Text = "De sync kon niet worden gestart.";
            SyncDetailText.Text = exception.Message;
        }
        catch (Exception)
        {
            SyncProgressText.Text = "De sync kon niet worden voltooid.";
            SyncDetailText.Text = "Controleer de verbindingspagina en probeer het opnieuw. Lokale data die al verzameld is, werd niet verwijderd.";
        }
        finally
        {
            UpdateSyncControls();
        }
    }

    private void CancelSync_Click(object sender, RoutedEventArgs e)
    {
        syncCoordinator.Cancel();
        SyncProgressText.Text = "Sync annuleren…";
        SyncDetailText.Text = "Voltooide eindpuntresultaten blijven lokaal beschikbaar.";
    }

    private void SyncCoordinator_ProgressChanged(object? sender, SyncProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            SyncProgressBar.Maximum = Math.Max(progress.TotalEndpoints, 1);
            SyncProgressBar.Value = progress.CompletedEndpoints;
            SyncProgressText.Text = $"Synchroniseren {progress.CompletedEndpoints} van {progress.TotalEndpoints} eindpunten";
            SyncDetailText.Text = progress.IsSuccess
                ? $"{FormatEndpoint(progress.Endpoint)} gelezen."
                : $"{FormatEndpoint(progress.Endpoint)} vereist aandacht: {progress.ErrorMessage ?? $"HTTP {progress.StatusCode}"}.";
            UpdateSyncControls();
        });
    }

    private void UpdateSyncControls()
    {
        var ready = sessionState.Current.State == SessionState.AuthenticatedReady;
        RunSyncButton.IsEnabled = ready && !syncCoordinator.IsRunning;
        CancelSyncButton.IsEnabled = syncCoordinator.IsRunning;
    }

    private static string FormatEndpoint(string endpoint)
    {
        var value = endpoint.Replace("/api/", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('/');
        return string.IsNullOrWhiteSpace(value) ? "the endpoint" : value;
    }

    private void PrimaryConnectionAction_Click(object sender, RoutedEventArgs e)
    {
        switch (sessionState.Current.State)
        {
            case SessionState.AuthenticatedNoFancier:
                FancierSelectorPanel.Visibility = Visibility.Visible;
                break;
            case SessionState.AuthenticatedReady:
                StartSync();
                break;
            case SessionState.TransportUnavailable:
                _ = RefreshSessionAsync();
                break;
            default:
                EmailTextBox.Focus();
                break;
        }
    }

    private void CheckConnection_Click(object sender, RoutedEventArgs e)
    {
        SetSessionText("Sessie controleren…");
        _ = RefreshSessionAsync();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Uitloggen en opgeslagen gegevens wissen? Verzamelde databasegegevens worden niet verwijderd.",
            "Uitloggen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        await credentialStore.ClearAsync();
        ClearCookies();
        transportProxy.ClearTransport();
        sessionState.SetState(SessionState.LoggedOut);
        PasswordBox.Password = string.Empty;
        FancierText.Text = string.Empty;
        FancierSelectorPanel.Visibility = Visibility.Collapsed;
        FancierComboBox.ItemsSource = null;
        SetSessionText("Uitgelogd. Voer je gegevens opnieuw in.");
    }

    private void ClearCookies()
    {
        try
        {
            foreach (Cookie cookie in cookieContainer.GetCookies(new Uri("https://www.pigeonfancier.com")))
            {
                cookie.Expired = true;
            }
        }
        catch
        {
        }
    }

    private void UpdatePresentation(SessionSnapshot snapshot)
    {
        var presentation = SessionStatusPresentation.From(snapshot);
        ConnectionStatusTitle.Text = presentation.Title;
        ConnectionStatusDescription.Text = presentation.Description;
        PrimaryConnectionAction.Content = presentation.ActionText;
        LastCheckedText.Text = $"Laatst gecontroleerd {snapshot.LastCheckedAtUtc.ToLocalTime():g}";
        FancierText.Text = snapshot.SelectedFancier?.DisplayName is { Length: > 0 } name
            ? $"Geselecteerde melker: {name} (ID {snapshot.SelectedFancier.Id})"
            : "Geen melker geselecteerd";
        ConnectionStatusBanner.Background = (Brush)FindResource($"{presentation.Severity}BannerBrush");

        var isAuthenticated = snapshot.State is SessionState.AuthenticatedNoFancier or SessionState.AuthenticatedReady;
        LoginPanel.Visibility = Visibility.Visible;
        LogoutButton.Visibility = isAuthenticated ? Visibility.Visible : Visibility.Collapsed;
        FancierSelectorPanel.Visibility = snapshot.State == SessionState.AuthenticatedNoFancier
            ? Visibility.Visible
            : Visibility.Collapsed;

        StepOneBorder.Background = presentation.Step >= 1 ? (Brush)FindResource("SuccessBannerBrush") : (Brush)FindResource("NeutralBannerBrush");
        StepTwoBorder.Background = presentation.Step >= 2 ? (Brush)FindResource("SuccessBannerBrush") : (Brush)FindResource("NeutralBannerBrush");
        StepThreeBorder.Background = presentation.Step >= 3 ? (Brush)FindResource("SuccessBannerBrush") : (Brush)FindResource("NeutralBannerBrush");
        StepOneText.Text = presentation.Step > 1 ? "Voltooid" : "Voer je e-mailadres en wachtwoord in";
        StepTwoText.Text = presentation.Step > 2 ? "Voltooid" : "Kies een account";
        StepThreeText.Text = presentation.Step >= 3 ? "Klaar om te synchroniseren" : "Sync wordt hier beschikbaar";
        UpdateSyncControls();
        if (snapshot.ErrorMessage is not null)
        {
            SetTechnicalDetails(snapshot.ErrorMessage);
        }
    }

    private void ShowLoginError(string message)
    {
        LoginErrorText.Text = message;
        LoginErrorText.Visibility = Visibility.Visible;
    }

    private void SetSessionText(string text)
    {
        if (!string.Equals(SessionText.Text, text, StringComparison.Ordinal))
        {
            SessionText.Text = text;
        }
    }

    private void SetFancierText(string text)
    {
        if (!string.Equals(FancierText.Text, text, StringComparison.Ordinal))
        {
            FancierText.Text = text;
        }
    }

    private void SetTechnicalDetails(string text)
    {
        TechnicalDetailsText.Text = string.IsNullOrWhiteSpace(text) ? "Geen technische details." : text;
    }

    private static string FormatTransportError(TransportResponse response) =>
        string.IsNullOrWhiteSpace(response.ErrorMessage) ? string.Empty : $" ({response.ErrorMessage})";

    private static T? Deserialize<T>(string body)
    {
        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            });
    }
}
