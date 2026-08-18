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
    private ActiveFlightInfo? currentActiveFlight;

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

            await LoadActiveFlightsAsync(selectedFancierId);
            await LoadUpcomingFlightsAsync(selectedFancierId);
            await LoadCompletedFlightsAsync(selectedFancierId);

            currentData = await flightResultsReader.GetFlightResultsAsync(selectedFancierId);

            ProfileGrid.ItemsSource = currentData.PigeonProfiles;
            ApplyResultsFilter();

            if (currentData.FoodAnalysis is { } analysis && analysis.MixPerformances.Count > 0)
            {
                var rows = analysis.MixPerformances.Select(p => new FoodMixPerformanceRow(p)).ToList();
                FoodAnalysisGrid.ItemsSource = rows;
                FoodAnalysisStatus.Text = $"{analysis.MixPerformances.Count} voedermix(en) gevonden — gesorteerd op beste gemiddelde positie.";
            }
            else
            {
                FoodAnalysisGrid.ItemsSource = null;
                FoodAnalysisStatus.Text = "Nog geen voedergegevens gekoppeld aan vluchten. Synchroniseer om voedergeschiedenis op te bouwen.";
            }

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

    private async Task LoadActiveFlightsAsync(int fancierId)
    {
        try
        {
            var activeFlights = await flightResultsReader.GetActiveFlightsAsync(fancierId);
            if (activeFlights.Count > 0)
            {
                var flight = activeFlights[0];
                currentActiveFlight = flight;
                var totalPrizes = PigeonFancierTracker.Core.Analytics.PrizeCalculator.GetTotalPrizePositions(flight.Subscribers);
                ActiveFlightHeaderText.Text = $"{flight.Location} — {flight.FlightType} — {flight.DistanceKm} km ({flight.Category}) — " +
                    $"{flight.Subscribers} deelnemers — Voortgang: {flight.Progress}%";
                ActivePrizeInfo.Text = $"{totalPrizes} prijsposities voor {flight.Subscribers} deelnemers — " +
                    $"Inschrijfgeld: EUR {flight.EntryPrice:0.00} — Prijzenpot: EUR {flight.TotalPrizePool:0.00}";
                ActivePrizeTableGrid.ItemsSource = flight.PrizeTable;
                ActivePigeonGrid.ItemsSource = flight.PigeonStandings;
                ActiveFancierGrid.ItemsSource = flight.FancierStandings;
                ActiveFlightDetails.Visibility = Visibility.Visible;
            }
            else
            {
                currentActiveFlight = null;
                ActiveFlightHeaderText.Text = "Geen actieve vlucht op dit moment. Synchroniseer als er een vlucht bezig is.";
                ActiveFlightDetails.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            currentActiveFlight = null;
            ActiveFlightHeaderText.Text = "Kan actieve vluchten niet laden. Probeer opnieuw te synchroniseren.";
            ActiveFlightDetails.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadUpcomingFlightsAsync(int fancierId)
    {
        try
        {
            var upcoming = await flightResultsReader.GetUpcomingFlightsAsync(fancierId);
            if (upcoming.Count > 0)
            {
                UpcomingFlightsGrid.ItemsSource = upcoming;
                UpcomingFlightsGrid.Visibility = Visibility.Visible;
                UpcomingFlightsStatusText.Text = $"{upcoming.Count} komende vlucht(en) gevonden. Klik op een vlucht om de prijzentabel te bekijken.";
                PrizeTableCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpcomingFlightsGrid.ItemsSource = null;
                UpcomingFlightsGrid.Visibility = Visibility.Collapsed;
                UpcomingFlightsStatusText.Text = "Geen komende vluchten gevonden. Synchroniseer om de laatste vluchtgegevens op te halen.";
            }
        }
        catch
        {
            UpcomingFlightsGrid.ItemsSource = null;
            UpcomingFlightsGrid.Visibility = Visibility.Collapsed;
            UpcomingFlightsStatusText.Text = "Kan komende vluchten niet laden. Probeer opnieuw te synchroniseren.";
        }
    }

    private async Task LoadCompletedFlightsAsync(int fancierId)
    {
        try
        {
            var completed = await flightResultsReader.GetCompletedFlightSummariesAsync(fancierId);
            if (completed.Count > 0)
            {
                CompletedFlightsGrid.ItemsSource = completed;
                CompletedFlightsGrid.Visibility = Visibility.Visible;
                CompletedFlightsStatusText.Text = $"{completed.Count} voltooide vlucht(en). Klik op een vlucht om de prijzentabel te bekijken.";
                CompletedPrizeTableCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompletedFlightsGrid.ItemsSource = null;
                CompletedFlightsGrid.Visibility = Visibility.Collapsed;
                CompletedFlightsStatusText.Text = "Nog geen voltooide vluchten. Voer een sync uit na afgelopen vluchten.";
            }
        }
        catch
        {
            CompletedFlightsGrid.ItemsSource = null;
            CompletedFlightsGrid.Visibility = Visibility.Collapsed;
            CompletedFlightsStatusText.Text = "Kan voltooide vluchten niet laden.";
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

        if (ProfileGrid.SelectedItem is PigeonDistanceProfile selected)
        {
            results = results.Where(r => r.PigeonId == selected.PigeonId);
        }

        if (CategoryFilterCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<DistanceCategory>(tag, out var category))
        {
            results = results.Where(r => r.Category == category);
        }

        ResultsGrid.ItemsSource = results.ToList();
    }

    private void UpcomingFlightsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UpcomingFlightsGrid.SelectedItem is UpcomingFlightInfo selected && selected.PrizeTable.Count > 0)
        {
            PrizeTableTitle.Text = $"Prijzentabel — {selected.Location} ({selected.FlightType}, {selected.Subscribers} deelnemers)";
            PrizeTableGrid.ItemsSource = selected.PrizeTable;
            PrizeTableCard.Visibility = Visibility.Visible;
        }
        else
        {
            PrizeTableCard.Visibility = Visibility.Collapsed;
        }
    }

    private void CompletedFlightsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompletedFlightsGrid.SelectedItem is CompletedFlightSummary selected && selected.PrizeTable.Count > 0)
        {
            CompletedPrizeTableTitle.Text = $"Prijzentabel — {selected.Location} ({selected.FlightType}, {selected.TotalParticipants} deelnemers)";
            CompletedPrizeTableGrid.ItemsSource = selected.PrizeTable;
            CompletedPrizeTableCard.Visibility = Visibility.Visible;
        }
        else
        {
            CompletedPrizeTableCard.Visibility = Visibility.Collapsed;
        }
    }

    private void CopyActiveFlightInfo_Click(object sender, RoutedEventArgs e)
    {
        if (currentActiveFlight is not { } flight) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Actieve vlucht: {flight.Location} ({flight.FlightType})");
        sb.AppendLine($"Datum: {flight.Start:dd-MM-yyyy HH:mm}");
        sb.AppendLine($"Afstand: {flight.DistanceKm} km ({flight.Category})");
        sb.AppendLine($"Deelnemers: {flight.Subscribers} | Voortgang: {flight.Progress}%");
        sb.AppendLine();
        AppendPrizeTable(sb, flight.PrizeTable);

        if (flight.PigeonStandings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Klassement per duif:");
            sb.AppendLine($"{"Pos",-5} {"Duif",-25} {"Melker",-20} {"Ptn",-6} {"Snelheid",-10} {"Voortgang",-10}");
            foreach (var p in flight.PigeonStandings)
                sb.AppendLine($"{p.Position,-5} {(p.PigeonName ?? ""),-25} {(p.FancierName ?? ""),-20} {p.Points,-6} {p.CurrentSpeed:0.00,-10} {p.Progress}%");
        }

        if (flight.FancierStandings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Klassement per melker:");
            sb.AppendLine($"{"Melker",-25} {"Ptn",-8} {"Duiven",-8} {"Beste pos.",-10}");
            foreach (var f in flight.FancierStandings)
                sb.AppendLine($"{f.FancierName,-25} {f.TotalPoints,-8} {f.PigeonCount,-8} {f.BestPosition,-10}");
        }

        Clipboard.SetText(sb.ToString());
    }

    private void CopyUpcomingFlightInfo_Click(object sender, RoutedEventArgs e)
    {
        if (UpcomingFlightsGrid.SelectedItem is not UpcomingFlightInfo flight) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Komende vlucht: {flight.Location} ({flight.FlightType})");
        sb.AppendLine($"Datum: {flight.Start:dd-MM-yyyy HH:mm}");
        sb.AppendLine($"Afstand: {flight.DistanceKm} km ({flight.Category})");
        sb.AppendLine($"Deelnemers: {flight.Subscribers} | Prijsposities: {flight.TotalPrizePositions}");
        sb.AppendLine();
        AppendPrizeTable(sb, flight.PrizeTable);

        Clipboard.SetText(sb.ToString());
    }

    private static void AppendPrizeTable(System.Text.StringBuilder sb, IReadOnlyList<PigeonFancierTracker.Core.Analytics.PrizeTier> prizeTable)
    {
        if (prizeTable.Count == 0) return;

        sb.AppendLine("Prijzentabel:");
        sb.AppendLine($"{"Posities",-12} {"Aantal",-8} {"Punten",-8}");
        foreach (var tier in prizeTable)
            sb.AppendLine($"{tier.RangeDisplay,-12} {tier.Count,-8} {tier.PointsPerPosition,-8}");
    }

    private async void DiagnosticsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsPanel.Visibility == Visibility.Visible)
        {
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DiagnosticsPanel.Visibility = Visibility.Visible;
        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int fid)
        {
            DiagnosticsText.Text = "Geen melker geselecteerd.";
            return;
        }

        try
        {
            DiagnosticsText.Text = "Diagnostiek laden...";
            var report = await flightResultsReader.GetFlightDiagnosticsAsync(fid);

            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"Queried FancierId: {report.QueriedFancierId}");
            lines.AppendLine();

            lines.AppendLine($"=== /api/flight/live snapshots ({report.LiveSnapshots.Count}) ===");
            foreach (var s in report.LiveSnapshots)
            {
                lines.AppendLine($"  Status={s.StatusCode}  Query=[{s.NormalizedQuery}]  Captured={s.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}  BodyLen={s.BodyLength}");
            }
            if (report.LiveSnapshots.Count == 0)
                lines.AppendLine("  (geen snapshots gevonden)");
            lines.AppendLine();

            lines.AppendLine($"=== /api/flight snapshots ({report.FlightSnapshots.Count}) ===");
            foreach (var s in report.FlightSnapshots)
            {
                lines.AppendLine($"  Status={s.StatusCode}  Query=[{s.NormalizedQuery}]  Captured={s.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}  BodyLen={s.BodyLength}");
            }
            if (report.FlightSnapshots.Count == 0)
                lines.AppendLine("  (geen snapshots gevonden)");
            lines.AppendLine();

            lines.AppendLine($"=== Best live snapshot (fancier-filtered) ===");
            lines.AppendLine($"Parsed flights: {report.ParsedLiveFlightsCount}");
            lines.AppendLine($"Filtered active (started + regional/national): {report.FilteredActiveCount}");
            if (report.ParseError is not null)
                lines.AppendLine($"PARSE ERROR: {report.ParseError}");
            lines.AppendLine();

            if (report.LiveSnapshotBodyPreview is not null)
            {
                lines.AppendLine("=== Live snapshot body preview ===");
                lines.AppendLine(report.LiveSnapshotBodyPreview);
                lines.AppendLine();
            }

            if (report.FlightSnapshotBodyPreview is not null)
            {
                lines.AppendLine("=== Flight snapshot body preview ===");
                lines.AppendLine(report.FlightSnapshotBodyPreview);
            }

            DiagnosticsText.Text = lines.ToString();
        }
        catch (Exception ex)
        {
            DiagnosticsText.Text = $"Diagnostiek fout: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private async void FoodAnalysisGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        if (e.Row.Item is not FoodMixPerformanceRow row)
            return;

        if (e.EditingElement is not TextBox textBox)
            return;

        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int fid)
            return;

        try
        {
            var comment = string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text.Trim();
            await flightResultsReader.SaveFoodCommentAsync(fid, row.Mix, comment);
        }
        catch
        {
        }
    }
}
