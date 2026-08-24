using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class TransferDetailControl : UserControl
{
    private TransferListItem? currentItem;

    public event EventHandler? CloseRequested;
    public event EventHandler<TransferListItem>? AddToAutoBidRequested;

    public TransferDetailControl()
    {
        InitializeComponent();
    }

    public void ShowDetail(TransferListItem item, bool showAutoBid)
    {
        currentItem = item;
        DetailTitle.Text = $"Detail: {item.PigeonName}";
        DetailForm.Text = item.FormDisplay ?? "—";
        DetailExperience.Text = item.ExperienceDisplay ?? "—";
        DetailStamina.Text = item.StaminaDisplay ?? "—";
        DetailSpeed.Text = item.SpeedDisplay ?? "—";
        DetailNavigation.Text = item.NavigationDisplay ?? "—";
        DetailTechnique.Text = item.TechniqueDisplay ?? "—";
        DetailAerodynamics.Text = item.AerodynamicsDisplay ?? "—";
        DetailIntelligence.Text = item.IntelligenceDisplay ?? "—";
        DetailLibido.Text = item.LibidoDisplay ?? "—";
        DetailNightvision.Text = item.NightvisionDisplay ?? "—";

        MarketPercentileGrid.ItemsSource = item.MarketPercentiles;
        FlockPercentileGrid.ItemsSource = item.FlockPercentiles;
        PriceEstimateGrid.ItemsSource = item.PriceEstimates;

        // Bind the dependent sub-tables directly so they are populated on open,
        // independent of whether the SelectionChanged side-effect reaches a
        // realized visual tree (the page may still be collapsed at this point).
        var bestPrice = SelectBestWindow(PriceEstimateGrid, item.PriceEstimates);
        ComparablesGrid.ItemsSource = bestPrice?.Comparables;

        var bestMarket = SelectBestPercentileWindow(MarketPercentileGrid, item.MarketPercentiles);
        MarketPopulationGrid.ItemsSource = bestMarket?.Population;

        var bestFlock = SelectBestPercentileWindow(FlockPercentileGrid, item.FlockPercentiles);
        FlockPopulationGrid.ItemsSource = bestFlock?.Population;

        AutoBidPanel.Visibility = showAutoBid ? Visibility.Visible : Visibility.Collapsed;
        if (showAutoBid && item.CurrentPrice.HasValue)
            AutoBidMaxPriceInput.Text = ((int)Math.Ceiling(item.CurrentPrice.Value * 2m)).ToString();

        Visibility = Visibility.Visible;
    }

    public void HideDetail()
    {
        Visibility = Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideDetail();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddToAutoBid_Click(object sender, RoutedEventArgs e)
    {
        if (currentItem is not null)
            AddToAutoBidRequested?.Invoke(this, currentItem);
    }

    public string AutoBidMaxPriceText => AutoBidMaxPriceInput.Text;

    private void MarketPercentileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarketPopulationGrid.ItemsSource =
            (MarketPercentileGrid.SelectedItem as WindowPercentileRow)?.Population;
    }

    private void FlockPercentileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FlockPopulationGrid.ItemsSource =
            (FlockPercentileGrid.SelectedItem as WindowPercentileRow)?.Population;
    }

    private void PriceEstimateGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComparablesGrid.ItemsSource = (PriceEstimateGrid.SelectedItem as WindowPriceRow)?.Comparables;
    }

    private static WindowPriceRow? SelectBestWindow(DataGrid grid, IReadOnlyList<WindowPriceRow>? rows)
    {
        var best = rows?
            .Where(r => r.EstimatedPrice.HasValue)
            .OrderByDescending(r => r.ComparableCount)
            .FirstOrDefault();
        grid.SelectedItem = best;
        return best;
    }

    private static WindowPercentileRow? SelectBestPercentileWindow(DataGrid grid, IReadOnlyList<WindowPercentileRow>? rows)
    {
        var best = rows?
            .Where(r => r.PopulationCount > 0)
            .OrderByDescending(r => r.PopulationCount)
            .FirstOrDefault();
        grid.SelectedItem = best;
        return best;
    }
}
