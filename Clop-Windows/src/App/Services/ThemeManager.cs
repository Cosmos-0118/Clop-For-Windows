using System;
using System.Linq;
using System.Windows;

namespace ClopWindows.App.Services;

public sealed class ThemeManager : IDisposable
{
    private readonly ResourceDictionary _defaultTheme;
    private readonly ResourceDictionary _highContrastTheme;
    private bool _isHighContrast;
    private bool _disposed;

    public ThemeManager()
    {
        if (System.Windows.Application.Current is null)
        {
            throw new InvalidOperationException("Application resources are not available.");
        }

        _defaultTheme = ResolveDefaultTheme();
        _highContrastTheme = LoadTheme("Resources/Theme.HighContrast.xaml");

        Apply(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _disposed = true;
    }

    private static ResourceDictionary LoadTheme(string resourcePath)
    {
        var uri = new Uri($"pack://application:,,,/ClopWindows;component/{resourcePath}", UriKind.Absolute);
        return new ResourceDictionary { Source = uri };
    }

    private ResourceDictionary ResolveDefaultTheme()
    {
        var dictionaries = System.Windows.Application.Current!.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source?.OriginalString?.EndsWith("Theme.Default.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null)
        {
            return existing;
        }

        var theme = LoadTheme("Resources/Theme.Default.xaml");
        dictionaries.Insert(0, theme);
        return theme;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            Apply(SystemParameters.HighContrast);
        }
    }

    private void Apply(bool highContrast)
    {
        if (System.Windows.Application.Current is null)
        {
            return;
        }

        if (highContrast == _isHighContrast)
        {
            return;
        }

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;

        if (highContrast)
        {
            dictionaries.Remove(_defaultTheme);
            if (!dictionaries.Contains(_highContrastTheme))
            {
                dictionaries.Add(_highContrastTheme);
            }
        }
        else
        {
            dictionaries.Remove(_highContrastTheme);
            if (!dictionaries.Contains(_defaultTheme))
            {
                dictionaries.Insert(0, _defaultTheme);
            }
        }

        _isHighContrast = highContrast;
    }
}
