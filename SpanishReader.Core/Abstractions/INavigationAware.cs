using System;
using System.Collections.Generic;
using System.Text;

namespace SpanishReader.Core.Abstractions;

public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
}
