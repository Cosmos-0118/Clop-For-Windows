using System;
using System.Collections.ObjectModel;
using ClopWindows.App.Infrastructure;
using ClopWindows.Core.Settings;

namespace ClopWindows.App.ViewModels;

public sealed class OnboardingViewModel : ObservableObject
{
    private readonly ObservableCollection<OnboardingStepViewModel> _steps;
    private readonly ReadOnlyObservableCollection<OnboardingStepViewModel> _readonlySteps;
    private bool _hasCompleted;

    public OnboardingViewModel()
    {
        _steps = new ObservableCollection<OnboardingStepViewModel>
        {
            new("Drop files", "Drag photos, PDFs, or videos right into the window."),
            new("Review savings", "Clop shows how much space you reclaimed for every item."),
            new("Share instantly", "Copy optimised results or open their folder with a click."),
            new("Automate", "Enable clipboard and watched folders to optimise without lifting a finger.")
        };

        _readonlySteps = new ReadOnlyObservableCollection<OnboardingStepViewModel>(_steps);

        GetStartedCommand = new RelayCommand(_ => CompleteOnboarding());
        _hasCompleted = SettingsHost.Get(SettingsRegistry.FinishedOnboarding);
    }

    public event EventHandler? OnboardingCompleted;

    public string Title => "Welcome to Clop";

    public string Subtitle => "Optimise images, videos, and PDFs with macOS feature parity.";

    public ReadOnlyObservableCollection<OnboardingStepViewModel> Steps => _readonlySteps;

    public bool HasCompleted
    {
        get => _hasCompleted;
        private set => SetProperty(ref _hasCompleted, value);
    }

    public RelayCommand GetStartedCommand { get; }

    private void CompleteOnboarding()
    {
        SettingsHost.Set(SettingsRegistry.FinishedOnboarding, true);
        HasCompleted = true;
        OnboardingCompleted?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class OnboardingStepViewModel
{
    public OnboardingStepViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}
