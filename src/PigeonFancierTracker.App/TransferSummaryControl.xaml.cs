using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class TransferSummaryControl : UserControl
{
    private TransferListItem? currentItem;

    public TransferSummaryControl()
    {
        InitializeComponent();
        StatusInput.ItemsSource = new[] { TransferStatus.Sold, TransferStatus.Expired };
    }

    public event EventHandler<TransferListItem>? OpenDetailsRequested;

    public event EventHandler<TransferListItem>? AddToAutoBidRequested;

    public event EventHandler<TransferSummarySaveRequestedEventArgs>? SaveRequested;

    public event EventHandler<TransferListItem>? RecheckRequested;

    public string AutoBidMaxPriceText => AutoBidMaxPriceInput.Text;

    public void ShowSummary(TransferListItem item, bool showAutoBid, bool allowEdit)
    {
        currentItem = item;
        SummaryEyebrowText.Text = item.Status switch
        {
            TransferStatus.Active => "ACTIEVE TRANSFER",
            TransferStatus.Sold => "VERKOCHT",
            _ => "VERLOPEN",
        };
        SummaryTitleText.Text = item.PigeonName;
        SummaryIdentityText.Text = $"ID {item.PigeonId?.ToString(CultureInfo.CurrentCulture) ?? "—"} · {item.Sex ?? "Geslacht onbekend"} · {item.Age ?? "Leeftijd onbekend"} · {item.Breed ?? "Ras onbekend"}";
        SummaryPriceText.Text = (item.SoldPrice ?? item.CurrentPrice)?.ToString("C0", CultureInfo.CurrentCulture) ?? "—";
        SummaryEstimateText.Text = item.BestEstimateDisplay ?? "Onvoldoende data";
        SummaryMarketText.Text = $"{item.BidCount} biedingen · {item.Seller ?? "Onbekende verkoper"} → {item.Buyer ?? item.SoldTo ?? "Nog geen koper"} · {item.TimeRemaining}";
        SummarySkillsText.Text = $"Totaal {item.TotalSkillDisplay ?? "—"} · Kort {item.ShortDisplay ?? "—"} · Midden {item.MediumDisplay ?? "—"} · Lang {item.LongDisplay ?? "—"}";

        AutoBidPanel.Visibility = showAutoBid ? Visibility.Visible : Visibility.Collapsed;
        if (showAutoBid && item.CurrentPrice.HasValue)
            AutoBidMaxPriceInput.Text = Math.Ceiling(item.CurrentPrice.Value * 2m).ToString(CultureInfo.CurrentCulture);

        CompletedEditPanel.Visibility = allowEdit ? Visibility.Visible : Visibility.Collapsed;
        if (allowEdit)
        {
            SoldPriceInput.Text = (item.SoldPrice ?? item.CurrentPrice)?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            BuyerInput.Text = item.Buyer ?? item.SoldTo ?? string.Empty;
            StatusInput.SelectedItem = item.Status is TransferStatus.Sold or TransferStatus.Expired
                ? item.Status
                : TransferStatus.Expired;
            RecheckButton.Visibility = item.Status == TransferStatus.Expired ? Visibility.Visible : Visibility.Collapsed;
        }

        OpenDetailsButton.IsEnabled = true;
    }

    public void ShowEmpty(string message)
    {
        currentItem = null;
        SummaryEyebrowText.Text = "TRANSFER";
        SummaryTitleText.Text = "Geen transfer geselecteerd";
        SummaryIdentityText.Text = message;
        SummaryPriceText.Text = "—";
        SummaryEstimateText.Text = "—";
        SummaryMarketText.Text = "—";
        SummarySkillsText.Text = "—";
        AutoBidPanel.Visibility = Visibility.Collapsed;
        CompletedEditPanel.Visibility = Visibility.Collapsed;
        OpenDetailsButton.IsEnabled = false;
    }

    private void OpenDetails_Click(object sender, RoutedEventArgs e)
    {
        if (currentItem is not null)
            OpenDetailsRequested?.Invoke(this, currentItem);
    }

    private void AddToAutoBid_Click(object sender, RoutedEventArgs e)
    {
        if (currentItem is not null)
            AddToAutoBidRequested?.Invoke(this, currentItem);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (currentItem is not null && StatusInput.SelectedItem is TransferStatus status)
        {
            SaveRequested?.Invoke(
                this,
                new TransferSummarySaveRequestedEventArgs(currentItem, SoldPriceInput.Text, BuyerInput.Text, status));
        }
    }

    private void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (currentItem is not null)
            RecheckRequested?.Invoke(this, currentItem);
    }
}

public sealed class TransferSummarySaveRequestedEventArgs : EventArgs
{
    public TransferSummarySaveRequestedEventArgs(
        TransferListItem item,
        string soldPriceText,
        string buyer,
        TransferStatus status)
    {
        Item = item;
        SoldPriceText = soldPriceText;
        Buyer = buyer;
        Status = status;
    }

    public TransferListItem Item { get; }

    public string SoldPriceText { get; }

    public string Buyer { get; }

    public TransferStatus Status { get; }
}