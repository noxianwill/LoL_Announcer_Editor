using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using LoL_Announcer_Editor.Models;

namespace LoL_Announcer_Editor.Utils
{
    /// <summary>
    /// Static logging engine that handles file-based logging and categorized diagnostic output.
    /// All logs are written to 'app.log' in the application directory.
    /// </summary>
    public static class Logger
    {
        private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
        private static readonly object _lock = new object();

        static Logger()
        {
            try
            {
                if (File.Exists(_logPath)) File.Delete(_logPath);
                Log("Logger Initialized");
                Log("------------------");
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                lock (_lock) File.AppendAllText(_logPath, $"[{ts}] {message}\n");
            }
            catch { }
        }

        public static void StartNewSession() => Log("\n--- NEW SESSION ---");
        public static void LogError(string m) => Log($"ERROR: {m}");
        public static void LogSuccess(string m) => Log($"SUCCESS: {m}");
        public static void LogSection(string t) => Log($"\n=== {t} ===");

        public static void LogException(string context, Exception ex)
        {
            Log($"EXCEPTION in {context}: {ex.Message}");
            Log($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null) Log($"Inner Exception: {ex.InnerException.Message}");
        }

        // --- BIN PARSER LOGS ---
        public static void LogEventAmount(uint amount) => Log($"event amount: {amount}");
        public static void LogMusicAmount(ushort amount) => Log($"music amount: {amount}");
        public static void LogBinString(string str) => Log($"string: \"{str}\".");
        public static void LogBinError(string path) => LogError($"Error: Failed to open \"{path}\".");
        public static void LogBinMalformed() => LogError("Error: Malformed/truncated bin file!");
        public static void LogBinMalformed(char input) => LogError($"Error: Malformed input to Char2Int ({input:X}).");

        // --- BNK AUDIO PARSER LOGS ---
        public static void LogBnkError(string path) => LogError($"Error: Failed to open \"{path}\".");
        public static void LogBnkMissingSections(string path) => LogError($"Error: Failed to find the required sections in file \"{path}\". Make sure to provide the correct file.");
        public static void LogBnkSection(string name, long offset) => Log($"{name}@0x{offset:X}");

        // --- WPK EXTRACTOR/PACKAGER LOGS ---
        public static void LogWpkError(string path) => LogError($"Error: Failed to open \"{path}\".");
        public static void LogWpkMissingDir(string dir) => LogError($"Error: Target directory \"{dir}\" does not exist.");
        public static void LogWpkSection(string path) => LogSection($"ParseWpkFile: {path}");
        public static void LogWpkParsed(int count) => Log($"WPK parsed: {count} WEM files found");
        public static void LogWpkPackaged(string path) => LogSuccess($"WPK packaged successfully: {path}");
        public static void LogWpkPackagedEntry(uint id, int offset, int size) => Log($"  Packaged: {id}.wem (Offset: {offset}, Size: {size})");
        public static void LogWpkSourceFolder(string folder) => Log($"Source Folder: {folder}");
        public static void LogWpkOutputFile(string file) => Log($"Output File: {file}");
        public static void LogWpkFoundWems(int count) => Log($"Found {count} WEM files.");
        public static void LogWpkParseException(string path, Exception ex) => LogException($"ParseWpkFile ({path})", ex);

        // --- BNK READERS (HIRC) LOGS ---
        public static void LogHircSoundError(uint id, string msg) => LogError($"    [Type 2] Sound {id} Parse Error: {msg}");
        public static void LogHircAction(uint id, uint target, ushort type) => Log($"    [Type 3] Action {id}: Target {target}, Type {type}");
        public static void LogHircActionError(uint id, string msg) => LogError($"    [Type 3] Action {id} Parse Error: {msg}");
        public static void LogHircEvent(uint id, int count) => Log($"    [Type 4] Event {id}: {count} actions found.");
        public static void LogHircEventError(uint id, string msg) => LogError($"    [Type 4] Event {id} Parse Error: {msg}");
        public static void LogHircContainer(uint id, uint count) => Log($"    [Type 5] Container {id}: Found {count} children.");
        public static void LogHircContainerError(uint id, string msg) => LogError($"    ERROR in ReadRandomContainerObject {id}: {msg}");
        public static void LogHircSwitch(uint id, int count) => Log($"    [Type 6] SwitchContainer {id}: Found {count} children.");
        public static void LogHircSwitchError(uint id, string msg) => LogError($"    ERROR in ReadSwitchContainerObject {id}: {msg}");
        public static void LogHircMixer(uint id, int count) => Log($"    [Type 7] ActorMixer {id}: Found {count} children.");
        public static void LogHircMixerError(uint id, string msg) => LogError($"    ERROR in ReadActorMixerObject {id}: {msg}");

        // --- WWISE PROJECT EXPORT LOGS ---
        public static void LogExportStart(string folder) => Log($"Project Folder: {folder}");
        public static void LogExportStats(BNKSections s) => Log($"BNK Stats: {s.Sounds.Count} Sounds, {s.Events.Count} Events, {s.RandomContainers.Count} Containers");
        public static void LogExportPaths(string actor, string events)
        {
            Log($"Target WWU (Actor): {actor}");
            Log($"Target WWU (Events): {events}");
        }
        public static void LogExportSoundMap(int count) => Log($"Sound Map build complete. {count} sounds ready for re-parenting.");
        public static void LogExportWorkUnit(string name, string id) => Log($"Found WorkUnit: {name} (ID: {id})");
        public static void LogExportPatch(string path) => Log($"Patched {path} to use Vorbis instead of PCM.");
        public static void LogExportPatchError(string file, string msg) => LogError($"Failed to patch Conversion Settings file {file}: {msg}");
        public static void LogExportSoundExtractionStart() => Log("--- STARTING SOUND EXTRACTION ---");
        public static void LogExportSoundExtracted(uint id, string name) => Log($"  Extracted WEM ID {id} from Wwise object '{name}'");
        public static void LogExportSoundExtractionComplete(int count) => Log($"--- EXTRACTION COMPLETE: {count} unique IDs mapped ---");
        public static void LogExportBnkVersion(uint version) => Log($"BNK Version: {version}");
        public static void LogExportGeneratedCount(int count) => Log($"Generated {count} top-level containers/sounds.");
        public static void LogExportRawSounds(int count) => Log($"Found {count} raw 'Sound' elements in the WWU file.");
        public static void LogExportIdWarning(string name) => Log($"  Warning: Could not extract numeric ID from sound name '{name}'");
        public static void LogExportConvId(string id, string wuId) => Log($"Extracted Conversion Settings ID from wwu: {id} (WorkUnit: {wuId})");
        public static void LogExportRefError(string msg) => LogError($"Failed to extract references from conversion .wwu: {msg}");
        public static void LogExportCycle(uint id) => Log($"Cycle detected for ID {id}, skipping duplication.");
        public static void LogExportAdvBits(string name, byte bits) => Log($"    Node {name} AdvBits: 0x{bits:X2}");
        public static void LogExportMapped(int max, int over, bool virt) => Log($"    Mapped: MaxReached={max}, OverLimit={over}, UseVirtual={virt}");
        public static void LogExportBuildingContainer(string name, uint id) => Log($"Building Container '{name}' (ID: {id})");
        public static void LogExportBankInfo(string name, string shortId) => Log($"SoundBank name: '{name}', FNV32 ShortID: {shortId}");

        // --- WPK WRITER LOGS ---
        public static void LogWpkWriteStart(string path, int original, int newCount)
        {
            Log($"Output Path: {path}");
            Log($"Original Slot Count: {original}, New WEMs: {newCount}");
        }
        public static void LogWpkWriteNewWem(uint id, int slot, int offset, int len) => Log($"  Writing new WEM: Id={id}, SlotIndex={slot}, EntryOffset={offset}, DataLength={len}");
        public static void LogWpkWriteWemDetails(uint id, int offset, int end) => Log($"  WEM {id}: DataOffset={offset}, DataEndPos={end}");
        public static void LogWpkWriteSuccess(string path, long size) => LogSuccess($"WPK file written successfully to {path}. Total size: {size} bytes.");
        public static void LogWpkIntegrityStart() => LogSection("WPK BIT-LEVEL INTEGRITY CHECK");
        public static void LogWpkIntegritySlots(int actual, int expected) => Log($"Verified Slot Count: {actual} (Expected: {expected})");
        public static void LogWpkIntegrityValid(int count) => Log($"Verified {count} active WEM slots with non-zero data.");
        public static void LogWpkNewWemCount(int count, int startSlot) => Log($"Adding {count} new WEM files starting at slot {startSlot}");
        public static void LogWpkZeroOffset(int slot, int offset) => Log($"!!! CRITICAL: Slot {slot} has header at {offset} but data offset is ZERO!");
        public static void LogWpkZeroData(int slot, int offset) => Log($"!!! CRITICAL: Slot {slot} at data offset {offset} contains ZEROS!");
        public static void LogWpkIntegrityError(string msg) => Log($"!!! WPK INTEGRITY ERROR: {msg}");

        // --- MAIN WINDOW / UI LOGS ---
        public static void LogPaths(string bin, string wpk, string bnk) => Log($"Paths: BIN={bin}, WPK={wpk}, BNK={bnk}");
        public static void LogBnkLoadStart(string path) => Log($"Loading original BNK bytes for Full Fidelity mode: {path}");
        public static void LogBnkLoaded(int size) => Log($"BNK Loaded: {size} bytes");
        public static void LogBinParseStart() => Log("Parsing BIN file...");
        public static void LogBinParsed(int count) => Log($"BIN parsed: {count} hashes found");
        public static void LogBnkParseStart() => Log("Parsing BNK file (Full Fidelity Mode)...");
        public static void LogBnkParsed(int count) => Log($"Full Fidelity BNK parsed: {count} objects found");
        public static void LogEventResolve(uint eventId, uint actionId, uint targetId) => Log($"Event {eventId} -> Action {actionId} -> TargetId: {targetId}");
        public static void LogEventWemsResolved(uint eventId, int count) => Log($"Event {eventId} resolved {count} WEMs");
        public static void LogFilesLoaded(string bin, string wpk, string bnk)
        {
            Log($"BIN: {bin}");
            Log($"WPK: {wpk}");
            Log($"BNK: {bnk}");
        }
        public static void LogResolveSound(uint objId, uint srcId) => Log($"ResolveObjectToFileIds: Found Sound {objId} -> SourceId {srcId}");
        public static void LogResolveContainer(uint objId, int count) => Log($"ResolveObjectToFileIds: Found RandomContainer {objId} with {count} children");
        public static void LogResolveChild(uint childId) => Log($"  RC childId: {childId}");
        public static void LogAudioPlay(string section, string wemId, string parent, int size)
        {
            LogSection(section);
            Log($"WEM ID: {wemId}");
            Log($"Parent: {parent}");
            Log($"Size: {size} bytes");
        }
        public static void LogWemReplaced(int count, string file) => LogError($"Replaced {count} WEM(s) (and all with same ID) with {file}.");
        public static void LogWpkSaveSuccess(string path, int count)
        {
            Log($"Output path: {path}");
            Log($"WEM Count: {count}");
            LogError($"Saved {count} WEM(s) to:\n{path}");
        }
        public static void LogInitError(string msg) => LogError($"Failed to initialize application:\n{msg}");
        public static void LogInvalidWwiseProject(string error) => LogError($"Selected folder is not a valid Wwise project:\n{error}");
        public static void LogExportError(string msg) => LogError($"Export failed: {msg}");
        public static void LogBnkLoadError(string msg) => LogError($"Failed to load BNK file: {msg}");
        public static void LogFileParsingError(string msg) => LogError($"An error occurred while parsing files:\n{msg}");
        public static void LogExtractionError(string msg) => LogError($"Extraction failed:\n{msg}");
        public static void LogWemReplacementSummary(int replaced, int notMatched) => LogError($"Replaced {replaced} WEM(s) (and all with same ID). {notMatched} did not match any selected file.");
        public static void LogWemReplacementError(string msg) => LogError($"Error during WEM replacement: {msg}");
        public static void LogIntegrityMismatch(string node, int dataLen, uint declLen) => LogError($"Data integrity mismatch for node {node}: Data.Length={dataLen}, Declared Length={declLen}");
        public static void LogWpkSaveError(string msg) => LogError($"Error saving WPK file:\n{msg}");
        public static void LogPackagingError(string msg) => LogError($"Packaging failed: {msg}");

        // --- LEGACY OBJECT LOGGERS ---
        public static void LogObject(string context, object obj)
        {
            if (obj is Sound sound) LogSound(context, sound);
            else if (obj is RandomContainer rc) LogRandomContainer(context, rc);
            else Log($"  [{context}] {obj.GetType().Name}: (no specific logger)");
        }

        private static void LogSound(string context, Sound sound)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] Sound:");
            sb.AppendLine($"    SelfId:     {sound.SelfId}");
            sb.AppendLine($"    SourceId:   {sound.SourceId}");
            sb.AppendLine($"    StreamType: {sound.StreamType}");
            Log(sb.ToString().TrimEnd());
        }

        private static void LogRandomContainer(string context, RandomContainer rc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  [{context}] RandomContainer:");
            sb.AppendLine($"    SelfId:   {rc.SelfId}");
            sb.AppendLine($"    Sounds:   {rc.SoundIds.Count}");
            sb.AppendLine($"    Playlist: {rc.Playlist.Count}");
            Log(sb.ToString().TrimEnd());
        }
    }
}
