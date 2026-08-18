using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class SponsorView : UserControl
{
    private readonly ISponsorDataReader sponsorDataReader;
    private readonly ISessionStateService sessionState;

    public SponsorView(
        ISponsorDataReader sponsorDataReader,
        ISessionStateService sessionState)
    {
        InitializeComponent();
        this.sponsorDataReader = sponsorDataReader;
        this.sessionState = sessionState;
    }

    public async Task RefreshAsync()
    {
        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
        {
            SponsorStatusText.Text = "Selecteer een melker voordat je sponsoring bekijkt.";
            return;
        }

        try
        {
            SponsorStatusText.Text = "Sponsorgegevens laden...";
            var overview = await sponsorDataReader.GetSponsorOverviewAsync(selectedFancierId);

            TotalMonthlyText.Text = FormatCurrency(overview.TotalMonthlyIncome);
            HistAvgText.Text = FormatCurrency(overview.HistoricalAvgMonthly);
            TrendText.Text = $"{overview.OfferTrendPercent:+0.0;-0.0;0.0}% ({overview.OfferTrendDirection})";
            CanCallText.Text = overview.CanCallSponsors ? "Beschikbaar" : "Niet beschikbaar";

            PopulateSlots(overview);
            PopulatePendingOffers(overview);
            PopulateHistory(overview);

            SponsorStatusText.Text = overview.ActiveSponsors.Count == 0 && overview.PendingOffers.Count == 0
                ? "Nog geen sponsorgegevens beschikbaar. Voer een sync uit om gegevens op te halen."
                : "";
        }
        catch (Exception ex)
        {
            SponsorStatusText.Text = $"Sponsorgegevens konden niet worden geladen: {ex.Message}";
        }
    }

    private void PopulateSlots(SponsorOverview overview)
    {
        var slots = new[]
        {
            (Slot1Title, Slot1Monthly, Slot1Direct, Slot1Runtime, Slot1Total, Slot1End, Slot1Score),
            (Slot2Title, Slot2Monthly, Slot2Direct, Slot2Runtime, Slot2Total, Slot2End, Slot2Score),
            (Slot3Title, Slot3Monthly, Slot3Direct, Slot3Runtime, Slot3Total, Slot3End, Slot3Score),
        };

        for (int i = 0; i < slots.Length; i++)
        {
            var (title, monthly, direct, runtime, total, end, score) = slots[i];
            if (i < overview.ScoredSponsors.Count)
            {
                var s = overview.ScoredSponsors[i];
                title.Text = $"Sponsor #{s.Sponsor.SponsorId}";
                monthly.Text = $"Maandelijks: {FormatCurrency(s.Sponsor.Monthly)}";
                direct.Text = $"Tekenpremie: {FormatCurrency(s.Sponsor.Direct)}";
                runtime.Text = $"Looptijd: {s.Sponsor.RuntimeRemaining}/{s.Sponsor.Runtime} maanden resterend";
                total.Text = $"Totaal verdiend: {FormatCurrency(s.Sponsor.Total)}";
                end.Text = s.Sponsor.ContractEnd is DateTimeOffset endDate
                    ? $"Eindigt: {endDate.ToLocalTime():d MMM yyyy}"
                    : "";
                score.Text = $"Score: {s.Score.Score:0} — {s.Score.Verdict}";
            }
            else
            {
                title.Text = "Vrij slot";
                monthly.Text = "";
                direct.Text = "";
                runtime.Text = "";
                total.Text = "";
                end.Text = "";
                score.Text = "";
            }
        }
    }

    private void PopulatePendingOffers(SponsorOverview overview)
    {
        if (overview.PendingOffers.Count == 0)
        {
            PendingOffersCard.Visibility = Visibility.Collapsed;
            return;
        }

        PendingOffersCard.Visibility = Visibility.Visible;
        var rows = overview.PendingOffers.Select(s => new SponsorOfferRow
        {
            SponsorId = s.Sponsor.SponsorId,
            MonthlyDisplay = FormatCurrency(s.Sponsor.Monthly),
            DirectDisplay = FormatCurrency(s.Sponsor.Direct),
            RuntimeDisplay = $"{s.Sponsor.Runtime} maanden",
            TotalValueDisplay = FormatCurrency(SponsorDealScorer.CalculateTotalContractValue(
                s.Sponsor.Monthly, s.Sponsor.Direct, s.Sponsor.Runtime)),
            ScoreDisplay = $"{s.Score.Score:0}",
            Verdict = s.Score.Verdict,
            Recommendation = s.Score.Recommendation,
        }).ToList();

        PendingOffersGrid.ItemsSource = rows;
    }

    private void PopulateHistory(SponsorOverview overview)
    {
        var allScored = overview.ScoredSponsors.Concat(overview.PendingOffers).Concat(overview.ExpiredSponsors);
        var rows = allScored.Select(s => new SponsorHistoryRow
        {
            SponsorId = s.Sponsor.SponsorId,
            MonthlyDisplay = FormatCurrency(s.Sponsor.Monthly),
            DirectDisplay = FormatCurrency(s.Sponsor.Direct),
            RuntimeDisplay = $"{s.Sponsor.RuntimeRemaining}/{s.Sponsor.Runtime}",
            TotalDisplay = FormatCurrency(s.Sponsor.Total),
            EndDisplay = s.Sponsor.ContractEnd is DateTimeOffset endDate
                ? endDate.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                : "—",
            AvgPerMonthDisplay = s.Sponsor.Runtime > 0
                ? FormatCurrency(s.Sponsor.Direct / s.Sponsor.Runtime + s.Sponsor.Monthly)
                : "—",
            ScoreDisplay = $"{s.Score.Score:0}",
            Verdict = s.Score.Verdict,
            Recommendation = s.Score.Recommendation,
        }).ToList();

        SponsorHistoryGrid.ItemsSource = rows;
    }

    private static string FormatCurrency(decimal value) =>
        value.ToString("C0", CultureInfo.CurrentCulture);
}

public sealed class SponsorOfferRow
{
    public int SponsorId { get; set; }
    public string MonthlyDisplay { get; set; } = "";
    public string DirectDisplay { get; set; } = "";
    public string RuntimeDisplay { get; set; } = "";
    public string TotalValueDisplay { get; set; } = "";
    public string ScoreDisplay { get; set; } = "";
    public string Verdict { get; set; } = "";
    public string Recommendation { get; set; } = "";
}

public sealed class SponsorHistoryRow
{
    public int SponsorId { get; set; }
    public string MonthlyDisplay { get; set; } = "";
    public string DirectDisplay { get; set; } = "";
    public string RuntimeDisplay { get; set; } = "";
    public string TotalDisplay { get; set; } = "";
    public string EndDisplay { get; set; } = "";
    public string AvgPerMonthDisplay { get; set; } = "";
    public string ScoreDisplay { get; set; } = "";
    public string Verdict { get; set; } = "";
    public string Recommendation { get; set; } = "";
}
