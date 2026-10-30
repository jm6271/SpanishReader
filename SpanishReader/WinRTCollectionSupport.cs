using System.Collections.ObjectModel;
using SpanishReader.Core.Models;

// These collections originate in Core, outside the WinRT generator's analysis.
// Register them here so ItemsSource can marshal them after reflection metadata
// has been trimmed, without introducing a Windows dependency into Core.
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<Story>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(CEFR[]))]
