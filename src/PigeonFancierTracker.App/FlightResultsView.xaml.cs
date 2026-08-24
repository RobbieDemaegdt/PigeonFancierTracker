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
    private string currentSegment = "Active";

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

            // Open on the segment that actually has something to show.
            var initialSegment = currentActiveFlight is not null ? "Active"
                : UpcomingFlightsGrid.Items.Count > 0 ? "Upcoming"
                : CompletedFlightsGrid.Items.Count > 0 ? "Completed"
                : "Active";
            SelectSegment(initialSegment);

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

            LoadBreedAnalysis(currentData);

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

    private void LoadBreedAnalysis(FlightResultsPageData data)
    {
        if (data.BreedAnalysis is { Breeds.Count: > 0 } breedAnalysis)
        {
            var rows = BreedConditionRow.Flatten(breedAnalysis);
            BreedAnalysisGrid.ItemsSource = rows;
            BreedAnalysisStatus.Text = $"{breedAnalysis.Breeds.Count} ras(sen) geanalyseerd — gesorteerd op beste gemiddelde plaatsing.";
        }
        else
        {
            BreedAnalysisGrid.ItemsSource = null;
            BreedAnalysisStatus.Text = "Nog geen rasgegevens gekoppeld aan vluchten. Synchroniseer om resultaten en rassen op te bouwen.";
        }

        if (data.BreedSkillAnalysis is { Breeds.Count: > 0 } skillAnalysis)
        {
            var rows = BreedSkillRow.Flatten(skillAnalysis);
            BreedSkillGrid.ItemsSource = rows;
            BreedSkillStatus.Text = $"{skillAnalysis.Breeds.Count} ras(sen) — vaardigheden gesorteerd op sterkste positieve correlatie.";
        }
        else
        {
            BreedSkillGrid.ItemsSource = null;
            BreedSkillStatus.Text = "Nog geen vaardigheidsgegevens beschikbaar. Synchroniseer om duivenvaardigheden op te bouwen.";
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

                if (flight.AgeCategoryPrizes is { Count: > 0 } activeCats)
                {
                    // National flight: prizes are awarded per age category, so show the
                    // three category tables in place of the combined one.
                    ActivePrizeInfo.Text =
                        $"Nationale vlucht — prijzen per leeftijdscategorie · {flight.Subscribers} deelnemers totaal";
                    ActiveAgeCategoryItems.ItemsSource = activeCats.Select(AgeCategoryPrizeRow.From).ToList();
                    ActiveAgeCategoryPanel.Visibility = Visibility.Visible;
                    ActivePrizeTableGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ActivePrizeInfo.Text = $"{totalPrizes} prijsposities voor {flight.Subscribers} deelnemers — " +
                        $"Inschrijfgeld: EUR {flight.EntryPrice:0.00} — Prijzenpot: EUR {flight.TotalPrizePool:0.00}";
                    ActivePrizeTableGrid.ItemsSource = flight.PrizeTable;
                    ActivePrizeTableGrid.Visibility = Visibility.Visible;
                    ActiveAgeCategoryPanel.Visibility = Visibility.Collapsed;
                }
                ActivePigeonGrid.ItemsSource = flight.PigeonStandings;
                ActiveFancierGrid.ItemsSource = flight.FancierStandings;
                ActiveFlightDetails.Visibility = Visibility.Visible;
                KpiActiveFlightText.Text = string.IsNullOrWhiteSpace(flight.Location) ? "Actief" : flight.Location;
                KpiActiveFlightSubText.Text = $"bezig · {flight.Progress}%";
            }
            else
            {
                currentActiveFlight = null;
                ActiveFlightHeaderText.Text = "Geen actieve vlucht op dit moment. Synchroniseer als er een vlucht bezig is.";
                ActiveFlightDetails.Visibility = Visibility.Collapsed;
                KpiActiveFlightText.Text = "Geen";
                KpiActiveFlightSubText.Text = "";
            }
        }
        catch
        {
            currentActiveFlight = null;
            ActiveFlightHeaderText.Text = "Kan actieve vluchten niet laden. Probeer opnieuw te synchroniseren.";
            ActiveFlightDetails.Visibility = Visibility.Collapsed;
            KpiActiveFlightText.Text = "—";
            KpiActiveFlightSubText.Text = "";
        }
    }

    private async Task LoadUpcomingFlightsAsync(int fancierId)
    {
        try
        {
            var upcoming = await flightResultsReader.GetUpcomingFlightsAsync(fancierId);
            KpiUpcomingCountText.Text = upcoming.Count.ToString();
            if (upcoming.Count > 0)
            {
                UpcomingFlightsGrid.ItemsSource = upcoming;
                UpcomingFlightsGrid.Visibility = Visibility.Visible;
                UpcomingFlightsStatusText.Text = $"{upcoming.Count} komende vlucht(en). Klik op een vlucht om de prijzentabel te bekijken.";
                UpcomingFlightsGrid.SelectedIndex = 0;
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
            KpiUpcomingCountText.Text = "—";
        }
    }

    private async Task LoadCompletedFlightsAsync(int fancierId)
    {
        try
        {
            var completed = await flightResultsReader.GetCompletedFlightSummariesAsync(fancierId);
            KpiCompletedCountText.Text = completed.Count.ToString();
            if (completed.Count > 0)
            {
                CompletedFlightsGrid.ItemsSource = completed;
                CompletedFlightsGrid.Visibility = Visibility.Visible;
                CompletedFlightsStatusText.Text = $"{completed.Count} voltooide vlucht(en). Klik op een vlucht om de prijzentabel te bekijken.";
                var bestPosition = completed.Where(c => c.BestPosition > 0).Select(c => c.BestPosition).DefaultIfEmpty(0).Min();
                KpiBestPositionText.Text = bestPosition > 0 ? $"{bestPosition}e" : "—";
                CompletedFlightsGrid.SelectedIndex = 0;
            }
            else
            {
                CompletedFlightsGrid.ItemsSource = null;
                CompletedFlightsGrid.Visibility = Visibility.Collapsed;
                CompletedFlightsStatusText.Text = "Nog geen voltooide vluchten. Voer een sync uit na afgelopen vluchten.";
                KpiBestPositionText.Text = "—";
            }
        }
        catch
        {
            CompletedFlightsGrid.ItemsSource = null;
            CompletedFlightsGrid.Visibility = Visibility.Collapsed;
            CompletedFlightsStatusText.Text = "Kan voltooide vluchten niet laden.";
            KpiCompletedCountText.Text = "—";
            KpiBestPositionText.Text = "—";
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

    private void SegActive_Click(object sender, RoutedEventArgs e) => SelectSegment("Active");

    private void SegUpcoming_Click(object sender, RoutedEventArgs e) => SelectSegment("Upcoming");

    private void SegCompleted_Click(object sender, RoutedEventArgs e) => SelectSegment("Completed");

    private void SelectSegment(string segment)
    {
        currentSegment = segment;

        SegActiveButton.IsChecked = segment == "Active";
        SegUpcomingButton.IsChecked = segment == "Upcoming";
        SegCompletedButton.IsChecked = segment == "Completed";

        ActiveSegmentPanel.Visibility = segment == "Active" ? Visibility.Visible : Visibility.Collapsed;
        ListDetailPanel.Visibility = segment == "Active" ? Visibility.Collapsed : Visibility.Visible;

        UpcomingListContainer.Visibility = segment == "Upcoming" ? Visibility.Visible : Visibility.Collapsed;
        CompletedListContainer.Visibility = segment == "Completed" ? Visibility.Visible : Visibility.Collapsed;

        UpdateDetailPanels();
    }

    private void UpdateDetailPanels()
    {
        var showUpcoming = currentSegment == "Upcoming"
            && UpcomingFlightsGrid.SelectedItem is UpcomingFlightInfo up && up.PrizeTable.Count > 0;
        var showCompleted = currentSegment == "Completed"
            && CompletedFlightsGrid.SelectedItem is CompletedFlightSummary cp && cp.PrizeTable.Count > 0;

        PrizeTableCard.Visibility = showUpcoming ? Visibility.Visible : Visibility.Collapsed;
        CompletedPrizeTableCard.Visibility = showCompleted ? Visibility.Visible : Visibility.Collapsed;
        DetailPlaceholderText.Visibility =
            currentSegment != "Active" && !showUpcoming && !showCompleted
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpcomingFlightsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UpcomingFlightsGrid.SelectedItem is UpcomingFlightInfo selected)
        {
            PrizeTableTitle.Text = $"Prijzentabel — {selected.Location}";
            UpcomingDetailMeta.Text = $"{selected.FlightType} · {selected.DistanceKm} km ({selected.Category}) · " +
                $"{selected.Subscribers} deelnemers · {selected.TotalPrizePositions} prijsposities";
            PrizeTableGrid.ItemsSource = selected.PrizeTable;
        }
        UpdateDetailPanels();
    }

    private void CompletedFlightsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompletedFlightsGrid.SelectedItem is CompletedFlightSummary selected)
        {
            CompletedPrizeTableTitle.Text = $"Prijzentabel — {selected.Location}";
            CompletedDetailMeta.Text = $"{selected.FlightType} · {selected.DistanceKm} km ({selected.Category}) · " +
                $"{selected.TotalParticipants} deelnemers · {selected.OwnPigeonCount} eigen duiven · " +
                $"beste {selected.BestPosition}e · {selected.TotalPoints} ptn";

            if (selected.AgeCategoryPrizes is { Count: > 0 } completedCats)
            {
                CompletedAgeCategoryItems.ItemsSource = completedCats.Select(AgeCategoryPrizeRow.From).ToList();
                CompletedAgeCategoryItems.Visibility = Visibility.Visible;
                CompletedPrizeTableGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompletedPrizeTableGrid.ItemsSource = selected.PrizeTable;
                CompletedPrizeTableGrid.Visibility = Visibility.Visible;
                CompletedAgeCategoryItems.Visibility = Visibility.Collapsed;
            }
        }
        UpdateDetailPanels();
    }

    private void CopyActiveFlightInfo_Click(object sender, RoutedEventArgs e)
    {
        if (currentActiveFlight is not { } flight) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("```");
        sb.AppendLine($"Actieve vlucht: {flight.Location} ({flight.FlightType})");
        sb.AppendLine($"Deelnemers: {flight.Subscribers} | Voortgang: {flight.Progress}%");
        sb.AppendLine();
        if (flight.AgeCategoryPrizes is { Count: > 0 } cats)
            AppendAgeCategoryPrizes(sb, cats);
        else
            AppendPrizeTable(sb, flight.PrizeTable);

        if (flight.PigeonStandings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Klassement per duif:");
            AppendTable(sb,
                new[] { "Pos", "Duif", "Melker", "Ptn", "Snelheid", "Voortgang" },
                flight.PigeonStandings.Select(p => new[]
                {
                    p.Position.ToString(),
                    p.PigeonName ?? "",
                    p.FancierName ?? "",
                    p.Points.ToString(),
                    p.CurrentSpeed.ToString("0.00"),
                    $"{p.Progress}%",
                }),
                rightAlign: new[] { true, false, false, true, true, true });
        }

        if (flight.FancierStandings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Klassement per melker:");
            AppendTable(sb,
                new[] { "Melker", "Ptn", "Duiven", "Beste pos." },
                flight.FancierStandings.Select(f => new[]
                {
                    f.FancierName ?? "",
                    f.TotalPoints.ToString(),
                    f.PigeonCount.ToString(),
                    f.BestPosition.ToString(),
                }),
                rightAlign: new[] { false, true, true, true });
        }

        sb.AppendLine("```");
        Clipboard.SetText(sb.ToString());
    }

    private void CopyUpcomingFlightInfo_Click(object sender, RoutedEventArgs e)
    {
        if (UpcomingFlightsGrid.SelectedItem is not UpcomingFlightInfo flight) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("```");
        sb.AppendLine($"Komende vlucht: {flight.Location} ({flight.FlightType})");
        sb.AppendLine($"Deelnemers: {flight.Subscribers} | Prijsposities: {flight.TotalPrizePositions}");
        sb.AppendLine();
        AppendPrizeTable(sb, flight.PrizeTable);

        sb.AppendLine("```");
        Clipboard.SetText(sb.ToString());
    }

    private static void AppendAgeCategoryPrizes(
        System.Text.StringBuilder sb,
        IReadOnlyList<AgeCategoryPrizeInfo> categories)
    {
        foreach (var category in categories)
        {
            sb.AppendLine(AgeCategoryPrizeRow.From(category).HeaderText);
            AppendTable(sb,
                new[] { "Posities", "Aantal", "Punten", "Prijs" },
                category.PrizeTable.Select(tier => new[]
                {
                    tier.RangeDisplay ?? "",
                    tier.Count.ToString(),
                    tier.PointsPerPosition.ToString(),
                    $"EUR {tier.PrizeMoneyPerPosition:0.00}",
                }),
                rightAlign: new[] { false, true, true, true });
            sb.AppendLine();
        }
    }

    private static void AppendPrizeTable(System.Text.StringBuilder sb, IReadOnlyList<PigeonFancierTracker.Core.Analytics.PrizeTier> prizeTable)
    {
        if (prizeTable.Count == 0) return;

        sb.AppendLine("Prijzentabel:");
        AppendTable(sb,
            new[] { "Posities", "Aantal", "Punten" },
            prizeTable.Select(tier => new[]
            {
                tier.RangeDisplay ?? "",
                tier.Count.ToString(),
                tier.PointsPerPosition.ToString(),
            }),
            rightAlign: new[] { false, true, true });
    }

    /// <summary>
    /// Writes a monospace-aligned table whose column widths are computed from the
    /// actual header/cell contents, so no value ever overflows its column. A
    /// separator row is drawn under the header. Columns are single-space separated
    /// and trailing padding is trimmed on the last column to keep pasted output clean.
    /// </summary>
    private static void AppendTable(
        System.Text.StringBuilder sb,
        string[] headers,
        IEnumerable<string[]> rows,
        bool[] rightAlign)
    {
        var allRows = new List<string[]> { headers };
        allRows.AddRange(rows);

        var widths = new int[headers.Length];
        foreach (var row in allRows)
            for (int c = 0; c < headers.Length; c++)
                widths[c] = System.Math.Max(widths[c], (row[c] ?? "").Length);

        string FormatRow(string[] row)
        {
            var cells = new string[headers.Length];
            for (int c = 0; c < headers.Length; c++)
            {
                var value = row[c] ?? "";
                cells[c] = rightAlign[c] ? value.PadLeft(widths[c]) : value.PadRight(widths[c]);
            }
            return string.Join(" | ", cells).TrimEnd();
        }

        sb.AppendLine(FormatRow(headers));
        sb.AppendLine(string.Join("-+-", widths.Select(w => new string('-', w))));
        foreach (var row in allRows.Skip(1))
            sb.AppendLine(FormatRow(row));
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
