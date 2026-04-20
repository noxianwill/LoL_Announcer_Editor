using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                if (HandleCli(e.Args))
                {
                    Shutdown();
                    return;
                }
            }
            catch
            {
                Shutdown();
                return;
            }
            base.OnStartup(e);
        }

        private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args) => null;

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("An unexpected error occurred:\n" + e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) { }
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) => e.SetObserved();

        private bool HandleCli(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            var argset = new HashSet<string>(args.Select(a => a.ToLowerInvariant()));
            if (!argset.Contains("-parse")) return false;

            try
            {
                string originalBin = GetOption(args, "-originalbin");
                string originalWpk = GetOption(args, "-originalwpk");
                string originalBnk = GetOption(args, "-originalbnk");
                string addWemPath = GetOption(args, "-addwem");
                string toEvent = GetOption(args, "-toevent");
                string saveWpk = GetOption(args, "-savewpk");
                string saveBnk = GetOption(args, "-savebnk");

                if (string.IsNullOrWhiteSpace(originalBin) || string.IsNullOrWhiteSpace(originalWpk) || string.IsNullOrWhiteSpace(originalBnk) || 
                    string.IsNullOrWhiteSpace(addWemPath) || string.IsNullOrWhiteSpace(toEvent) || string.IsNullOrWhiteSpace(saveWpk) || string.IsNullOrWhiteSpace(saveBnk))
                    return true;

                var stringHashes = BinUtils.ParseBinFile(originalBin);
                if (stringHashes == null) return true;

                var sections = new BNKSections();
                var fullFidelity = new BnkFullFidelity();
                if (!BnkExtractor.ParseEventBnkFile_FullFidelity(originalBnk, fullFidelity, sections)) return true;

                uint wemId = ExtractWemId(addWemPath);
                byte[] wemBytes = File.ReadAllBytes(addWemPath);
                uint targetEventId = ResolveEventId(toEvent);

                var modder = new BnkModder(fullFidelity, sections);
                var lookup = new Dictionary<string, uint> { { toEvent, targetEventId } };
                modder.AddWemToEvent(toEvent, wemId, (uint)wemBytes.Length, lookup);

                var originalEventIds = new HashSet<uint>(sections.Events.Select(ev => ev.SelfId));
                var wemInfo = WpkExtractor.ParseWpkFile(originalWpk, stringHashes, originalEventIds);
                if (wemInfo == null) return true;

                var wemDataById = wemInfo.SortedWemDataList.ToDictionary(w => w.Id, w => w);
                var newWems = new List<AudioData> { new AudioData { Id = wemId, Length = (uint)wemBytes.Length, Data = wemBytes } };
                Utility.WriteWpkFilePreservingSlots(wemDataById, wemInfo.OriginalSlotCount, wemInfo.WemIdToSlotIndex, new HashSet<uint>(), newWems, saveWpk, wemInfo.OriginalOffsets, wemInfo.SlotIndexToEntry);

                BnkWriter.WriteBnkFile_FullFidelity(fullFidelity, saveBnk);
                return true;
            }
            catch { return true; }
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return string.Empty;
        }

        private static uint ExtractWemId(string wemPath)
        {
            var fn = Path.GetFileNameWithoutExtension(wemPath);
            if (uint.TryParse(fn, out var id)) return id;
            return 0;
        }

        private static uint ResolveEventId(string toEvent)
        {
            if (uint.TryParse(toEvent, out var id)) return id;
            return BinUtils.Fnv1Hash(toEvent);
        }
    }
}
