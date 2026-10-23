using Microsoft.UI.Xaml.Controls;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views;

public sealed partial class ImportPage : Page
{
    public ImportPageViewModel ViewModel { get; }

    public ImportPage(ImportPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    // Match the reader's compact margins without changing the form's data flow.
    private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        Microsoft.UI.Xaml.VisualStateManager.GoToState(this, ActualWidth < 900 ? "Narrow" : "Wide", false);
}
