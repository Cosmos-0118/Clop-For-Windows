using System;
using ClopWindows.Core.Settings;

namespace ClopWindows.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private bool _enableFloatingResults;
    private bool _autoHideFloatingResults;
    private int _autoHideFloatingResultsAfter;
    private bool _enableClipboardOptimiser;
    private bool _autoCopyToClipboard;
    private bool _preserveDates;
    private bool _stripMetadata;
    private bool _enableAutomaticImageOptimisations;
    private bool _enableAutomaticVideoOptimisations;
    private bool _enableAutomaticPdfOptimisations;
    private bool _suppressStoreUpdates;

    public SettingsViewModel()
    {
        SettingsHost.EnsureInitialized();
        Load();
        SettingsHost.SettingChanged += OnSettingChanged;
    }

    public bool EnableFloatingResults
    {
        get => _enableFloatingResults;
        set
        {
            if (SetProperty(ref _enableFloatingResults, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.EnableFloatingResults, value);
            }
        }
    }

    public bool AutoHideFloatingResults
    {
        get => _autoHideFloatingResults;
        set
        {
            if (SetProperty(ref _autoHideFloatingResults, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.AutoHideFloatingResults, value);
            }
        }
    }

    public int AutoHideFloatingResultsAfter
    {
        get => _autoHideFloatingResultsAfter;
        set
        {
            if (SetProperty(ref _autoHideFloatingResultsAfter, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.AutoHideFloatingResultsAfter, value);
            }
        }
    }

    public bool EnableClipboardOptimiser
    {
        get => _enableClipboardOptimiser;
        set
        {
            if (SetProperty(ref _enableClipboardOptimiser, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.EnableClipboardOptimiser, value);
            }
        }
    }

    public bool AutoCopyToClipboard
    {
        get => _autoCopyToClipboard;
        set
        {
            if (SetProperty(ref _autoCopyToClipboard, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.AutoCopyToClipboard, value);
            }
        }
    }

    public bool PreserveDates
    {
        get => _preserveDates;
        set
        {
            if (SetProperty(ref _preserveDates, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.PreserveDates, value);
            }
        }
    }

    public bool StripMetadata
    {
        get => _stripMetadata;
        set
        {
            if (SetProperty(ref _stripMetadata, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.StripMetadata, value);
            }
        }
    }

    public bool EnableAutomaticImageOptimisations
    {
        get => _enableAutomaticImageOptimisations;
        set
        {
            if (SetProperty(ref _enableAutomaticImageOptimisations, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.EnableAutomaticImageOptimisations, value);
            }
        }
    }

    public bool EnableAutomaticVideoOptimisations
    {
        get => _enableAutomaticVideoOptimisations;
        set
        {
            if (SetProperty(ref _enableAutomaticVideoOptimisations, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.EnableAutomaticVideoOptimisations, value);
            }
        }
    }

    public bool EnableAutomaticPdfOptimisations
    {
        get => _enableAutomaticPdfOptimisations;
        set
        {
            if (SetProperty(ref _enableAutomaticPdfOptimisations, value) && !_suppressStoreUpdates)
            {
                SettingsHost.Set(SettingsRegistry.EnableAutomaticPdfOptimisations, value);
            }
        }
    }

    private void Load()
    {
        _suppressStoreUpdates = true;

        EnableFloatingResults = SettingsHost.Get(SettingsRegistry.EnableFloatingResults);
        AutoHideFloatingResults = SettingsHost.Get(SettingsRegistry.AutoHideFloatingResults);
        AutoHideFloatingResultsAfter = SettingsHost.Get(SettingsRegistry.AutoHideFloatingResultsAfter);
        EnableClipboardOptimiser = SettingsHost.Get(SettingsRegistry.EnableClipboardOptimiser);
        AutoCopyToClipboard = SettingsHost.Get(SettingsRegistry.AutoCopyToClipboard);
        PreserveDates = SettingsHost.Get(SettingsRegistry.PreserveDates);
        StripMetadata = SettingsHost.Get(SettingsRegistry.StripMetadata);
        EnableAutomaticImageOptimisations = SettingsHost.Get(SettingsRegistry.EnableAutomaticImageOptimisations);
        EnableAutomaticVideoOptimisations = SettingsHost.Get(SettingsRegistry.EnableAutomaticVideoOptimisations);
        EnableAutomaticPdfOptimisations = SettingsHost.Get(SettingsRegistry.EnableAutomaticPdfOptimisations);

        _suppressStoreUpdates = false;
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        _suppressStoreUpdates = true;
        switch (e.Name)
        {
            case var name when name == SettingsRegistry.EnableFloatingResults.Name:
                EnableFloatingResults = SettingsHost.Get(SettingsRegistry.EnableFloatingResults);
                break;
            case var name when name == SettingsRegistry.AutoHideFloatingResults.Name:
                AutoHideFloatingResults = SettingsHost.Get(SettingsRegistry.AutoHideFloatingResults);
                break;
            case var name when name == SettingsRegistry.AutoHideFloatingResultsAfter.Name:
                AutoHideFloatingResultsAfter = SettingsHost.Get(SettingsRegistry.AutoHideFloatingResultsAfter);
                break;
            case var name when name == SettingsRegistry.EnableClipboardOptimiser.Name:
                EnableClipboardOptimiser = SettingsHost.Get(SettingsRegistry.EnableClipboardOptimiser);
                break;
            case var name when name == SettingsRegistry.AutoCopyToClipboard.Name:
                AutoCopyToClipboard = SettingsHost.Get(SettingsRegistry.AutoCopyToClipboard);
                break;
            case var name when name == SettingsRegistry.PreserveDates.Name:
                PreserveDates = SettingsHost.Get(SettingsRegistry.PreserveDates);
                break;
            case var name when name == SettingsRegistry.StripMetadata.Name:
                StripMetadata = SettingsHost.Get(SettingsRegistry.StripMetadata);
                break;
            case var name when name == SettingsRegistry.EnableAutomaticImageOptimisations.Name:
                EnableAutomaticImageOptimisations = SettingsHost.Get(SettingsRegistry.EnableAutomaticImageOptimisations);
                break;
            case var name when name == SettingsRegistry.EnableAutomaticVideoOptimisations.Name:
                EnableAutomaticVideoOptimisations = SettingsHost.Get(SettingsRegistry.EnableAutomaticVideoOptimisations);
                break;
            case var name when name == SettingsRegistry.EnableAutomaticPdfOptimisations.Name:
                EnableAutomaticPdfOptimisations = SettingsHost.Get(SettingsRegistry.EnableAutomaticPdfOptimisations);
                break;
        }
        _suppressStoreUpdates = false;
    }

    public void Dispose()
    {
        SettingsHost.SettingChanged -= OnSettingChanged;
    }
}
