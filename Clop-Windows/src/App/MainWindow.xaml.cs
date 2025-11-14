using System.Windows;
using ClopWindows.App.Services;
using ClopWindows.App.ViewModels;

namespace ClopWindows.App;

public partial class MainWindow : Window
{
    private readonly KeyboardShortcutService _shortcutService;

    public MainWindow(MainWindowViewModel viewModel, KeyboardShortcutService shortcutService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _shortcutService = shortcutService;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _shortcutService.Attach(this);
    }
}
