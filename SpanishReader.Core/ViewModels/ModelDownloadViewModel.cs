using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Services;

namespace SpanishReader.Core.ViewModels;

public partial class ModelDownloadViewModel(ModelDownloadService downloader,
    Func<string?, Task> configureModel) : ObservableObject
{
    private CancellationTokenSource? _downloadCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    public partial bool IsInstalled { get; private set; } = downloader.IsInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty] public partial double ProgressPercent { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = downloader.IsInstalled ? "Downloaded" : "Not downloaded";
    [ObservableProperty] public partial string ErrorMessage { get; private set; } = string.Empty;

    public bool CanDownload => !IsBusy && !IsInstalled;
    public bool CanDelete => !IsBusy && IsInstalled;
    public bool IsDownloading => IsBusy && _downloadCancellation is not null;

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (!CanDownload) return;
        ErrorMessage = string.Empty;
        IsBusy = true;
        _downloadCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsDownloading));
        Status = "Downloading";
        try
        {
            var progress = new Progress<(long Received, long? Total)>(value =>
            {
                if (!IsDownloading) return;
                if (value.Total is > 0)
                    ProgressPercent = 100.0 * value.Received / value.Total.Value;
                Status = value.Total is > 0
                    ? $"Downloading — {ProgressPercent:0}%"
                    : $"Downloading — {value.Received / 1_000_000:N0} MB";
            });
            await downloader.DownloadAsync(progress, _downloadCancellation.Token);
            await configureModel(downloader.ModelPath);
            IsInstalled = true;
            ProgressPercent = 100;
            Status = "Downloaded";
        }
        catch (OperationCanceledException)
        {
            Status = "Download paused";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
            Status = "Download failed";
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            IsBusy = false;
            OnPropertyChanged(nameof(IsDownloading));
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCancellation?.Cancel();

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDelete) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await configureModel(null);
            await downloader.DeleteAsync();
            IsInstalled = false;
            ProgressPercent = 0;
            Status = "Not downloaded";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
            Status = "Could not delete model";
            if (downloader.IsInstalled) await configureModel(downloader.ModelPath);
        }
        finally { IsBusy = false; }
    }
}
