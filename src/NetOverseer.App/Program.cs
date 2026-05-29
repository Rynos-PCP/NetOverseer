// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Rynos-PCP
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace NetOverseer.App;

/// <summary>
/// Eigener Programm-Einstieg (anstelle des XAML-generierten Mains).
/// Hält den UI-Start zentral. Der headless Bootmonitor läuft inzwischen
/// in einer separaten EXE: <c>NetOverseer.BootMonitor.exe</c> (siehe
/// <c>StartupInstallerService</c>), weil WinUI 3 / WindowsAppRuntime in
/// Session 0 (SYSTEM-Task vor Login) nicht bootstrappen kann.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
