using System.Windows;
using System.Windows.Input;
using ClopWindows.App.Infrastructure;
using ClopWindows.App.Views.Dialogs;

namespace ClopWindows.App.ViewModels;

public sealed class ShortcutPreferenceViewModel : ObservableObject
{
    private readonly ShortcutId _id;
    private readonly ShortcutScope _scope;
    private string _currentBinding;

    public ShortcutPreferenceViewModel(ShortcutDescriptor descriptor)
    {
        _id = descriptor.Id;
        _scope = descriptor.Scope;
        Description = descriptor.Description;
        DefaultBinding = descriptor.DefaultDisplay;
        _currentBinding = ShortcutCatalog.GetDisplayString(_id);

        CaptureCommand = new RelayCommand(_ => CaptureShortcut());
        ClearCommand = new RelayCommand(_ => ClearShortcut());
        ResetCommand = new RelayCommand(_ => ResetShortcut());
    }

    public string Description { get; }

    public string DefaultBinding { get; }

    public string ScopeLabel => _scope == ShortcutScope.Global ? "Global" : "In-app";

    public string CurrentBinding
    {
        get => _currentBinding;
        private set => SetProperty(ref _currentBinding, value);
    }

    public ICommand CaptureCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand ResetCommand { get; }

    public void Refresh()
    {
        CurrentBinding = ShortcutCatalog.GetDisplayString(_id);
    }

    private void CaptureShortcut()
    {
        var dialog = new ShortcutCaptureDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            ScopeLabel = ScopeLabel,
            CurrentBinding = CurrentBinding,
            DefaultBinding = DefaultBinding
        };

        var result = dialog.ShowDialog();
        if (result != true)
        {
            return;
        }

        if (dialog.IsDisabled)
        {
            ClearShortcut();
            return;
        }

        var modifiers = dialog.CapturedModifiers;
        var key = dialog.CapturedKey;

        if (!ValidateCombination(modifiers, key))
        {
            return;
        }

        var conflict = ShortcutCatalog.FindConflict(_id, modifiers, key);
        if (conflict is not null)
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
        $"That shortcut is already used by '{conflict.Description}'. Pick a different combination.",
        "Shortcut conflict",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
            return;
        }

        ShortcutCatalog.UpdateShortcut(_id, modifiers, key);
        Refresh();
    }

    private bool ValidateCombination(ModifierKeys modifiers, Key key)
    {
        if (key == Key.None)
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
        "Press a non-modifier key to create a shortcut.",
        "Invalid shortcut",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
            return false;
        }

        if (_scope == ShortcutScope.Global && modifiers == ModifierKeys.None)
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
                "Global shortcuts must include at least one modifier key (Ctrl, Shift, Alt, or Win).",
                "Invalid shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (modifiers == ModifierKeys.None && !AllowsModifierlessKey(key))
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
            "Add a modifier (Ctrl, Shift, Alt, or Win) for that key combination.",
            "Invalid shortcut",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private static bool AllowsModifierlessKey(Key key)
    {
        return key is >= Key.F1 and <= Key.F24 or Key.Escape or Key.Tab or Key.Enter;
    }

    private void ClearShortcut()
    {
        ShortcutCatalog.ClearShortcut(_id);
        Refresh();
    }

    private void ResetShortcut()
    {
        ShortcutCatalog.ResetShortcut(_id);
        Refresh();
    }
}
