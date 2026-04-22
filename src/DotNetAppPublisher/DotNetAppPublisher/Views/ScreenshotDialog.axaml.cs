using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DotNetAppPublisher.Views;

public partial class ScreenshotDialog : Window
{
    private readonly TextBlock _countdownTextBlock;
    private readonly TextBlock _completionTextBlock;
    private readonly ProgressBar _progressBar;
    private CancellationTokenSource? _cancellationTokenSource;

    public ScreenshotDialog()
    {
        InitializeComponent();
        
        _countdownTextBlock = this.FindControl<TextBlock>("CountdownTextBlock") 
            ?? throw new InvalidOperationException("CountdownTextBlock not found");
        _completionTextBlock = this.FindControl<TextBlock>("CompletionTextBlock") 
            ?? throw new InvalidOperationException("CompletionTextBlock not found");
        _progressBar = this.FindControl<ProgressBar>("Progress") 
            ?? throw new InvalidOperationException("ProgressBar not found");
        
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    public async Task ShowCountdownAsync(int seconds = 5)
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            // Reset UI
            _completionTextBlock.IsVisible = false;
            _countdownTextBlock.IsVisible = true;
            _progressBar.IsVisible = true;
            _progressBar.IsIndeterminate = false;

            // Show dialog
            IsVisible = true;

            // Countdown loop
            for (var remaining = seconds; remaining >= 0; remaining--)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var progress = (double)remaining / seconds * 100;
                _progressBar.Value = progress;

                if (remaining > 0)
                {
                    _countdownTextBlock.Text = $"Screenshot in {remaining}s...";
                    await Task.Delay(1000, cancellationToken);
                }
            }

            // Countdown complete
            if (cancellationToken.IsCancellationRequested)
                return;

            _countdownTextBlock.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // Dialog was closed during countdown
        }
    }

    public async Task ShowCompletionAsync(string message = "✓ Screenshot captured!", int displayMilliseconds = 2000)
    {
        try
        {
            _completionTextBlock.Text = message;
            _completionTextBlock.IsVisible = true;
            _progressBar.IsVisible = false;

            await Task.Delay(displayMilliseconds);
            
            Close();
        }
        catch (Exception)
        {
            // Ignore errors during completion display
        }
    }

    public async Task<string> ShowCountdownAndCaptureAsync(int seconds, Func<Task<string>> captureAction)
    {
        // Show the dialog
        Show();
        
        // Run countdown
        await ShowCountdownAsync(seconds);
        
        // Perform the capture
        var result = await captureAction();
        
        // Show completion
        await ShowCompletionAsync("✓ Screenshot captured!", 2000);
        
        return result;
    }

    public void UpdateCountdownText(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _countdownTextBlock.Text = text;
        }
        else
        {
            Dispatcher.UIThread.Post(() => _countdownTextBlock.Text = text);
        }
    }

    public void UpdateProgress(double value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _progressBar.Value = value;
        }
        else
        {
            Dispatcher.UIThread.Post(() => _progressBar.Value = value);
        }
    }
}
