using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App;

public partial class FlightResultsView : UserControl
{
    private readonly IFlightResultsReader flightResultsReader;
    private readonly ISessionStateService sessionState;
    private FlightResultsPageData? currentData;

    public FlightResultsView(
        IFlightResultsReader flightResultsReader,
        ISessionStateService sessionState)
    {
        InitializeComponent();
        this.flightResultsReader = flightResultsReader;
        this.sessionState = sessionState;
    }

    public async Task RefreshAsync()
    {
        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
        {
            FlightStatusText.Text = "Selecteer een melker voordat je vluchten bekijkt.";
            return;
        }

        try
        {
            FlightStatusText.Text = "Vluchtresultaten laden...";
            currentData = await flightResultsReader.GetFlightResultsAsync(selectedFancierId);

            ProfileGrid.ItemsSource = currentData.PigeonProfiles;
            ApplyResultsFilter();

            var resultCount = currentData.RecentResults.Count;
            var profileCount = currentData.PigeonProfiles.Count;
            FlightStatusText.Text = resultCount == 0
                ? "Geen vluchtresultaten gevonden. Voer een sync uit na afgelopen vluchten."
                : $"{resultCount} resultaten voor {profileCount} duiven.";
        }
        catch (Exception ex)
        {
            FlightStatusText.Text = $"Fout bij laden: {ex.Message}";
        }
    }

    private void ProfileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyResultsFilter();
    }

    private void CategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyResultsFilter();
    }

    private void ApplyResultsFilter()
    {
        if (currentData is null)
            return;

        var results = currentData.RecentResults.AsEnumerable();

        // Filter by selected pigeon in profile grid
        if (ProfileGrid.SelectedItem is PigeonDistanceProfile selected)
        {
            results = results.Where(r => r.PigeonId == selected.PigeonId);
        }

        // Filter by distance category
        if (CategoryFilterCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<DistanceCategory>(tag, out var category))
        {
            results = results.Where(r => r.Category == category);
        }

        ResultsGrid.ItemsSource = results.ToList();
    }
}
