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
}
