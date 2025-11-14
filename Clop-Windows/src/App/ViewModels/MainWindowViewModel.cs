using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClopWindows.Core.Settings;

namespace ClopWindows.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly OnboardingViewModel _onboardingViewModel;
    private readonly CompareViewModel _compareViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private NavigationItemViewModel? _selectedItem;
    private ObservableObject? _currentView;

    public MainWindowViewModel(
        OnboardingViewModel onboardingViewModel,
        CompareViewModel compareViewModel,
        SettingsViewModel settingsViewModel)
    {
        _onboardingViewModel = onboardingViewModel;
        _compareViewModel = compareViewModel;
        _settingsViewModel = settingsViewModel;

        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new NavigationItemViewModel(MainSection.Onboarding, "Get Started", "Set up Clop for Windows"),
            new NavigationItemViewModel(MainSection.Compare, "Compare", "Drop files or review recent optimisations"),
            new NavigationItemViewModel(MainSection.Settings, "Settings", "Adjust optimisation defaults")
        };

        _onboardingViewModel.OnboardingCompleted += HandleOnboardingCompleted;

        var defaultSection = SettingsHost.Get(SettingsRegistry.FinishedOnboarding)
            ? MainSection.Compare
            : MainSection.Onboarding;

        SelectedItem = NavigationItems.First(item => item.Section == defaultSection);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public NavigationItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
            {
                return;
            }

            if (value is null)
            {
                CurrentView = null;
                return;
            }

            CurrentView = ResolveView(value.Section);
        }
    }

    public ObservableObject? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    private ObservableObject ResolveView(MainSection section) => section switch
    {
        MainSection.Onboarding => _onboardingViewModel,
        MainSection.Compare => _compareViewModel,
        MainSection.Settings => _settingsViewModel,
        _ => _onboardingViewModel
    };

    private void HandleOnboardingCompleted(object? sender, EventArgs e)
    {
        var compare = NavigationItems.First(item => item.Section == MainSection.Compare);
        SelectedItem = compare;
    }

    public void Dispose()
    {
        _onboardingViewModel.OnboardingCompleted -= HandleOnboardingCompleted;
    }
}

public enum MainSection
{
    Onboarding,
    Compare,
    Settings
}

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(MainSection section, string title, string subtitle)
    {
        Section = section;
        Title = title;
        Subtitle = subtitle;
    }

    public MainSection Section { get; }

    public string Title { get; }

    public string Subtitle { get; }
}
