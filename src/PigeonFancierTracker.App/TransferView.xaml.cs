using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class TransferView : UserControl
{
    private readonly ITransferDataReader transferDataReader;
    private readonly ISessionStateService sessionState;

    public TransferView(
        ITransferDataReader transferDataReader,
        ISessionStateService sessionState)
    {
        InitializeComponent();
        this.transferDataReader = transferDataReader;
        this.sessionState = sessionState;
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
    }

    private async void TransferView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= TransferView_Loaded;
        await RefreshAsync();
    }
}
