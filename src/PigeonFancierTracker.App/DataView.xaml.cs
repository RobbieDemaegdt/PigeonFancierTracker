using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public partial class DataView : UserControl
{
    private readonly IDataExporter exporter;
    private readonly IDataImporter importer;
    private readonly IDataResetter resetter;
    private readonly ISessionStateService sessionState;
    private CancellationTokenSource? exportCts;
    private CancellationTokenSource? importCts;

    public DataView(IDataExporter exporter, IDataImporter importer, IDataResetter resetter, ISessionStateService sessionState)
    {
        InitializeComponent();
        this.exporter = exporter;
        this.importer = importer;
        this.resetter = resetter;
        this.sessionState = sessionState;
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Gegevens exporteren",
            Filter = "Pigeon Fancier Back-up (*.pfbackup)|*.pfbackup",
            DefaultExt = ".pfbackup",
            FileName = $"pigeonfancier-backup-{DateTime.Now:yyyy-MM-dd}"
        };

        if (dialog.ShowDialog() != true) return;

        SetExportRunning(true);
        exportCts = new CancellationTokenSource();
        var progress = new Progress<DataPortProgress>(p =>
        {
            ExportProgress.Value = p.Percentage;
            ExportDetailText.Text = p.StepLabel;
        });

        try
        {
            var result = await exporter.ExportAsync(dialog.FileName, progress, exportCts.Token);
            ExportStatusText.Text = result.Success ? "✓" : "✗";
            ExportDetailText.Text = result.Message;
        }
        catch (OperationCanceledException)
        {
            ExportDetailText.Text = "Export geannuleerd.";
            ExportStatusText.Text = "";
        }
        catch (Exception ex)
        {
            ExportDetailText.Text = $"Fout: {ex.Message}";
            ExportStatusText.Text = "✗";
        }
        finally
        {
            SetExportRunning(false);
            exportCts?.Dispose();
            exportCts = null;
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        exportCts?.Cancel();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Gegevens importeren",
            Filter = "Pigeon Fancier Back-up (*.pfbackup)|*.pfbackup",
            DefaultExt = ".pfbackup"
        };

        if (dialog.ShowDialog() != true) return;

        SetImportRunning(true);
        importCts = new CancellationTokenSource();
        var progress = new Progress<DataPortProgress>(p =>
        {
            ImportProgress.Value = p.Percentage;
            ImportDetailText.Text = p.StepLabel;
        });

        try
        {
            var result = await importer.ImportAsync(dialog.FileName, progress, importCts.Token);
            ImportStatusText.Text = result.Success ? "✓" : "✗";
            ImportDetailText.Text = result.Success
                ? $"{result.Message}\nMomentopnamen: {result.SnapshotCount}, Syncruns: {result.SyncRunCount}, Sync-items: {result.SyncRunItemCount}, Transfers: {result.TransferCount}"
                : result.Message;
        }
        catch (OperationCanceledException)
        {
            ImportDetailText.Text = "Import geannuleerd.";
            ImportStatusText.Text = "";
        }
        catch (Exception ex)
        {
            ImportDetailText.Text = $"Fout: {ex.Message}";
            ImportStatusText.Text = "✗";
        }
        finally
        {
            SetImportRunning(false);
            importCts?.Dispose();
            importCts = null;
        }
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e)
    {
        importCts?.Cancel();
    }

    private void SetExportRunning(bool running)
    {
        ExportButton.IsEnabled = !running;
        ImportButton.IsEnabled = !running;
        CancelExportButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ExportProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running)
        {
            ExportProgress.Value = 0;
            ExportStatusText.Text = "";
            ExportDetailText.Text = "";
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Weet je zeker dat je ALLE lokale gegevens wilt wissen?\n\nDit verwijdert alle momentopnamen, syncruns, transfers en opgeslagen inloggegevens. Dit kan niet ongedaan worden gemaakt.",
            "Alle gegevens wissen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        ResetButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        ResetStatusText.Text = "Bezig met wissen...";

        try
        {
            await resetter.ResetAllAsync();
            sessionState.SetState(Core.Domain.SessionState.LoggedOut);
            ResetStatusText.Text = "Alle gegevens zijn gewist.";
        }
        catch (Exception ex)
        {
            ResetStatusText.Text = $"Fout: {ex.Message}";
        }
        finally
        {
            ResetButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            ImportButton.IsEnabled = true;
        }
    }

    private void SetImportRunning(bool running)
    {
        ImportButton.IsEnabled = !running;
        ExportButton.IsEnabled = !running;
        CancelImportButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ImportProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running)
        {
            ImportProgress.Value = 0;
            ImportStatusText.Text = "";
            ImportDetailText.Text = "";
        }
    }
}
