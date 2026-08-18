using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.App;

public partial class TransferView : UserControl
{
    private readonly ITransferDataReader transferDataReader;
    private readonly ISessionStateService sessionState;
    private readonly IAutoBidService autoBidService;
    private readonly IMarketAnalysisReader marketAnalysisReader;
    private readonly ISyncCoordinator syncCoordinator;

    public TransferView(
        ITransferDataReader transferDataReader,
        ISessionStateService sessionState,
        IAutoBidService autoBidService,
        IMarketAnalysisReader marketAnalysisReader,
        ISyncCoordinator syncCoordinator)
    {
        InitializeComponent();
        this.transferDataReader = transferDataReader;
        this.sessionState = sessionState;
        this.autoBidService = autoBidService;
        this.marketAnalysisReader = marketAnalysisReader;
        this.syncCoordinator = syncCoordinator;
        autoBidService.EntryChanged += AutoBidService_EntryChanged;
        autoBidService.LogMessage += AutoBidService_LogMessage;
        ActiveDetailPanel.CloseRequested += (_, _) => ActiveTransferGrid.SelectedItem = null;
        ActiveDetailPanel.AddToAutoBidRequested += ActiveDetailPanel_AddToAutoBid;
        CompletedDetailPanel.CloseRequested += (_, _) => CompletedTransferGrid.SelectedItem = null;
        Loaded += TransferView_Loaded;
    }

    public async Task RefreshAsync()
    {
        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
        {
            TransferStatusText.Text = "Selecteer een melker voordat je transfers bekijkt.";
            return;
        }

        try
        {
            TransferStatusText.Text = "Lokale transfergegevens laden…";
            var data = await transferDataReader.GetTransferDataAsync(selectedFancierId);

            ActiveTransferGrid.ItemsSource = data.ActiveTransfers;
            ActiveTransferCountText.Text = data.ActiveTransfers.Count == 0
                ? "Geen actieve transfers gevonden in lokale momentopnames."
                : $"{data.ActiveTransfers.Count} actieve transfer(s).";

            CompletedTransferGrid.ItemsSource = data.CompletedTransfers;
            CompletedTransferCountText.Text = data.CompletedTransfers.Count == 0
                ? "Nog geen voltooide transfers gedetecteerd. Voltooide transfers verschijnen wanneer een aanbieding verdwijnt tussen syncs."
                : $"{data.CompletedTransfers.Count} voltooide transfer(s).";

            ActiveDetailPanel.HideDetail();
            CompletedDetailPanel.HideDetail();

            PopulateMarketAnalysis(data);

            try
            {
                var analysis = await marketAnalysisReader.GetMarketAnalysisAsync(selectedFancierId, data);
                PopulateMarketAnalysisExtended(analysis);
            }
            catch
            {
                BuyRecommendationCountText.Text = "Marktanalyse kon niet worden berekend.";
                SellEstimateCountText.Text = "Verkoopwaarde kon niet worden berekend.";
            }

            var total = data.ActiveTransfers.Count + data.CompletedTransfers.Count;
            TransferStatusText.Text = total == 0
                ? "Geen transfergegevens beschikbaar. Voer een sync uit om transferaanbiedingen vast te leggen."
                : $"{total} transfer(s) uit lokale momentopnames; er is geen netwerkverzoek gedaan.";
        }
        catch (Exception exception)
        {
            TransferStatusText.Text = $"Transfers konden niet worden geladen: {exception.Message}";
            ActiveTransferGrid.ItemsSource = null;
            CompletedTransferGrid.ItemsSource = null;
        }

        RefreshAutoBidGrid();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            TransferStatusText.Text = "Gegevens ophalen van server…";
            await syncCoordinator.SyncAsync(SyncProfile.Quick);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            TransferStatusText.Text = $"Vernieuwen mislukt: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ActiveTransferGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveTransferGrid.SelectedItem is TransferListItem item)
            ActiveDetailPanel.ShowDetail(item, showAutoBid: true);
        else
            ActiveDetailPanel.HideDetail();
    }

    private void CompletedTransferGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompletedTransferGrid.SelectedItem is TransferListItem item)
            CompletedDetailPanel.ShowDetail(item, showAutoBid: false);
        else
            CompletedDetailPanel.HideDetail();
    }

    private async void SaveRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TransferListItem item } button)
            return;

        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
            return;

        var row = FindVisualParent<DataGridRow>(button);
        if (row is null)
            return;

        var changed = false;

        var statusCombo = FindVisualChildByTag<ComboBox>(row, "StatusEditor");
        if (statusCombo?.SelectedItem is string selectedStatus && selectedStatus != item.TimeRemaining)
        {
            var newStatus = selectedStatus == "Sold" ? TransferStatus.Sold : TransferStatus.Expired;
            await transferDataReader.UpdateTransferStatusAsync(selectedFancierId, item.TransferId, newStatus);
            changed = true;
        }

        var priceBox = FindVisualChildByTag<TextBox>(row, "PriceEditor");
        if (priceBox is not null)
        {
            const NumberStyles priceStyles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
            var text = priceBox.Text?.Trim();
            decimal? newPrice = decimal.TryParse(text, priceStyles, CultureInfo.CurrentCulture, out var parsed)
                ? parsed
                : decimal.TryParse(text, priceStyles, CultureInfo.InvariantCulture, out parsed)
                    ? parsed
                    : null;

            if (newPrice != item.SoldPrice)
            {
                await transferDataReader.UpdateTransferSoldPriceAsync(selectedFancierId, item.TransferId, newPrice);
                changed = true;
            }
        }

        var buyerBox = FindVisualChildByTag<TextBox>(row, "BuyerEditor");
        if (buyerBox is not null)
        {
            var newBuyer = buyerBox.Text?.Trim();
            if (newBuyer != item.Buyer)
            {
                await transferDataReader.UpdateTransferBuyerAsync(selectedFancierId, item.TransferId, newBuyer);
                changed = true;
            }
        }

        if (changed)
            await RefreshAsync();
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child is not null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is T result)
                return result;
        }
        return null;
    }

    private static T? FindVisualChildByTag<T>(DependencyObject parent, string tag) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Tag as string == tag)
                return fe;

            var found = FindVisualChildByTag<T>(child, tag);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void PopulateMarketAnalysisExtended(MarketAnalysisPageData analysis)
    {
        MarketKoopjeCountText.Text = analysis.KoopjeCount.ToString();
        MarketTeDuurCountText.Text = analysis.TeDuurCount.ToString();
        MarketAvgSkillPerEuroText.Text = analysis.AvgSkillPerEuro > 0
            ? $"{analysis.AvgSkillPerEuro:0.00}"
            : "—";
        MarketTopBargainText.Text = analysis.TopBargainName ?? "—";

        if (analysis.BuyRecommendations.Count > 0)
        {
            BuyRecommendationGrid.ItemsSource = analysis.BuyRecommendations;
            BuyRecommendationCountText.Text = $"{analysis.BuyRecommendations.Count} duif(en) geanalyseerd.";
        }
        else
        {
            BuyRecommendationGrid.ItemsSource = null;
            BuyRecommendationCountText.Text = "Geen actieve transfers om te analyseren of onvoldoende verkoophistorie.";
        }

        if (analysis.SellEstimates.Count > 0)
        {
            SellEstimateGrid.ItemsSource = analysis.SellEstimates;
            SellEstimateCountText.Text = $"{analysis.SellEstimates.Count} eigen duif(en) gewaardeerd.";
        }
        else
        {
            SellEstimateGrid.ItemsSource = null;
            SellEstimateCountText.Text = "Geen verkoopwaarde beschikbaar — onvoldoende verkoophistorie.";
        }
    }

    private void PopulateMarketAnalysis(TransferPageData data)
    {
        if (data.MarketTrend is { } trend)
        {
            MarketCurrentPriceText.Text = trend.CurrentAvgPricePerSkill > 0
                ? $"€{trend.CurrentAvgPricePerSkill:N2}"
                : "—";
            MarketPreviousPriceText.Text = trend.PreviousAvgPricePerSkill > 0
                ? $"€{trend.PreviousAvgPricePerSkill:N2}"
                : "—";
            MarketTrendDirectionText.Text = trend.TrendDirection;
            MarketTrendPercentageText.Text = trend.TrendPercentage != 0
                ? $"{trend.TrendPercentage:+0.0;-0.0}%"
                : "—";
        }
        else
        {
            MarketCurrentPriceText.Text = "—";
            MarketPreviousPriceText.Text = "—";
            MarketTrendDirectionText.Text = "Onvoldoende data";
            MarketTrendPercentageText.Text = "—";
        }

        if (data.AuctionTiming is { } timing && timing.TimeSlots.Count > 0)
        {
            AuctionBestSlotText.Text = timing.BestTimeSlot ?? "—";
            AuctionWorstSlotText.Text = timing.WorstTimeSlot ?? "—";
            AuctionTimingGrid.ItemsSource = timing.TimeSlots;
        }
        else
        {
            AuctionBestSlotText.Text = "—";
            AuctionWorstSlotText.Text = "—";
            AuctionTimingGrid.ItemsSource = null;
        }
    }

    private void ActiveDetailPanel_AddToAutoBid(object? sender, TransferListItem _)
    {
        if (ActiveTransferGrid.SelectedItem is not TransferListItem item)
        {
            AutoBidLogText.Text = "Selecteer eerst een actieve transfer.";
            return;
        }

        if (!decimal.TryParse(ActiveDetailPanel.AutoBidMaxPriceText, out var maxPrice) || maxPrice <= 0)
        {
            AutoBidLogText.Text = "Voer een geldige max prijs in.";
            return;
        }

        if (item.CurrentPrice.HasValue && maxPrice <= item.CurrentPrice.Value)
        {
            AutoBidLogText.Text = $"Max prijs moet hoger zijn dan de huidige prijs ({item.CurrentPrice.Value}).";
            return;
        }

        autoBidService.AddTransfer(item.TransferId, item.PigeonName, item.CurrentPrice ?? 0, null, maxPrice);
        RefreshAutoBidGrid();
    }

    private void AutoBidToggle_Click(object sender, RoutedEventArgs e)
    {
        if (autoBidService.IsRunning)
        {
            autoBidService.Stop();
            AutoBidToggleButton.Content = "Starten";
        }
        else
        {
            var fancierId = sessionState.Current.SelectedFancier?.Id;
            if (fancierId is not int fid)
            {
                AutoBidLogText.Text = "Log eerst in en selecteer een melker.";
                return;
            }

            autoBidService.Start(fid);
            AutoBidToggleButton.Content = "Stoppen";
        }
    }

    private void AutoBidService_EntryChanged(AutoBidEntry entry) => Dispatcher.Invoke(RefreshAutoBidGrid);

    private void AutoBidService_LogMessage(string message) => Dispatcher.Invoke(() => AutoBidLogText.Text = message);

    private void RefreshAutoBidGrid()
    {
        var entries = autoBidService.Entries;
        if (entries.Count > 0)
        {
            AutoBidGrid.ItemsSource = null;
            AutoBidGrid.ItemsSource = entries;
            AutoBidGrid.Visibility = Visibility.Visible;
        }
        else
        {
            AutoBidGrid.ItemsSource = null;
            AutoBidGrid.Visibility = Visibility.Collapsed;
        }
    }

    private async void RecheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TransferListItem item })
            return;

        var fancierId = sessionState.Current.SelectedFancier?.Id;
        if (fancierId is not int selectedFancierId)
            return;

        var upgraded = await transferDataReader.RecheckTransferStatusAsync(selectedFancierId, item.TransferId);
        if (upgraded)
        {
            await RefreshAsync();
            ShowRecheckMessage($"Transfer \"{item.PigeonName}\" bijgewerkt naar Verkocht.");
        }
        else
        {
            ShowRecheckMessage("Geen verkoopgegevens gevonden voor deze transfer.");
        }
    }

    private void ShowRecheckMessage(string message)
    {
        RecheckStatusMessage.Text = message;
        RecheckStatusMessage.Visibility = Visibility.Visible;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            RecheckStatusMessage.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    private async void TransferView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= TransferView_Loaded;
        await RefreshAsync();
    }
}

public sealed class ExpiredStatusToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TransferStatus.Expired ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
