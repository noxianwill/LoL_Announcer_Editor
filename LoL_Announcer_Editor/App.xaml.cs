using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading.Tasks;
using System.Reflection;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Set up assembly resolution to prevent LoadFrom errors
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            
            // Set up global exception handlers
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            
            // Initialize the ReferenceSoundMapper to load all Sound mappings from the original JSON
            ReferenceSoundMapper.Initialize();
            
            // Log successful startup
            LogError(new Exception("Application started successfully"));
        }

        private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                // Try to resolve missing assemblies gracefully
                LogError(new Exception($"Assembly resolve requested for: {args.Name}"));
                return null; // Let the default resolver handle it
            }
            catch (Exception ex)
            {
                LogError(ex);
                return null;
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogError(e.Exception);
            MessageBox.Show("An unexpected error occurred:\n" + e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogError(e.ExceptionObject as Exception);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogError(e.Exception);
            e.SetObserved();
        }

        private void LogError(Exception? ex)
        {
            try
            {
                if (ex == null) return;
                string logEntry = $"{ex.GetType().Name}: {ex.Message}";
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    logEntry += $"\nStackTrace: {ex.StackTrace}";
                }
                logEntry += "\n--------------------------------------------------";
                Defs.LogError(logEntry);
            }
            catch { }
        }
    }
}
