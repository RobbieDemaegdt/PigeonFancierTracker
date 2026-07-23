using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class PigeonHistoryView : UserControl
{
    private readonly IPigeonHistoryReader historyReader;
    private readonly ISessionStateService sessionState;
    private bool isLoading;

    public PigeonHistoryView(
        IPigeonHistoryReader historyReader,
        ISessionStateService sessionState)
    {
        InitializeComponent();
        this.historyReader = historyReader;
        this.sessionState = sessionState;
        Loaded += PigeonHistoryView_Loaded;
    }

    public Task RefreshAsync()
    {
        int? selectedPigeonId = PigeonSelector.SelectedValue is int pigeonId ? pigeonId : null;
        return LoadHistoryAsync(selectedPigeonId);
    }

    private async void PigeonHistoryView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= PigeonHistoryView_Loaded;
        await RefreshAsync();
    }

    private async void PigeonSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isLoading && PigeonSelector.SelectedValue is int pigeonId)
        {
            await LoadHistoryAsync(pigeonId);
        }
    }

    private async Task LoadHistoryAsync(int? pigeonId = null)
    {
        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
        {
            HistoryStatusText.Text = "Selecteer een melker voordat je de geschiedenis opent.";
            return;
        }

        try
        {
            isLoading = true;
            HistoryStatusText.Text = "Lokale geschiedenis laden…";
            var data = await historyReader.GetHistoryAsync(selectedFancierId, pigeonId);

            PigeonSelector.ItemsSource = data.Pigeons;
            if (data.SelectedPigeon is not null)
            {
                PigeonSelector.SelectedValue = data.SelectedPigeon.SourceId;
            }

            HistoryGrid.ItemsSource = data.Points;
            UpdateSummary(data);
            HistoryStatusText.Text = data.Points.Count == 0
                ? data.Pigeons.Count == 0
                    ? "Geen duivengegevens gevonden in de lokale momentopnames. Voer eerst een sync uit."
                    : "Geen waarnemingen beschikbaar voor de geselecteerde duif."
                : $"{data.Points.Count} wijziging(en) uit alle lokale momentopnames; duplicaten verborgen.";
        }
        catch (Exception exception)
        {
            HistoryStatusText.Text = $"Geschiedenis kon niet worden geladen: {exception.Message}";
            HistoryGrid.ItemsSource = null;
            UpdateSummary(new PigeonHistoryData([], null, []));
        }
        finally
        {
            isLoading = false;
        }
    }

    private void UpdateSummary(PigeonHistoryData data)
    {
        var selected = data.SelectedPigeon;
        var first = data.Points.FirstOrDefault();
        var latest = data.Points.LastOrDefault();

        PigeonTitleText.Text = selected is null
            ? "Selecteer een duif"
            : $"{selected.DisplayName} · {selected.Breed ?? "Ras onbekend"}";
        PigeonSummaryText.Text = selected is null
            ? string.Empty
            : $"ID {selected.SourceId} · {selected.Sex ?? "Geslacht onbekend"} · {selected.Age ?? "Leeftijd onbekend"} · "
                + $"eerste waarneming {selected.FirstObservedAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—"}";
        LatestTotalSkillText.Text = FormatNumber(latest?.TotalSkill);
        TotalSkillChangeText.Text = first?.TotalSkill is decimal firstTotal && latest?.TotalSkill is decimal latestTotal
            ? FormatSigned(latestTotal - firstTotal)
            : "—";
        LatestPremiumText.Text = latest?.Premium?.ToString("C2", CultureInfo.CurrentCulture) ?? "—";
        ObservationCountText.Text = data.Points.Count.ToString(CultureInfo.CurrentCulture);
        LatestObservedText.Text = latest?.ObservedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
    }

    private static string FormatNumber(decimal? value) =>
        value?.ToString("N1", CultureInfo.CurrentCulture) ?? "—";

    private static string FormatSigned(decimal value) =>
        value.ToString("+0.0;-0.0;—", CultureInfo.CurrentCulture);
}