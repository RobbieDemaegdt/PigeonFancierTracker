using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App;

public partial class RankingView : UserControl
{
    private readonly IRankingDataReader rankingDataReader;
    private readonly IFlightResultsReader flightResultsReader;
    private readonly ISessionStateService sessionState;
    private readonly ISyncCoordinator syncCoordinator;
    private RankingPageData? currentData;

    public RankingView(
        IRankingDataReader rankingDataReader,
        IFlightResultsReader flightResultsReader,
        ISessionStateService sessionState,
        ISyncCoordinator syncCoordinator)
    {
        InitializeComponent();
        this.rankingDataReader = rankingDataReader;
        this.flightResultsReader = flightResultsReader;
        this.sessionState = sessionState;
        this.syncCoordinator = syncCoordinator;
    }

    private async void SyncRanking_Click(object sender, RoutedEventArgs e)
    {
        if (sessionState.Current.State != SessionState.AuthenticatedReady)
        {
            RankingStatusText.Text = "Meld je aan voordat je het klassement synchroniseert.";
            return;
        }

        if (syncCoordinator.IsRunning)
        {
            RankingStatusText.Text = "Er loopt al een synchronisatie. Even geduld…";
            return;
        }

        SyncRankingButton.IsEnabled = false;
        try
        {
            RankingStatusText.Text = "Klassement synchroniseren…";
            await syncCoordinator.SyncAsync(SyncProfile.Standard);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            RankingStatusText.Text = $"Synchronisatie mislukt: {ex.Message}";
        }
        finally
        {
            SyncRankingButton.IsEnabled = true;
        }
    }

    public async Task RefreshAsync()
    {
        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
        {
            RankingStatusText.Text = "Selecteer een melker voordat je het klassement bekijkt.";
            return;
        }

        try
        {
            RankingStatusText.Text = "Klassement laden...";

            currentData = await rankingDataReader.GetRankingAsync(selectedFancierId);

            RegionalFancierGrid.ItemsSource = currentData.RegionalFanciers;
            RegionalPigeonGrid.ItemsSource = currentData.RegionalPigeons;
            NationalFancierGrid.ItemsSource = currentData.NationalFanciers;
            NationalPigeonGrid.ItemsSource = currentData.NationalPigeons;

            RegionalFancierStatusText.Text = currentData.RegionalFanciers.Count > 0
                ? $"{currentData.RegionalFanciers.Count} melker(s) in het regionaal klassement."
                : "";

            await LoadPredictedRankingAsync(selectedFancierId);

            var parts = new List<string>();
            if (currentData.RegionalFanciers.Count > 0)
                parts.Add($"{currentData.RegionalFanciers.Count} regionaal");
            if (currentData.NationalFanciers.Count > 0)
                parts.Add($"{currentData.NationalFanciers.Count} nationaal");

            RankingStatusText.Text = parts.Count > 0
                ? $"Klassement geladen: {string.Join(", ", parts)} melker(s)."
                : "Geen klassementgegevens gevonden. Voer een standaard sync uit om het klassement op te halen.";
        }
        catch (Exception ex)
        {
            RankingStatusText.Text = $"Fout bij laden: {ex.Message}";
        }
    }

    private async void RefreshActiveFlight_Click(object sender, RoutedEventArgs e)
    {
        if (sessionState.Current.State != SessionState.AuthenticatedReady)
        {
            RankingStatusText.Text = "Meld je aan voordat je de actieve vlucht ververst.";
            return;
        }

        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
        {
            RankingStatusText.Text = "Selecteer een melker voordat je de actieve vlucht ververst.";
            return;
        }

        RefreshActiveFlightButton.IsEnabled = false;
        try
        {
            PredictedStatusText.Text = "Actieve vlucht verversen…";
            await LoadPredictedRankingAsync(selectedFancierId);
            RankingStatusText.Text = PredictedTab.Visibility == Visibility.Visible
                ? "Voorspelling bijgewerkt met de huidige stand van de actieve vlucht."
                : "Geen actieve regionale vlucht gevonden.";
        }
        catch (Exception ex)
        {
            RankingStatusText.Text = $"Verversen mislukt: {ex.Message}";
        }
        finally
        {
            RefreshActiveFlightButton.IsEnabled = true;
        }
    }

    private async Task LoadPredictedRankingAsync(int fancierId)
    {
        try
        {
            var activeFlights = await flightResultsReader.GetActiveFlightsAsync(fancierId);
            var regionalActive = activeFlights
                .Where(f => string.Equals(f.FlightType, "regional", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (regionalActive.Count > 0)
            {
                var predicted = await rankingDataReader.GetPredictedRankingAsync(fancierId, activeFlights);
                if (predicted.Count > 0)
                {
                    PredictedRankingGrid.ItemsSource = predicted;
                    var flightInfo = regionalActive[0];
                    PredictedStatusText.Text = $"Gebaseerd op de huidige stand van {flightInfo.Location} " +
                        $"({flightInfo.DistanceKm} km, {flightInfo.Progress}% voortgang).";
                    PredictedTab.Visibility = Visibility.Visible;
                    return;
                }
            }
        }
        catch
        {
        }

        PredictedTab.Visibility = Visibility.Collapsed;
    }

    private void RankingTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }
}
