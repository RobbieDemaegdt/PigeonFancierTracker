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
    private readonly FoodDistributionIngester foodDistributionIngester;
    private readonly SponsorIngester sponsorIngester;
    private readonly ISettingsService settings;
    private readonly ConnectionView connectionView;
    private readonly PigeonHistoryView pigeonHistoryView;
    private readonly TransferView transferView;
    private readonly FlightResultsView flightResultsView;
    private readonly RankingView rankingView;
    private readonly SponsorView sponsorView;
    private readonly DataView dataView;
    private readonly IPigeonOverviewReader pigeonOverviewReader;
    private readonly IAutoBidService autoBidService;
    private readonly DispatcherTimer autoSyncTimer;
    private IReadOnlyList<PigeonListItem>? allPigeons;
    private IReadOnlyList<PigeonListItem>? allOverviewPigeons;
    private int? selectedPigeonId;
    private bool overviewLoaded;
    private bool breedingLoaded;

    public MainWindow(
        ISessionStateService sessionState,
        ISyncCoordinator syncCoordinator,
        ITrackerDataReader trackerDataReader,
        IBreedingDataReader breedingDataReader,
        FlightResultIngester flightResultIngester,
        FoodDistributionIngester foodDistributionIngester,
        SponsorIngester sponsorIngester,
        IPigeonOverviewReader pigeonOverviewReader,
        ISettingsService settings,
        StartupManager startupManager,
        ConnectionView connectionView,
        PigeonHistoryView pigeonHistoryView,
        TransferView transferView,
        FlightResultsView flightResultsView,
        RankingView rankingView,
        SponsorView sponsorView,
        DataView dataView,
        IAutoBidService autoBidService)
    {
        InitializeComponent();
        this.sessionState = sessionState;
        this.syncCoordinator = syncCoordinator;
        this.trackerDataReader = trackerDataReader;
        this.breedingDataReader = breedingDataReader;
        this.flightResultIngester = flightResultIngester;
        this.foodDistributionIngester = foodDistributionIngester;
        this.sponsorIngester = sponsorIngester;
        this.pigeonOverviewReader = pigeonOverviewReader;
        this.settings = settings;
        this.connectionView = connectionView;
        this.pigeonHistoryView = pigeonHistoryView;
        this.transferView = transferView;
        this.flightResultsView = flightResultsView;
        this.rankingView = rankingView;
        this.sponsorView = sponsorView;
        this.autoBidService = autoBidService;
        this.dataView = dataView;
        ConnectionHost.Content = connectionView;
        HistoryHost.Content = pigeonHistoryView;
        TransferHost.Content = transferView;
        FlightsHost.Content = flightResultsView;
        RankingHost.Content = rankingView;
        SponsorHost.Content = sponsorView;
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
                connectionView.StartSync(SyncProfile.Standard);
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

        overviewLoaded = false;
        breedingLoaded = false;
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
                SyncStatusText.Text = "Voedergegevens verwerken...";
                await foodDistributionIngester.BackfillAsync(fid);
                await foodDistributionIngester.IngestAsync(fid);
                SyncStatusText.Text = "Sponsorgegevens verwerken...";
                await sponsorIngester.BackfillAsync(fid);
                await sponsorIngester.IngestAsync(fid);
                SyncStatusText.Text = result.Status;
            }
        }
        catch
        {
        }

        await flightResultsView.RefreshAsync();
        await rankingView.RefreshAsync();
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
    private static readonly Brush SidebarSelectedFill = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    static MainWindow()
    {
        OnlineFill.Freeze();
        OfflineFill.Freeze();
        WarningFill.Freeze();
        DangerFill.Freeze();
        SidebarSelectedFill.Freeze();
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
            selectedPigeonId = null;
            PigeonGrid.ItemsSource = null;
            UpdatePigeonDetail(null);
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

            breedingLoaded = false;
        }
        catch (Exception exception)
        {
            DataMessageText.Text = $"De lokale dataweergave kon niet worden geladen: {exception.Message}";
        }
    }

    private async Task LoadBreedingAnalysisAsync(int? selectedFancierId)
    {
        if (selectedFancierId is not int fancierId)
        {
            BreedingStatusText.Text = "Selecteer een melker en voer een sync uit.";
            BreedingPairGrid.ItemsSource = null;
            InbreedingGrid.ItemsSource = null;
            BreedingPairCountText.Text = "—";
            FlockInbreedingText.Text = "—";
            PedigreeCountText.Text = "—";
            return;
        }

        var isOnline = sessionState.Current.State is SessionState.AuthenticatedReady;
        BreedingStatusText.Text = isOnline
            ? "Fokgegevens worden geladen (stamboom- en nakomelingdata worden opgehaald)..."
            : "Fokgegevens worden geladen vanuit cache (niet aangemeld, alleen gecachte data beschikbaar)...";
        BreedingPairGrid.ItemsSource = null;
        InbreedingGrid.ItemsSource = null;
        BreedingPairCountText.Text = "—";
        FlockInbreedingText.Text = "—";
        PedigreeCountText.Text = "—";

        try
        {
            var data = await breedingDataReader.GetBreedingAnalysisAsync(fancierId);
            breedingLoaded = true;
            BreedingPairGrid.ItemsSource = data.PairPerformance;
            InbreedingGrid.ItemsSource = data.InbreedingReport;
            BreedingPairCountText.Text = data.PairPerformance.Count.ToString(CultureInfo.CurrentCulture);
            FlockInbreedingText.Text = data.FlockAvgInbreeding > 0
                ? $"{data.FlockAvgInbreeding:P2}"
                : "0%";
            PedigreeCountText.Text = data.InbreedingReport.Count(i => i.LineageDepth > 0)
                .ToString(CultureInfo.CurrentCulture);

            if (data.PairPerformance.Count == 0 && data.InbreedingReport.Count == 0)
            {
                BreedingStatusText.Text = isOnline
                    ? "Geen fokgegevens beschikbaar. Zorg dat er koppels en stamboomgegevens zijn."
                    : "Geen fokgegevens beschikbaar. Meld je aan en ververs om live data op te halen.";
            }
            else
            {
                BreedingStatusText.Text = !isOnline
                    ? "Weergave op basis van gecachte data. Meld je aan en ververs voor actuele gegevens."
                    : "";
            }
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

    private void OpenRanking_Click(object sender, RoutedEventArgs e)
    {
        SetPage(RankingPage);
        _ = rankingView.RefreshAsync();
    }

    private void OpenSponsors_Click(object sender, RoutedEventArgs e)
    {
        SetPage(SponsorPage);
        _ = sponsorView.RefreshAsync();
    }

    private void SetPage(UIElement page)
    {
        DashboardPage.Visibility = page == DashboardPage ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPage.Visibility = page == ConnectionPage ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = page == HistoryPage ? Visibility.Visible : Visibility.Collapsed;
        TransferPage.Visibility = page == TransferPage ? Visibility.Visible : Visibility.Collapsed;
        FlightsPage.Visibility = page == FlightsPage ? Visibility.Visible : Visibility.Collapsed;
        RankingPage.Visibility = page == RankingPage ? Visibility.Visible : Visibility.Collapsed;
        SponsorPage.Visibility = page == SponsorPage ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = page == DataPage ? Visibility.Visible : Visibility.Collapsed;
        SetNavigationButtonState(DashboardNavButton, page == DashboardPage);
        SetNavigationButtonState(ConnectionNavButton, page == ConnectionPage);
        SetNavigationButtonState(HistoryNavButton, page == HistoryPage);
        SetNavigationButtonState(TransferNavButton, page == TransferPage);
        SetNavigationButtonState(FlightsNavButton, page == FlightsPage);
        SetNavigationButtonState(RankingNavButton, page == RankingPage);
        SetNavigationButtonState(SponsorNavButton, page == SponsorPage);
        SetNavigationButtonState(DataNavButton, page == DataPage);

        (ShellPageTitle.Text, ShellPageSubtitle.Text) = page switch
        {
            var current when current == DashboardPage => ("Hokoverzicht", "Duiven, hokstatus en lokale gegevens in één overzicht"),
            var current when current == HistoryPage => ("Duivengeschiedenis", "Ontwikkeling, gezondheid en waarde per duif"),
            var current when current == FlightsPage => ("Vluchten", "Actieve, komende en voltooide wedstrijden"),
            var current when current == RankingPage => ("Klassement", "Regionale en nationale rangschikkingen"),
            var current when current == TransferPage => ("Transfers", "Actieve biedingen, historiek en marktinzichten"),
            var current when current == SponsorPage => ("Sponsoring", "Contracten, aanbiedingen en inkomsten"),
            var current when current == ConnectionPage => ("Verbinding", "Aanmelden, melker selecteren en synchroniseren"),
            _ => ("Gegevens & instellingen", "Back-ups, automatisch synchroniseren en lokale opslag"),
        };
    }

    private static void SetNavigationButtonState(Button button, bool isSelected)
    {
        button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        button.Background = isSelected ? SidebarSelectedFill : Brushes.Transparent;
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
            UpdatePigeonDetail(null);
            return;
        }

        var search = PigeonSearchBox.Text?.Trim();
        var filteredPigeons = string.IsNullOrEmpty(search)
            ? allPigeons
            : allPigeons
                .Where(p => p.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

        PigeonGrid.ItemsSource = filteredPigeons;
        var selectedPigeon = selectedPigeonId is int pigeonId
            ? filteredPigeons.FirstOrDefault(p => p.SourceId == pigeonId)
            : null;
        selectedPigeon ??= filteredPigeons.FirstOrDefault();
        PigeonGrid.SelectedItem = selectedPigeon;
        UpdatePigeonDetail(selectedPigeon);
    }

    private void PigeonGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedPigeon = PigeonGrid.SelectedItem as PigeonListItem;
        selectedPigeonId = selectedPigeon?.SourceId;
        UpdatePigeonDetail(selectedPigeon);
    }

    private void UpdatePigeonDetail(PigeonListItem? pigeon)
    {
        if (pigeon is null)
        {
            PigeonDetailTitleText.Text = "Geen duif geselecteerd";
            PigeonDetailIdentityText.Text = "Pas je zoekopdracht aan of synchroniseer om duiven te laden.";
            PigeonDetailHealthText.Text = "—";
            PigeonDetailRaceText.Text = "—";
            PigeonDetailFinanceText.Text = "—";
            PigeonDetailSkillsText.Text = "—";
            OpenPigeonHistoryButton.IsEnabled = false;
            return;
        }

        PigeonDetailTitleText.Text = pigeon.DisplayName;
        PigeonDetailIdentityText.Text = $"ID {pigeon.SourceId?.ToString(CultureInfo.CurrentCulture) ?? "—"} · {pigeon.Sex ?? "Geslacht onbekend"} · {pigeon.Age ?? "Leeftijd onbekend"} · {pigeon.Breed ?? "Ras onbekend"}";
        var flightStatus = pigeon.Flying switch
        {
            true => "In vlucht",
            false => "Niet in vlucht",
            null => "Vluchtstatus onbekend",
        };
        PigeonDetailHealthText.Text = $"{pigeon.Disease ?? "Geen ziekte geregistreerd"} · {flightStatus} · Training {pigeon.TrainingDisplay ?? "—"}";
        PigeonDetailRaceText.Text = $"{pigeon.RaceCount?.ToString(CultureInfo.CurrentCulture) ?? "—"} vluchten · {pigeon.TotalPoints?.ToString(CultureInfo.CurrentCulture) ?? "—"} punten";
        PigeonDetailFinanceText.Text = $"Verdiensten {pigeon.EarningsDisplay ?? "—"} · Waarde {pigeon.Premium?.ToString("C2", CultureInfo.CurrentCulture) ?? "—"}";
        PigeonDetailSkillsText.Text = $"Vorm {pigeon.FormDisplay ?? "—"} · Ervaring {pigeon.ExperienceDisplay ?? "—"} · Conditie {pigeon.StaminaDisplay ?? "—"} · Snelheid {pigeon.SpeedDisplay ?? "—"} · Navigatie {pigeon.NavigationDisplay ?? "—"}";
        OpenPigeonHistoryButton.IsEnabled = pigeon.SourceId.HasValue;
    }

    private async void OpenSelectedPigeonHistory_Click(object sender, RoutedEventArgs e)
    {
        if (PigeonGrid.SelectedItem is not PigeonListItem { SourceId: int pigeonId })
            return;

        SetPage(HistoryPage);
        await pigeonHistoryView.OpenPigeonAsync(pigeonId);
    }

    private void DashboardTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl || e.AddedItems.Count == 0
            || e.AddedItems[0] is not TabItem tab || tab.Header is not string header)
            return;

        if (!overviewLoaded && header.Contains("historisch", StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshOverviewAsync();
        }
        else if (!breedingLoaded && header.Contains("Fokanalyse", StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadBreedingAnalysisAsync(sessionState.Current.SelectedFancier?.Id);
        }
    }

    private void RefreshOverview_Click(object sender, RoutedEventArgs e) => _ = RefreshOverviewAsync();

    private void RefreshBreeding_Click(object sender, RoutedEventArgs e)
    {
        breedingLoaded = false;
        _ = LoadBreedingAnalysisAsync(sessionState.Current.SelectedFancier?.Id);
    }

    private async Task RefreshOverviewAsync()
    {
        var selectedFancierId = sessionState.Current.SelectedFancier?.Id;
        if (selectedFancierId is not int fancierId)
        {
            allOverviewPigeons = null;
            OverviewPigeonGrid.ItemsSource = null;
            OverviewMessageText.Text = "Selecteer een melker en voer een sync uit om historische data te laden.";
            OverviewPigeonCountText.Text = "—";
            OverviewObservationCountText.Text = "—";
            OverviewOldestText.Text = "—";
            OverviewNewestText.Text = "—";
            return;
        }

        try
        {
            OverviewMessageText.Text = "Historisch overzicht laden…";
            var data = await pigeonOverviewReader.GetOverviewAsync(fancierId);
            allOverviewPigeons = data.Pigeons;
            overviewLoaded = true;
            ApplyOverviewFilter();
            OverviewMessageText.Text = data.Pigeons.Count == 0
                ? "Geen duivengegevens gevonden in de lokale momentopnames. Voer eerst een sync uit."
                : $"{data.Pigeons.Count} duif(en) uit {data.TotalObservations} waarnemingen over alle momentopnames. Pijlen vergelijken met de vorige wijziging.";
            OverviewPigeonCountText.Text = data.Pigeons.Count.ToString(CultureInfo.CurrentCulture);
            OverviewObservationCountText.Text = data.TotalObservations.ToString(CultureInfo.CurrentCulture);
            OverviewOldestText.Text = data.OldestSnapshot?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
            OverviewNewestText.Text = data.NewestSnapshot?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
        }
        catch (Exception exception)
        {
            OverviewMessageText.Text = $"Historisch overzicht kon niet worden geladen: {exception.Message}";
        }
    }

    private void OverviewSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyOverviewFilter();
    }

    private void ApplyOverviewFilter()
    {
        if (allOverviewPigeons is null)
        {
            OverviewPigeonGrid.ItemsSource = null;
            return;
        }

        var search = OverviewSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            OverviewPigeonGrid.ItemsSource = allOverviewPigeons;
        }
        else
        {
            OverviewPigeonGrid.ItemsSource = allOverviewPigeons
                .Where(p => p.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }
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
