using System.Windows;
using System.Globalization;
using System.Windows.Media;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISessionStateService sessionState;
    private readonly ISyncCoordinator syncCoordinator;
    private readonly ITrackerDataReader trackerDataReader;
    private readonly ConnectionView connectionView;
    private readonly PigeonHistoryView pigeonHistoryView;
    private readonly TransferView transferView;
    private readonly DataView dataView;

    public MainWindow(
        ISessionStateService sessionState,
        ISyncCoordinator syncCoordinator,
        ITrackerDataReader trackerDataReader,
        ConnectionView connectionView,
        PigeonHistoryView pigeonHistoryView,
        TransferView transferView,
        DataView dataView)
    {
        InitializeComponent();
        this.sessionState = sessionState;
        this.syncCoordinator = syncCoordinator;
        this.trackerDataReader = trackerDataReader;
        this.connectionView = connectionView;
        this.pigeonHistoryView = pigeonHistoryView;
        this.transferView = transferView;
        this.dataView = dataView;
        ConnectionHost.Content = connectionView;
        HistoryHost.Content = pigeonHistoryView;
        TransferHost.Content = transferView;
        DataHost.Content = dataView;
        sessionState.Changed += SessionState_Changed;
        syncCoordinator.ProgressChanged += SyncCoordinator_ProgressChanged;
        connectionView.SyncCompleted += ConnectionView_SyncCompleted;
        UpdateSessionDisplay(sessionState.Current);
        _ = RefreshDataAsync();
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
            // Startup session restore is best-effort.
        }
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

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        OpenDashboardView();
    }

    private void OpenConnection_Click(object sender, RoutedEventArgs e)
    {
        OpenConnectionView();
    }

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

    private void DashboardPrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        OpenConnectionView();
    }

    private void SessionState_Changed(object? sender, SessionSnapshot snapshot)
    {
        Dispatcher.Invoke(() => UpdateSessionDisplay(snapshot));
        _ = RefreshDataAsync();
    }

    private void RefreshData_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshDataAsync();
    }

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
        await RefreshDataAsync();
        await pigeonHistoryView.RefreshAsync();
        await transferView.RefreshAsync();
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
            PigeonGrid.ItemsSource = data.Pigeons;
            DataMessageText.Text = data.Pigeons.Count == 0
                ? "Er is nog geen duivenmomentopname beschikbaar. Voer nu een sync uit om er een vast te leggen."
                : $"Laatste lokale momentopname voor {data.FancierName ?? $"melker #{fancierId}"}.";
            DataPigeonCountText.Text = data.PigeonCount?.ToString(CultureInfo.CurrentCulture) ?? "—";
            DataCapitalText.Text = FormatCurrency(data.Capital);
            DataFoodText.Text = data.FoodAmount is decimal food ? $"Voeder: {food:N0}" : "";
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
        }
        catch (Exception exception)
        {
            DataMessageText.Text = $"De lokale dataweergave kon niet worden geladen: {exception.Message}";
        }
    }

    private static string FormatCurrency(decimal? value) =>
        value?.ToString("C2", CultureInfo.CurrentCulture) ?? "—";

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        SetPage(DataPage);
    }

    private void SetPage(UIElement page)
    {
        DashboardPage.Visibility = page == DashboardPage ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPage.Visibility = page == ConnectionPage ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = page == HistoryPage ? Visibility.Visible : Visibility.Collapsed;
        TransferPage.Visibility = page == TransferPage ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = page == DataPage ? Visibility.Visible : Visibility.Collapsed;
        DashboardNavButton.FontWeight = page == DashboardPage ? FontWeights.SemiBold : FontWeights.Normal;
        ConnectionNavButton.FontWeight = page == ConnectionPage ? FontWeights.SemiBold : FontWeights.Normal;
        HistoryNavButton.FontWeight = page == HistoryPage ? FontWeights.SemiBold : FontWeights.Normal;
        TransferNavButton.FontWeight = page == TransferPage ? FontWeights.SemiBold : FontWeights.Normal;
        DataNavButton.FontWeight = page == DataPage ? FontWeights.SemiBold : FontWeights.Normal;
    }

    protected override void OnClosed(EventArgs e)
    {
        sessionState.Changed -= SessionState_Changed;
        syncCoordinator.ProgressChanged -= SyncCoordinator_ProgressChanged;
        connectionView.SyncCompleted -= ConnectionView_SyncCompleted;
        syncCoordinator.Cancel();
        base.OnClosed(e);
    }
}