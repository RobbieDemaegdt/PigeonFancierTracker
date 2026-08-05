using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class TransferDetailControl : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler<TransferListItem>? AddToAutoBidRequested;

    public TransferDetailControl()
    {
        InitializeComponent();
    }

    public void ShowDetail(TransferListItem item, bool showAutoBid)
    {
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
        SelectBestWindow(PriceEstimateGrid, item.PriceEstimates);
        SelectBestPercentileWindow(MarketPercentileGrid, item.MarketPercentiles);
        SelectBestPercentileWindow(FlockPercentileGrid, item.FlockPercentiles);

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
        // Delegate validation to parent
        AddToAutoBidRequested?.Invoke(this, null!);
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

    private static void SelectBestWindow(DataGrid grid, IReadOnlyList<WindowPriceRow>? rows)
    {
        var best = rows?
            .Where(r => r.EstimatedPrice.HasValue)
            .OrderByDescending(r => r.ComparableCount)
            .FirstOrDefault();
        grid.SelectedItem = best;
    }

    private static void SelectBestPercentileWindow(DataGrid grid, IReadOnlyList<WindowPercentileRow>? rows)
    {
        var best = rows?
            .Where(r => r.PopulationCount > 0)
            .OrderByDescending(r => r.PopulationCount)
            .FirstOrDefault();
        grid.SelectedItem = best;
    }
}
