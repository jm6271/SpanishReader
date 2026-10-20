using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using SpanishReader.Core.Models;
using CommunityToolkit.Mvvm.Input;
using SpanishReader.Core.Abstractions;

namespace SpanishReader.Core.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ObservableCollection<Story> Stories { get; set; } = new ObservableCollection<Story>();

    private readonly INavigationService _navigationService;

    public MainPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;


        var sample = new Story
        {
            Title = "Una Historia Fantástica",
            EnglishTitle = "A Fantastic Story",
            Author = "Jordan Miller",
            Level = CEFR.A1,
            DateAdded = DateTime.Now,
        };
        Stories.Add(sample);

        var sample2 = new Story
        {
            Title = "Una Otra Historia",
            EnglishTitle = "Another Story",
            Author = "Jordan Miller",
            Level = CEFR.B2,
            DateAdded = DateTime.Now,
        };
        Stories.Add(sample2);
    }

    [RelayCommand]
    private void ImportStory()
    {
        _navigationService.Navigate<ImportPageViewModel>();
    }
}
