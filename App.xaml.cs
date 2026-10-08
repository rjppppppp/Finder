using System.Threading;
using System.Windows;

namespace FinderApp;

/// <summary>
/// Interaction logic for App.xaml
/// Enforces single-instance execution. If another instance is launched,
/// it instantly signals the running instance to show its window and exits.
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _wakeupEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "Local\\FinderApp_SingleInstance_Mutex";
        const string eventName = "Local\\FinderApp_Wakeup_Event";

        bool isFirstInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out isFirstInstance);
        }
        catch
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Another instance is already active! Signal it to wake up and focus.
            try
            {
                if (EventWaitHandle.TryOpenExisting(eventName, out var existingEvent))
                {
                    existingEvent.Set();
                }
            }
            catch { }

            // Terminate this secondary process immediately
            Environment.Exit(0);
            return;
        }

        // Primary instance: listen for wakeup signals in background
        try
        {
            _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        _wakeupEvent.WaitOne();
                        Current?.Dispatcher.InvokeAsync(() =>
                        {
                            if (Current?.MainWindow is MainWindow mw)
                            {
                                mw.ShowWindow();
                            }
                        });
                    }
                    catch
                    {
                        break;
                    }
                }
            });
        }
        catch { }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _wakeupEvent?.Dispose();
        }
        catch { }

        base.OnExit(e);
    }
}
