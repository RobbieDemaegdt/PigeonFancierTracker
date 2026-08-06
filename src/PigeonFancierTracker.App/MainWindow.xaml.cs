using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Media;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.App;

public partial class MainWindow : Window
{
    private readonly ISessionStateService sessionState;
    private readonly ISyncCoordinator syncCoordinator;
    private readonly ITrackerDataReader trackerDataReader;
    private readonly IBreedingDataReader breedingDataReader;
    private readonly FlightResultIngester flightResultIngester;
    private readonly ISettingsService settings;
    private readonly ConnectionView connectionView;
    private readonly PigeonHistoryView pigeonHistoryView;
    private readonly TransferView transferView;
    private readonly FlightResultsView flightResultsView;
    private readonly DataView dataView;
    private readonly IAutoBidService autoBidService;
    private readonly DispatcherTimer autoSyncTimer;
    private IReadOnlyList<PigeonListItem>? allPigeons;

    public MainWindow(
        ISessionStateService sessionState,
        ISyncCoordinator syncCoordinator,
        ITrackerDataReader trackerDataReader,
        IBreedingDataReader breedingDataReader,
        FlightResultIngester flightResultIngester,
        ISettingsService settings,
        StartupManager startupManager,
        ConnectionView connectionView,
        PigeonHistoryView pigeonHistoryView,
        TransferView transferView,
        FlightResultsView flightResultsView,
        DataView dataView,
        IAutoBidService autoBidService)
    {
        InitializeComponent();
        this.sessionState = sessionState;
        this.syncCoordinator = syncCoordinator;
        this.trackerDataReader = trackerDataReader;
        this.breedingDataReader = breedingDataReader;
        this.flightResultIngester = flightResultIngester;
        this.settings = settings;
        this.connectionView = connectionView;
        this.pigeonHistoryView = pigeonHistoryView;
        this.transferView = transferView;
        this.flightResultsView = flightResultsView;
        this.autoBidService = autoBidService;
        this.dataView = dataView;
        ConnectionHost.Content = connectionView;
        HistoryHost.Content = pigeonHistoryView;
        TransferHost.Content = transferView;
        FlightsHost.Content = flightResultsView;
        DataHost.Content = dataView;
        sessionState.Changed += SessionState_Changed;
        syncCoordinator.ProgressChanged += SyncCoordinator_ProgressChanged;
        connectionView.SyncCompleted += ConnectionView_SyncCompleted;
        UpdateSessionDisplay(sessionState.Current);
        UpdateThemeButtonText();
        _ = RefreshDataAsync();

        startupManager.RefreshExePath();

        autoSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        autoSyncTimer.Tick += AutoSyncTimer_Tick;

        ContentRendered += MainWindow_ContentRendered;
    }

    private async void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        try
        {
            await connectionView.TryRestoreSessionAsync();
            if (sessionState.Current.State == SessionState.AuthenticatedReady)
            {
                connectionView.StartSync();
            }
        }
        catch
        {
        }

        autoSyncTimer.Start();
    }

    private async void AutoSyncTimer_Tick(object? sender, EventArgs e)
    {
        if (!settings.AutoDailySync) return;
        if (syncCoordinator.IsRunning) return;
        if (settings.LastAutoSyncDate == DateOnly.FromDateTime(DateTime.Now)) return;

        if (sessionState.Current.State != SessionState.AuthenticatedReady)
        {
            try
            {
                await connectionView.TryRestoreSessionAsync();
            }
            catch
            {
                return;
            }

            if (sessionState.Current.State != SessionState.AuthenticatedReady) return;
        }

        connectionView.StartSync();
    }

    public void OpenDashboardView()
    {
        SetPage(DashboardPage);
    }

    public void OpenConnectionView()
    {
        SetPage(ConnectionPage);
        connectionView.Focus();
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e) => OpenDashboardView();

    private void OpenConnection_Click(object sender, RoutedEventArgs e) => OpenConnectionView();

    private void OpenPigeonHistory_Click(object sender, RoutedEventArgs e)
    {
        SetPage(HistoryPage);
        _ = pigeonHistoryView.RefreshAsync();
    }

    private void OpenTransfers_Click(object sender, RoutedEventArgs e)
    {
        SetPage(TransferPage);
        _ = transferView.RefreshAsync();
    }

    private void DashboardPrimaryAction_Click(object sender, RoutedEventArgs e) => OpenConnectionView();

    private void SessionState_Changed(object? sender, SessionSnapshot snapshot)
    {
        Dispatcher.Invoke(() => UpdateSessionDisplay(snapshot));
        _ = RefreshDataAsync();
    }

    private void RefreshData_Click(object sender, RoutedEventArgs e) => _ = RefreshDataAsync();

    private async void ConnectionView_SyncCompleted(object? sender, SyncRunResult result)
    {
        LastSyncText.Text = result.Status switch
        {
            "Succeeded" => "Geslaagd",
            "PartiallySucceeded" => "Gedeeltelijk",
            "SessionExpired" => "Verlopen",
            "Cancelled" => "Geannuleerd",
            _ => "Mislukt",
        };
        SyncStatusText.Text = result.Status;

        if (result.IsSuccess)
        {
            settings.LastAutoSyncDate = DateOnly.FromDateTime(DateTime.Now);
            settings.Save();
        }

        await RefreshDataAsync();
        await pigeonHistoryView.RefreshAsync();
        await transferView.RefreshAsync();

        try
        {
            var fancierId = sessionState.Current.SelectedFancier?.Id;
            if (fancierId is int fid)
            {
                SyncStatusText.Text = "Vluchten ophalen...";
                await flightResultIngester.IngestAsync(fid);
                SyncStatusText.Text = result.Status;
            }
        }
        catch
        {
        }
    }

    private void SyncCoordinator_ProgressChanged(object? sender, SyncProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            SyncStatusText.Text = $"Sync {progress.CompletedEndpoints}/{progress.TotalEndpoints}";
        });
    }

    private static readonly Brush OnlineFill = new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x68));
    private static readonly Brush OfflineFill = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly Brush WarningFill = new SolidColorBrush(Color.FromRgb(0xD4, 0x8B, 0x00));
    private static readonly Brush DangerFill = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    static MainWindow()
    {
        OnlineFill.Freeze();
        OfflineFill.Freeze();
        WarningFill.Freeze();
        DangerFill.Freeze();
    }

    private void UpdateSessionDisplay(SessionSnapshot snapshot)
    {
        var presentation = SessionStatusPresentation.From(snapshot);
        SessionStatusText.Text = presentation.Title;
        HeaderFancierText.Text = snapshot.SelectedFancier?.DisplayName is { Length: > 0 } name
            ? $"Volgen: {name}"
            : "Geen melker geselecteerd";
        SelectedFancierText.Text = snapshot.SelectedFancier?.DisplayName
            ?? (snapshot.SelectedFancier?.Id is int id ? $"Melker #{id}" : "Geen melker geselecteerd");

        var isOnline = snapshot.State == SessionState.AuthenticatedReady;
        var isPartial = snapshot.State == SessionState.AuthenticatedNoFancier;
        var isDanger = snapshot.State is SessionState.SessionExpired or SessionState.TransportUnavailable;

        DashboardStatusTitle.Text = isOnline ? "Online" : isPartial ? "Geen melker geselecteerd" : isDanger ? presentation.Title : "Offline";
        DashboardLastCheckedText.Text = $"Laatst gecontroleerd {snapshot.LastCheckedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        DashboardPrimaryAction.Content = "Naar verbinding";
        DashboardStatusBanner.Background = (Brush)FindResource($"{presentation.Severity}BannerBrush");
        ConnectionIndicator.Fill = isOnline ? OnlineFill : isDanger ? DangerFill : isPartial ? WarningFill : OfflineFill;
    }

    private async Task RefreshDataAsync()
    {
        var selectedFancierId = sessionState.Current.SelectedFancier?.Id;
        if (selectedFancierId is not int fancierId)
        {
            allPigeons = null;
            PigeonGrid.ItemsSource = null;
            DataMessageText.Text = "Selecteer een melker en voer een sync uit om lokale data te laden.";
            DataPigeonCountText.Text = "—";
            DataCapitalText.Text = "—";
            DataFoodText.Text = "";
            DataAverageSkillText.Text = "—";
            DataAverageSkillChangeText.Text = "—";
            DataLastCapturedText.Text = "—";
            SeasonText.Text = "—";
            PenCapacityText.Text = "—";
            FancierLocationText.Text = "";
            return;
        }

        try
        {
            var data = await trackerDataReader.GetDashboardAsync(fancierId);
            allPigeons = data.Pigeons;
            ApplyPigeonFilter();
            DataMessageText.Text = data.Pigeons.Count == 0
                ? "Er is nog geen duivenmomentopname beschikbaar. Voer nu een sync uit om er een vast te leggen."
                : $"Laatste lokale momentopname voor {data.FancierName ?? $"melker #{fancierId}"}.";
            DataPigeonCountText.Text = data.PigeonCount?.ToString(CultureInfo.CurrentCulture) ?? "—";
            DataCapitalText.Text = FormatCurrency(data.Capital);
            DataFoodText.Text = data.FoodDistribution is { } dist ? $"Voeder: {dist}" : "";
            DataAverageSkillText.Text = data.AverageTotalSkill?.ToString("N1", CultureInfo.CurrentCulture) ?? "—";
            DataAverageSkillChangeText.Text = data.AverageSkillChange is decimal avgChange
                ? avgChange.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)
                : "—";
            DataLastCapturedText.Text = data.LastDataCapturedAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
            var seasonDisplay = data.SeasonNumber is int sn && data.SeasonWeek is int sw
                ? $"S{sn} W{sw}"
                : "—";
            SeasonText.Text = seasonDisplay;
            HeaderSeasonText.Text = seasonDisplay != "—" ? $"Seizoen {data.SeasonNumber}, Week {data.SeasonWeek}" : "";
            PenCapacityText.Text = data.PenOccupied is int occ && data.PenCapacity is int cap
                ? $"{occ} / {cap}"
                : "—";
            FancierLocationText.Text = data.LocationName ?? "";

            _ = LoadBreedingAnalysisAsync(fancierId);
        }
        catch (Exception exception)
        {
            DataMessageText.Text = $"De lokale dataweergave kon niet worden geladen: {exception.Message}";
        }
    }

    private async Task LoadBreedingAnalysisAsync(int fancierId)
    {
        BreedingStatusText.Text = "Fokgegevens worden geladen...";
        BreedingPairGrid.ItemsSource = null;
        InbreedingGrid.ItemsSource = null;
        BreedingPairCountText.Text = "—";
        FlockInbreedingText.Text = "—";
        PedigreeCountText.Text = "—";

        try
        {
            var data = await breedingDataReader.GetBreedingAnalysisAsync(fancierId);
            BreedingPairGrid.ItemsSource = data.PairPerformance;
            InbreedingGrid.ItemsSource = data.InbreedingReport;
            BreedingPairCountText.Text = data.PairPerformance.Count.ToString(CultureInfo.CurrentCulture);
            FlockInbreedingText.Text = data.FlockAvgInbreeding > 0
                ? $"{data.FlockAvgInbreeding:P2}"
                : "0%";
            PedigreeCountText.Text = data.InbreedingReport.Count(i => i.LineageDepth > 0)
                .ToString(CultureInfo.CurrentCulture);
            BreedingStatusText.Text = data.PairPerformance.Count == 0 && data.InbreedingReport.Count == 0
                ? "Geen fokgegevens beschikbaar. Zorg dat er koppels en stamboomgegevens zijn."
                : "";
        }
        catch (Exception ex)
        {
            BreedingStatusText.Text = $"Fokanalyse kon niet worden geladen: {ex.Message}";
        }
    }

    private static string FormatCurrency(decimal? value) =>
        value?.ToString("C2", CultureInfo.CurrentCulture) ?? "—";

    private void OpenData_Click(object sender, RoutedEventArgs e) => SetPage(DataPage);

    private void OpenFlights_Click(object sender, RoutedEventArgs e)
    {
        SetPage(FlightsPage);
        _ = flightResultsView.RefreshAsync();
    }

    private void SetPage(UIElement page)
    {
        DashboardPage.Visibility = page == DashboardPage ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPage.Visibility = page == ConnectionPage ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = page == HistoryPage ? Visibility.Visible : Visibility.Collapsed;
        TransferPage.Visibility = page == TransferPage ? Visibility.Visible : Visibility.Collapsed;
        FlightsPage.Visibility = page == FlightsPage ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = page == DataPage ? Visibility.Visible : Visibility.Collapsed;
        DashboardNavButton.FontWeight = page == DashboardPage ? FontWeights.SemiBold : FontWeights.Normal;
        ConnectionNavButton.FontWeight = page == ConnectionPage ? FontWeights.SemiBold : FontWeights.Normal;
        HistoryNavButton.FontWeight = page == HistoryPage ? FontWeights.SemiBold : FontWeights.Normal;
        TransferNavButton.FontWeight = page == TransferPage ? FontWeights.SemiBold : FontWeights.Normal;
        FlightsNavButton.FontWeight = page == FlightsPage ? FontWeights.SemiBold : FontWeights.Normal;
        DataNavButton.FontWeight = page == DataPage ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        settings.DarkMode = !settings.DarkMode;
        settings.Save();
        ((App)Application.Current).ApplyTheme(settings.DarkMode);
        UpdateThemeButtonText();
    }

    private void UpdateThemeButtonText()
    {
        ThemeToggleButton.Content = settings.DarkMode ? "☀ Licht thema" : "☽ Donker thema";
    }

    private void PigeonSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyPigeonFilter();
    }

    private void ApplyPigeonFilter()
    {
        if (allPigeons is null)
        {
            PigeonGrid.ItemsSource = null;
            return;
        }

        var search = PigeonSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            PigeonGrid.ItemsSource = allPigeons;
        }
        else
        {
            PigeonGrid.ItemsSource = allPigeons
                .Where(p => p.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }
    }

    private void ShowAllColumns_Changed(object sender, RoutedEventArgs e)
    {
        var show = ShowAllColumnsToggle.IsChecked == true;
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        ColForm.Visibility = vis;
        ColExperience.Visibility = vis;
        ColStamina.Visibility = vis;
        ColSpeed.Visibility = vis;
        ColNavigation.Visibility = vis;
        ColTechnique.Visibility = vis;
        ColAerodynamics.Visibility = vis;
        ColIntelligence.Visibility = vis;
        ColLibido.Visibility = vis;
        ColNightvision.Visibility = vis;
    }

    protected override void OnClosed(EventArgs e)
    {
        autoBidService.Stop();
        autoSyncTimer.Stop();
        sessionState.Changed -= SessionState_Changed;
        syncCoordinator.ProgressChanged -= SyncCoordinator_ProgressChanged;
        connectionView.SyncCompleted -= ConnectionView_SyncCompleted;
        syncCoordinator.Cancel();
        base.OnClosed(e);
    }
}
