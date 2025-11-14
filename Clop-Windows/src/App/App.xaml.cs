using System;
using System.Collections.Generic;
using System.Windows;
using ClopWindows.App.Services;
using ClopWindows.App.ViewModels;
using ClopWindows.App.Views.FloatingHud;
using ClopWindows.Core.Optimizers;
using ClopWindows.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClopWindows.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsHost.EnsureInitialized();

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IOptimiser, ImageOptimiser>();
                services.AddSingleton<IOptimiser, VideoOptimiser>();
                services.AddSingleton<IOptimiser, PdfOptimiser>();

                services.AddSingleton(provider =>
                {
                    var optimisers = provider.GetRequiredService<IEnumerable<IOptimiser>>();
                    return new OptimisationCoordinator(optimisers, Math.Max(Environment.ProcessorCount / 2, 2));
                });

                services.AddSingleton<FloatingHudViewModel>();
                services.AddSingleton<FloatingHudWindow>();
                services.AddSingleton<FloatingHudController>();
                services.AddSingleton<KeyboardShortcutService>();
                services.AddSingleton<OnboardingViewModel>();
                services.AddSingleton<CompareViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Start();

        var hudController = _host.Services.GetRequiredService<FloatingHudController>();
        hudController.Initialize();
        hudController.Show();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
            finally
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
