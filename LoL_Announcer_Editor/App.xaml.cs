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
            // Set up assembly resolution to prevent LoadFrom errors
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            // Set up global exception handlers
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

        private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            // Let the default resolver handle it
            return null;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("An unexpected error occurred:\n" + e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Unhandled exception - nothing to do without logging
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
        }

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

                if (string.IsNullOrWhiteSpace(originalBin) || string.IsNullOrWhiteSpace(originalWpk) || string.IsNullOrWhiteSpace(originalBnk) || string.IsNullOrWhiteSpace(addWemPath) || string.IsNullOrWhiteSpace(toEvent) || string.IsNullOrWhiteSpace(saveWpk) || string.IsNullOrWhiteSpace(saveBnk))
                {
                    return true;
                }

                // Parse BIN file for event name to ID mapping
                var stringHashes = BinUtils.ParseBinFile(originalBin);
                if (stringHashes == null)
                {
                    return true;
                }
                var eventNameLookup = new Dictionary<uint, string>();
                foreach (var sh in stringHashes)
                {
                    eventNameLookup[sh.Hash] = sh.String;
                }

                var sections = new BNKSections();
                var fullFidelity = new BnkFullFidelity();
                bool bnkOk = BnkExtractor.ParseEventBnkFile_FullFidelity(originalBnk, fullFidelity, sections);
                if (!bnkOk)
                {
                    return true;
                }

                var originalEventIds = new HashSet<uint>();
                foreach (var ev in sections.Events) originalEventIds.Add(ev.SelfId);
                var wemInfo = WpkExtractor.ParseWpkFile(originalWpk, stringHashes, originalEventIds);
                if (wemInfo == null)
                {
                    return true;
                }

                uint wemId = ExtractWemId(addWemPath);
                byte[] wemBytes = File.ReadAllBytes(addWemPath);

                uint targetEventId = ResolveEventId(toEvent);
                if (targetEventId == 0)
                {
                    return true;
                }
                bool addOk = AddWemToBnk_FullFidelity(fullFidelity, targetEventId, wemId, null);
                if (!addOk)
                {
                    return true;
                }

                var wemDataById = wemInfo.SortedWemDataList.ToDictionary(w => w.Id, w => w);
                var deletedIds = new HashSet<uint>();
                var newWems = new List<AudioData> { new AudioData { Id = wemId, Length = (uint)wemBytes.Length, Data = wemBytes } };
                Utility.WriteWpkFilePreservingSlots(
                    wemDataById,
                    wemInfo.OriginalSlotCount,
                    wemInfo.WemIdToSlotIndex,
                    deletedIds,
                    newWems,
                    saveWpk,
                    wemInfo.OriginalOffsets,
                    wemInfo.SlotIndexToEntry);

                bool writeBnk = BnkWriter.WriteBnkFile_FullFidelity(fullFidelity, saveBnk);
                if (!writeBnk)
                {
                    return true;
                }

                // Re-parse to validate
                var testSections = new BNKSections();
                var testFf = new BnkFullFidelity();
                BnkExtractor.ParseEventBnkFile_FullFidelity(saveBnk, testFf, testSections);
                var customEventIds = new HashSet<uint>();
                foreach (var ev in testSections.Events) customEventIds.Add(ev.SelfId);
                WpkExtractor.ParseWpkFile(saveWpk, new List<StringHash>(), customEventIds);

                return true;
            }
            catch
            {
                return true;
            }
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

        private static bool AddWemToBnk_FullFidelity(BnkFullFidelity fullFidelity, uint eventId, uint wemId, uint? preferredContainerId)
        {
            try
            {
                WemAdditionLogger.LogSection($"AddWemToBnk_FullFidelity: eventId={eventId}, wemId={wemId}");
                if (fullFidelity == null || fullFidelity.AllObjectsInOrder == null)
                {
                    WemAdditionLogger.Log("ERROR: fullFidelity is null or has no objects");
                    return false;
                }
                WemAdditionLogger.Log($"Total objects in BNK: {fullFidelity.AllObjectsInOrder.Count}");

                // Log all events available
                var allEvents = fullFidelity.AllObjectsInOrder.Where(o => o.Type == 4 && o.ParsedObject is Event).ToList();
                WemAdditionLogger.Log($"Available events in BNK: {allEvents.Count}");
                foreach (var ev in allEvents)
                {
                    var evParsed = ev.ParsedObject as Event;
                    if (evParsed != null)
                        WemAdditionLogger.Log($"  Event SelfId={evParsed.SelfId}");
                }

                var evtObj = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 4 && o.ParsedObject is Event e && e.SelfId == eventId);
                if (evtObj == null)
                {
                    WemAdditionLogger.Log($"ERROR: Event with id {eventId} not found in BNK");
                    return false;
                }
                var evt = evtObj.ParsedObject as Event;
                if (evt == null) return false;
                uint eaId = 0;
                if (preferredContainerId.HasValue)
                {
                    foreach (var id in evt.EventIds)
                    {
                        var eaMatch = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.ParsedObject is EventAction ea && ea.SelfId == id);
                        if (eaMatch != null)
                        {
                            var eaParsed = eaMatch.ParsedObject as EventAction;
                            if (eaParsed.SoundObjectId == preferredContainerId.Value)
                            {
                                eaId = id;
                                break;
                            }
                        }
                    }
                }
                if (eaId == 0) eaId = evt.EventIds.FirstOrDefault();
                if (eaId == 0) return false;
                var eaObj = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.ParsedObject is EventAction ea && ea.SelfId == eaId);
                if (eaObj == null) return false;
                var eaRef = eaObj.ParsedObject as EventAction;
                uint targetId = eaRef.SoundObjectId;
                if (preferredContainerId.HasValue && preferredContainerId.Value != targetId) targetId = preferredContainerId.Value;

                var rcObj = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 5 && o.ParsedObject is RandomContainer rc && rc.SelfId == targetId);
                if (rcObj != null)
                {
                    uint newSoundId = GenerateNewId(fullFidelity);
                    var templateSoundObj = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 2 && o.RawData != null && o.ParsedObject is Sound);
                    if (templateSoundObj == null) return false;
                    var templateSound = templateSoundObj.ParsedObject as Sound;
                    if (templateSound == null) return false;
                    byte[] soundRaw = (byte[])templateSoundObj.RawData.Clone();
                    Array.Copy(BitConverter.GetBytes(newSoundId), 0, soundRaw, 5, 4);
                    // Patch sourceID at offset 14 (after type+length+selfid+pluginid+streamtype)
                    // Sound structure: Type(1) + Length(4) + SelfId(4) + PluginID(4) + StreamType(1) + sourceID(4)
                    if (soundRaw.Length > 18)
                        Array.Copy(BitConverter.GetBytes(wemId), 0, soundRaw, 14, 4);
                    // NOTE: Do NOT patch offset 17 - it would corrupt sourceID!
                    var newSound = new Sound
                    {
                        SelfId = newSoundId,
                        Unknown1 = templateSound.Unknown1,
                        IsStreamed = templateSound.IsStreamed,
                        Unknown2 = templateSound.Unknown2 != null ? (byte[])templateSound.Unknown2.Clone() : new byte[0],
                        Unknown3 = templateSound.Unknown3 != null ? (byte[])templateSound.Unknown3.Clone() : new byte[0],
                        FileId = wemId,
                        StreamFileID = wemId,
                        Reserved0 = templateSound.Reserved0,
                        StreamSourceType = templateSound.StreamSourceType,
                        StreamSourceID = templateSound.StreamSourceID,
                        StreamFileOffset = templateSound.StreamFileOffset,
                        StreamFileLength = templateSound.StreamFileLength,
                        SoundType = templateSound.SoundType,
                        SoundStructure = templateSound.SoundStructure,
                        RawData = soundRaw
                    };
                    // Use IsChanged=false to use the patched RawData directly (serialization is broken for this BNK format)
                    fullFidelity.AllObjectsInOrder.Add(new BnkObjectRecord { Type = 2, Length = (uint)(soundRaw.Length - 5), RawData = soundRaw, ParsedObject = newSound, IsChanged = false, ObjectId = newSoundId });
                    var rc = rcObj.ParsedObject as RandomContainer;
                    // Playlist format is 8 bytes per entry (playID: u32 + weight: u32)
                    // Use weight from first existing entry for consistency, or default to 50000
                    uint defaultWeight = rc.Playlist.Count > 0 ? rc.Playlist[0].Weight : 50000;
                    rc.Playlist.Add(new PlaylistEntry { PlayID = newSoundId, Weight = defaultWeight, RsGroupID = 0 });
                    rc.SoundIds.Add(newSoundId);  // Keep backward compatibility
                    rc.SoundIdAmount = (uint)rc.SoundIds.Count;
                    rc.PlaylistLength = (ushort)rc.Playlist.Count;  // Update PlaylistLength
                    rcObj.IsChanged = true;
                    return true;
                }
                else
                {
                    uint newSoundId = GenerateNewId(fullFidelity);
                    uint newEventActionId = GenerateNewId(fullFidelity);
                    var templateSoundObj = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 2 && o.RawData != null && o.ParsedObject is Sound);
                    var templateEventActionObj = fullFidelity.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.RawData != null && o.ParsedObject is EventAction);
                    if (templateSoundObj == null || templateEventActionObj == null) return false;
                    var templateSound = templateSoundObj.ParsedObject as Sound;
                    var templateEventAction = templateEventActionObj.ParsedObject as EventAction;
                    if (templateSound == null || templateEventAction == null) return false;
                    byte[] soundRaw = (byte[])templateSoundObj.RawData.Clone();
                    Array.Copy(BitConverter.GetBytes(newSoundId), 0, soundRaw, 5, 4);
                    // Patch sourceID at offset 14 (after type+length+selfid+pluginid+streamtype)
                    // Sound structure: Type(1) + Length(4) + SelfId(4) + PluginID(4) + StreamType(1) + sourceID(4)
                    if (soundRaw.Length > 18)
                        Array.Copy(BitConverter.GetBytes(wemId), 0, soundRaw, 14, 4);
                    // NOTE: Do NOT patch offset 17 - it would corrupt sourceID!
                    var newSound = new Sound
                    {
                        SelfId = newSoundId,
                        Unknown1 = templateSound.Unknown1,
                        IsStreamed = templateSound.IsStreamed,
                        Unknown2 = templateSound.Unknown2 != null ? (byte[])templateSound.Unknown2.Clone() : new byte[0],
                        Unknown3 = templateSound.Unknown3 != null ? (byte[])templateSound.Unknown3.Clone() : new byte[0],
                        FileId = wemId,
                        StreamFileID = wemId,
                        Reserved0 = templateSound.Reserved0,
                        StreamSourceType = templateSound.StreamSourceType,
                        StreamSourceID = templateSound.StreamSourceID,
                        StreamFileOffset = templateSound.StreamFileOffset,
                        StreamFileLength = templateSound.StreamFileLength,
                        SoundType = templateSound.SoundType,
                        SoundStructure = templateSound.SoundStructure,
                        RawData = soundRaw
                    };
                    // Use IsChanged=false to use the patched RawData directly (serialization is broken for this BNK format)
                    fullFidelity.AllObjectsInOrder.Add(new BnkObjectRecord { Type = 2, Length = (uint)(soundRaw.Length - 5), RawData = soundRaw, ParsedObject = newSound, IsChanged = false, ObjectId = newSoundId });
                    byte[] eaRaw = (byte[])templateEventActionObj.RawData.Clone();
                    Array.Copy(BitConverter.GetBytes(newEventActionId), 0, eaRaw, 5, 4);
                    Array.Copy(BitConverter.GetBytes(newSoundId), 0, eaRaw, eaRaw.Length - 4, 4);
                    var newEa = new EventAction { SelfId = newEventActionId, Scope = templateEventAction.Scope, Type = templateEventAction.Type, SoundObjectId = newSoundId, SwitchGroupId = templateEventAction.SwitchGroupId, RawData = eaRaw };
                    fullFidelity.AllObjectsInOrder.Add(new BnkObjectRecord { Type = 3, Length = (uint)(eaRaw.Length - 5), RawData = eaRaw, ParsedObject = newEa, IsChanged = false, ObjectId = newEventActionId });
                    evt.EventIds.Add(newEventActionId);
                    evtObj.IsChanged = true;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static uint GenerateNewId(BnkFullFidelity fullFidelity)
        {
            var rng = new Random();
            var existing = new HashSet<uint>(fullFidelity.AllObjectsInOrder.Where(o => o.ObjectId.HasValue).Select(o => o.ObjectId.Value));
            uint id;
            do { id = (uint)rng.Next(100000000, int.MaxValue); } while (existing.Contains(id));
            return id;
        }
    }
}
