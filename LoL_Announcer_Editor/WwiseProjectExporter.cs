using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Exports the parsed BNK/WPK hierarchy to a Wwise project template.
    /// Creates Actor-Mixer hierarchy and Events work units from scratch.
    /// </summary>
    public class WwiseProjectExporter
    {
        private Dictionary<uint, string> _containerIdToGuid = new();
        private Dictionary<uint, string> _objectNameMap = new();  // ID -> human-readable name
        private string _actorMixerWorkUnitId = "";
        private string _masterBusId = "";
        private string _masterBusWorkUnitId = "";
        private string _conversionSettingsId = "";
        private string _conversionSettingsWorkUnitId = "";

        public void Export(
            BNKSections sections,
            Dictionary<uint, string> eventNameLookup,
            string projectFolder)
        {
            WwiseExporterLogger.LogSection("WWISE PROJECT EXPORT START");
            WwiseExporterLogger.Log($"Project Folder: {projectFolder}");
            WwiseExporterLogger.Log($"BNK Version: {sections.BnkVersion}");
            WwiseExporterLogger.Log($"BNK Stats: {sections.Sounds.Count} Sounds, {sections.Events.Count} Events, {sections.RandomContainers.Count} Containers");

            // 1. Path setup
            string actorMixerWwuPath = Path.Combine(projectFolder, "Actor-Mixer Hierarchy", "Default Work Unit.wwu");
            string eventsWwuPath = Path.Combine(projectFolder, "Events", "Default Work Unit.wwu");

            WwiseExporterLogger.Log($"Target WWU (Actor): {actorMixerWwuPath}");
            WwiseExporterLogger.Log($"Target WWU (Events): {eventsWwuPath}");

            if (!File.Exists(actorMixerWwuPath))
                throw new FileNotFoundException("Default Work Unit.wwu not found in Actor-Mixer Hierarchy");
            if (!File.Exists(eventsWwuPath))
                throw new FileNotFoundException("Default Work Unit.wwu not found in Events");

            // 2. Load Work Units
            WwiseExporterLogger.Log("Loading XML documents...");
            var actorMixerDoc = XDocument.Load(actorMixerWwuPath);
            var eventsDoc = XDocument.Load(eventsWwuPath);

            // 3. Extract existing Sound objects from the Default Work Unit
            var soundMap = ExtractExistingSounds(actorMixerDoc);
            WwiseExporterLogger.Log($"Sound Map build complete. {soundMap.Count} sounds ready for re-parenting.");

            if (soundMap.Count == 0)
            {
                WwiseExporterLogger.LogError("CRITICAL: soundMap is empty! No sounds were found to organize.");
                throw new InvalidOperationException("No 'Sound' objects found in your Wwise project. " +
                    "Please make sure you have imported the .wav files into the 'Default Work Unit' of the Actor-Mixer Hierarchy and saved the project before running this export.");
            }

            // 4. Build Actor-Mixer Hierarchy
            _containerIdToGuid.Clear();
            var workUnitElement = actorMixerDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "WorkUnit");
            if (workUnitElement == null)
            {
                WwiseExporterLogger.LogError("Could not find 'WorkUnit' element in XML.");
                throw new InvalidOperationException("Could not find 'WorkUnit' element in Actor-Mixer Hierarchy");
            }

            WwiseExporterLogger.Log($"Found WorkUnit: {workUnitElement.Attribute("Name")?.Value} (ID: {workUnitElement.Attribute("ID")?.Value})");

            var childrenList = workUnitElement.Elements().FirstOrDefault(e => e.Name.LocalName == "ChildrenList");
            if (childrenList == null)
            {
                WwiseExporterLogger.Log("ChildrenList not found, creating new one.");
                childrenList = new XElement(workUnitElement.Name.Namespace + "ChildrenList");
                workUnitElement.Add(childrenList);
            }
            
            // Store references for event creation
            WwiseExporterLogger.Log("Extracting project references (Buses, Conversions)...");
            ExtractReferences(actorMixerDoc);
            
            WwiseExporterLogger.Log("Clearing existing children from Default Work Unit...");
            childrenList.RemoveAll();

            // Build Containers
            WwiseExporterLogger.Log("Building Actor-Mixer Hierarchy (Containers and Sounds)...");
            var containers = BuildActorMixerHierarchy(sections, eventNameLookup, soundMap, projectFolder);
            WwiseExporterLogger.Log($"Generated {containers.Count} top-level containers/sounds.");

            foreach (var container in containers)
            {
                childrenList.Add(container);
            }

            // 5. Handle Unreferenced Sounds
            var usedWemIds = GetUsedWemIds(sections);
            var unreferencedSounds = soundMap.Where(kvp => !usedWemIds.Contains(kvp.Key)).Select(kvp => kvp.Value).ToList();
            if (unreferencedSounds.Count > 0)
            {
                var unreferencedFolder = new XElement("Folder",
                    new XAttribute("Name", "Unreferenced_Sounds"),
                    new XAttribute("ID", GenerateGuid()),
                    new XAttribute("ShortID", GenerateShortId()),
                    new XElement("ChildrenList", unreferencedSounds));
                childrenList.Add(unreferencedFolder);
            }

            // 6. Save Actor-Mixer
            actorMixerDoc.Save(actorMixerWwuPath);

            // 7. Build Events
            var events = BuildEventsHierarchy(sections, eventNameLookup);
            ModifyEventsWorkUnit(eventsDoc, events);
            eventsDoc.Save(eventsWwuPath);

            // 8. Build SoundBanks
            WwiseExporterLogger.Log("Building SoundBanks Work Unit...");
            string soundBanksWwuPath = Path.Combine(projectFolder, "SoundBanks", "Default Work Unit.wwu");
            XDocument sbDoc;
            if (File.Exists(soundBanksWwuPath))
            {
                sbDoc = XDocument.Load(soundBanksWwuPath);
            }
            else
            {
                WwiseExporterLogger.Log("SoundBanks/Default Work Unit.wwu not found. Creating it...");
                Directory.CreateDirectory(Path.Combine(projectFolder, "SoundBanks"));
                sbDoc = CreateDefaultSoundBanksWorkUnit();
            }

            string eventsWuId = eventsDoc.Descendants("WorkUnit").FirstOrDefault()?.Attribute("ID")?.Value ?? "{00000000-0000-0000-0000-000000000000}";
            ModifySoundBanksWorkUnit(sbDoc, eventsWuId, events);
            sbDoc.Save(soundBanksWwuPath);

            // Patch conversion settings to Vorbis
            PatchConversionSettings(projectFolder);
        }

        private HashSet<uint> GetUsedWemIds(BNKSections sections)
        {
            var used = new HashSet<uint>();
            foreach (var s in sections.Sounds)
                used.Add(s.SourceId);
            return used;
        }

        private void PatchConversionSettings(string projectFolder)
        {
            string conversionWwuPath = Path.Combine(projectFolder, "Conversion Settings", "Default Work Unit.wwu");
            if (File.Exists(conversionWwuPath))
            {
                try
                {
                    var doc = XDocument.Load(conversionWwuPath);
                    var pluginInfos = doc.Descendants("ConversionPluginInfo").Where(e => e.Attribute("Platform")?.Value == "Windows");
                    bool modified = false;

                    foreach (var info in pluginInfos)
                    {
                        var plugin = info.Element("ConversionPlugin");
                        if (plugin != null && plugin.Attribute("PluginName")?.Value == "PCM")
                        {
                            plugin.SetAttributeValue("PluginName", "Vorbis");
                            plugin.SetAttributeValue("PluginID", "4");
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        doc.Save(conversionWwuPath);
                        WwiseExporterLogger.Log("Patched Default Conversion Settings to use Vorbis instead of PCM.");
                    }
                }
                catch (Exception ex)
                {
                    WwiseExporterLogger.LogError($"Failed to patch Conversion Settings: {ex.Message}");
                }
            }
        }

        private Dictionary<uint, XElement> ExtractExistingSounds(XDocument doc)
        {
            var soundMap = new Dictionary<uint, XElement>();
            WwiseExporterLogger.Log("--- STARTING SOUND EXTRACTION ---");

            // Use LocalName to ignore namespaces which often break XDocument searches in Wwise files
            var soundElements = doc.Descendants().Where(e => e.Name.LocalName == "Sound").ToList();
            WwiseExporterLogger.Log($"Found {soundElements.Count} raw 'Sound' elements in the WWU file.");

            foreach (var soundEl in soundElements)
            {
                string name = soundEl.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name))
                {
                    WwiseExporterLogger.Log("  Warning: Found Sound element with no Name attribute.");
                    continue;
                }

                // Strip extension if present (e.g. "899480.wav" -> "899480")
                string cleanName = Path.GetFileNameWithoutExtension(name);
                
                // Try to find a numeric ID in the name
                uint wemId = 0;
                bool foundId = false;

                if (uint.TryParse(cleanName, out wemId))
                {
                    foundId = true;
                }
                else
                {
                    // Try regex if simple parse fails (handles "Sound_899480" etc)
                    var match = System.Text.RegularExpressions.Regex.Match(cleanName, @"\d+");
                    if (match.Success && uint.TryParse(match.Value, out wemId))
                    {
                        foundId = true;
                    }
                }

                if (foundId)
                {
                    WwiseExporterLogger.Log($"  Extracted WEM ID {wemId} from Wwise object '{name}'");
                    var clone = new XElement(soundEl);
                    soundMap[wemId] = clone;
                }
                else
                {
                    WwiseExporterLogger.Log($"  Warning: Could not extract numeric ID from sound name '{name}'");
                }
            }

            WwiseExporterLogger.Log($"--- EXTRACTION COMPLETE: {soundMap.Count} unique IDs mapped ---");
            return soundMap;
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private void ExtractReferences(XDocument doc)
        {
            var workUnit = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "WorkUnit" && e.Attribute("Name")?.Value == "Default Work Unit");

            if (workUnit != null)
                _actorMixerWorkUnitId = workUnit.Attribute("ID")?.Value ?? "";

            var outputBusRef = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Reference" && e.Attribute("Name")?.Value == "OutputBus");

            if (outputBusRef != null)
            {
                var objRef = outputBusRef.Elements().FirstOrDefault(e => e.Name.LocalName == "ObjectRef");
                if (objRef != null)
                {
                    _masterBusId = objRef.Attribute("ID")?.Value ?? "{1514A4D8-1DA6-412A-A17E-75CA0C2149F3}";
                    _masterBusWorkUnitId = objRef.Attribute("WorkUnitID")?.Value ?? "";
                }
            }

            var conversionRef = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Reference" && e.Attribute("Name")?.Value == "Conversion");

            if (conversionRef != null)
            {
                var objRef = conversionRef.Elements().FirstOrDefault(e => e.Name.LocalName == "ObjectRef");
                if (objRef != null)
                {
                    _conversionSettingsId = objRef.Attribute("ID")?.Value ?? "";
                    _conversionSettingsWorkUnitId = objRef.Attribute("WorkUnitID")?.Value ?? "";
                }
            }
        }

        private List<XElement> BuildActorMixerHierarchy(
            BNKSections sections,
            Dictionary<uint, string> eventNameLookup,
            Dictionary<uint, XElement> soundMap,
            string projectFolder)
        {
            var containers = new List<XElement>();
            _containerIdToGuid.Clear();

            // Pre-build a full ID->name map so every object gets a meaningful name
            BuildObjectNameMap(sections, eventNameLookup);

            foreach (var bnkEvent in sections.Events)
            {
                foreach (var eventActionId in bnkEvent.EventIds)
                {
                    var eventAction = sections.EventActions.FirstOrDefault(ea => ea.SelfId == eventActionId);
                    if (eventAction == null) continue;

                    uint targetId = eventAction.SoundObjectId;
                    uint rootId = FindRootParent(targetId, sections);

                    // If this root has already been built (by another event), skip it at the top level
                    if (_containerIdToGuid.ContainsKey(rootId)) continue;

                    // Name comes from the pre-built map
                    string rootName = _objectNameMap.TryGetValue(rootId, out var n) ? n : $"Cnt_{rootId}";

                    var element = BuildHierarchyRecursive(rootId, sections, soundMap, projectFolder, rootName);
                    if (element != null)
                    {
                        containers.Add(element);
                    }
                }
            }

            return containers;
        }

        /// <summary>
        /// Walks every event->action->target chain and propagates the event name down
        /// to every reachable object (containers, child containers, sounds).
        /// Child containers that have their own event name keep that name;
        /// objects shared by multiple events keep the first name assigned.
        /// <summary>
        /// Walks every event->action->target chain and assigns human-readable names.
        /// Mixer: common prefix e.g. "Announcer_Female1"
        /// Container: event-specific suffix e.g. "AceBlue"
        /// Sounds: kept with their numeric source ID (no change)
        /// </summary>
        private void BuildObjectNameMap(BNKSections sections, Dictionary<uint, string> eventNameLookup)
        {
            _objectNameMap.Clear();

            foreach (var bnkEvent in sections.Events)
            {
                string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var n) ? n : $"Event_{bnkEvent.SelfId}";

                // Strip common game prefixes: "Play_vo_", "Play_"
                string stripped = eventName
                    .Replace("Play_vo_", "")
                    .Replace("Play_", "");

                // Split into parts: ["Announcer", "Female1", "AceBlue"]
                var parts = stripped.Split('_');

                // Container name = last segment (e.g. "AceBlue")
                string containerName = parts[parts.Length - 1];

                // Mixer name = everything except the last segment (e.g. "Announcer_Female1")
                string mixerName = parts.Length > 1
                    ? string.Join("_", parts, 0, parts.Length - 1)
                    : stripped;

                foreach (var actionId in bnkEvent.EventIds)
                {
                    var action = sections.EventActions.FirstOrDefault(a => a.SelfId == actionId);
                    if (action == null) continue;

                    uint rootId = FindRootParent(action.SoundObjectId, sections);
                    PropagateNameDown(rootId, containerName, mixerName, sections, new HashSet<uint>(), isRoot: true);
                }
            }
        }

        private void PropagateNameDown(uint id, string containerName, string mixerName, BNKSections sections, HashSet<uint> visited, bool isRoot = false)
        {
            if (!visited.Add(id)) return;

            // Only assign if not already named (first event wins)
            if (!_objectNameMap.ContainsKey(id))
            {
                // We use the ID as a suffix to GUARANTEE uniqueness and stop all "NameConflict" errors.
                if (sections.ActorMixers.Any(a => a.SelfId == id))
                {
                    // Root Mixer gets the full event context, nested ones are just "Mixer_ID"
                    _objectNameMap[id] = isRoot ? $"Root_{containerName}_{id}" : $"Mixer_{id}";
                }
                else if (sections.RandomContainers.Any(r => r.SelfId == id) || 
                         sections.SwitchContainers.Any(s => s.SelfId == id))
                {
                    // Containers are named after the event + ID
                    _objectNameMap[id] = $"{containerName}_{id}";
                }
                // Sounds: intentionally not renamed — they keep their numeric IDs
            }

            // Recurse into children
            var rc = sections.RandomContainers.FirstOrDefault(r => r.SelfId == id);
            if (rc != null)
            {
                var childIds = rc.ChildrenIds.Count > 0 ? rc.ChildrenIds : rc.SoundIds;
                foreach (var childId in childIds)
                {
                    // Sounds keep their IDs, don't rename them here
                    if (sections.Sounds.Any(s => s.SelfId == childId)) continue;
                    PropagateNameDown(childId, containerName, mixerName, sections, visited);
                }
                return;
            }

            var sc = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == id);
            if (sc != null)
            {
                foreach (var childId in sc.Children)
                {
                    if (sections.Sounds.Any(s => s.SelfId == childId)) continue;
                    PropagateNameDown(childId, containerName, mixerName, sections, visited);
                }
                return;
            }

            var am = sections.ActorMixers.FirstOrDefault(a => a.SelfId == id);
            if (am != null)
            {
                foreach (var childId in am.Children)
                {
                    if (sections.Sounds.Any(s => s.SelfId == childId)) continue;
                    PropagateNameDown(childId, containerName, mixerName, sections, visited);
                }
            }
        }

        private uint FindRootParent(uint targetId, BNKSections sections)
        {
            uint currentId = targetId;
            while (true)
            {
                uint parentId = 0;

                // Check Sound
                var s = sections.Sounds.FirstOrDefault(x => x.SelfId == currentId);
                if (s != null) parentId = s.NodeBaseParams.DirectParentId;
                else
                {
                    // Check Random Container
                    var r = sections.RandomContainers.FirstOrDefault(x => x.SelfId == currentId);
                    if (r != null) parentId = r.NodeBaseParams.DirectParentId;
                    else
                    {
                        // Check Actor Mixer
                        var a = sections.ActorMixers.FirstOrDefault(x => x.SelfId == currentId);
                        if (a != null) parentId = a.NodeBaseParams.DirectParentId;
                        else
                        {
                            // Check Switch Container
                            var sw = sections.SwitchContainers.FirstOrDefault(x => x.SelfId == currentId);
                            if (sw != null) parentId = sw.NodeBaseParams.DirectParentId;
                        }
                    }
                }

                if (parentId == 0 || parentId == currentId) break;
                
                // Ensure the parent actually exists in our sections
                bool parentExists = sections.Sounds.Any(x => x.SelfId == parentId) ||
                                    sections.RandomContainers.Any(x => x.SelfId == parentId) ||
                                    sections.ActorMixers.Any(x => x.SelfId == parentId) ||
                                    sections.SwitchContainers.Any(x => x.SelfId == parentId);

                if (!parentExists) break;

                currentId = parentId;
            }
            return currentId;
        }

        private HashSet<uint> _recursionStack = new HashSet<uint>();

        private XElement BuildHierarchyRecursive(uint targetId, BNKSections sections, Dictionary<uint, XElement> soundMap, string projectFolder, string suggestedName = null)
        {
            if (_recursionStack.Contains(targetId))
            {
                WwiseExporterLogger.Log($"Cycle detected for ID {targetId}, skipping duplication.");
                return null;
            }

            _recursionStack.Add(targetId);
            try
            {
                // 1. Is it a Sound?
                var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == targetId);
                if (sound != null)
                {
                    string soundGuid = GenerateGuid();
                    // We still store the GUID, but subsequent ones will overwrite it for Event mapping.
                    // Usually events point to the 'main' instance or a container.
                    _containerIdToGuid[targetId] = soundGuid;
                    
                    if (soundMap.TryGetValue(sound.SourceId, out var existingSoundEl))
                    {
                        var element = RepairSoundElement(new XElement(existingSoundEl), sound.SourceId, projectFolder, sound.SelfId.ToString());
                        element.SetAttributeValue("ID", soundGuid); // Ensure unique GUID
                        ApplyNodeProperties(element, sound.NodeBaseParams);
                        return element;
                    }
                    else
                    {
                        var element = CreateSoundElement(sound.SourceId, sound.SelfId, projectFolder, soundGuid, sound.SelfId.ToString());
                        ApplyNodeProperties(element, sound.NodeBaseParams);
                        return element;
                    }
                }

                // 2. Is it a Random Container?
                var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == targetId);
                if (randomContainer != null)
                {
                    string containerGuid = GenerateGuid();
                    _containerIdToGuid[targetId] = containerGuid;
                    
                    string containerName = suggestedName ?? (_objectNameMap.TryGetValue(targetId, out var rcName) ? rcName : $"Container_{targetId}");
                    return CreateRandomSequenceContainer(containerName, containerGuid, targetId.ToString(), randomContainer, sections, soundMap, projectFolder);
                }

                // 3. Is it a Switch Container?
                var switchContainer = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == targetId);
                if (switchContainer != null)
                {
                    string containerGuid = GenerateGuid();
                    _containerIdToGuid[targetId] = containerGuid;
                    
                    string containerName = suggestedName ?? (_objectNameMap.TryGetValue(targetId, out var swName) ? swName : $"Switch_{targetId}");
                    return CreateSwitchContainer(containerName, containerGuid, targetId.ToString(), switchContainer, sections, soundMap, projectFolder);
                }

                // 4. Is it an Actor Mixer?
                var actorMixer = sections.ActorMixers.FirstOrDefault(a => a.SelfId == targetId);
                if (actorMixer != null)
                {
                    string containerGuid = GenerateGuid();
                    _containerIdToGuid[targetId] = containerGuid;
                    
                    string containerName = suggestedName ?? (_objectNameMap.TryGetValue(targetId, out var amName) ? amName : $"Mixer_{targetId}");
                    return CreateActorMixer(containerName, containerGuid, targetId.ToString(), actorMixer, sections, soundMap, projectFolder);
                }

                return null;
            }
            finally
            {
                _recursionStack.Remove(targetId);
            }
        }

        private XElement CreateRandomSequenceContainer(
            string name,
            string guid,
            string shortId,
            RandomContainer container,
            BNKSections sections,
            Dictionary<uint, XElement> soundMap,
            string projectFolder)
        {
            var childrenList = new XElement("ChildrenList");
            WwiseExporterLogger.Log($"Building Container '{name}' (ID: {container.SelfId})");

            var childIds = container.ChildrenIds.Count > 0
                ? container.ChildrenIds.ToList()
                : container.SoundIds.ToList();

            // Ensure all items in the playlist are also treated as children
            if (container.Playlist != null)
            {
                foreach (var item in container.Playlist)
                {
                    if (!childIds.Contains(item.Id))
                    {
                        childIds.Add(item.Id);
                    }
                }
            }

            foreach (var childId in childIds)
            {
                var childElement = BuildHierarchyRecursive(childId, sections, soundMap, projectFolder);
                if (childElement != null)
                {
                    // Apply weight if it exists in the playlist
                    var playlistItem = container.Playlist.FirstOrDefault(p => p.Id == childId);
                    if (playlistItem != null)
                    {
                        var childPropList = childElement.Element("PropertyList");
                        if (childPropList == null)
                        {
                            childPropList = new XElement("PropertyList");
                            childElement.AddFirst(childPropList);
                        }
                        childPropList.Add(new XElement("Property",
                            new XAttribute("Name", "Weight"),
                            new XAttribute("Type", "Real64"),
                            new XAttribute("Value", (playlistItem.Weight / 1000.0).ToString("F1"))));
                    }
                    childrenList.Add(childElement);
                }
            }

            // Re-apply properties from BNK data
            var propList = new XElement("PropertyList",
                new XElement("Property",
                    new XAttribute("Name", "RandomOrSequence"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", container.Mode.ToString())), // 0 = Random, 1 = Sequence
                new XElement("Property",
                    new XAttribute("Name", "ContainerModeRandom"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", container.RandomMode.ToString())), // 1 = Shuffle
                new XElement("Property",
                    new XAttribute("Name", "NormalOrContinuous"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", ((container.byBitVector >> 3) & 1).ToString())), // bit3 bIsContinuous
                new XElement("Property",
                    new XAttribute("Name", "TransitionTime"),
                    new XAttribute("Type", "Real64"),
                    new XAttribute("Value", container.TransitionTime.ToString("F1"))),
                new XElement("Property",
                    new XAttribute("Name", "AvoidRepeatCount"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", container.AvoidRepeatCount.ToString())),
                new XElement("Property",
                    new XAttribute("Name", "ResetPlayListAtEachPlay"),
                    new XAttribute("Type", "bool"),
                    new XAttribute("Value", ((container.byBitVector >> 1) & 1) == 1 ? "True" : "False")),
                new XElement("Property",
                    new XAttribute("Name", "Scope"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", ((container.byBitVector >> 4) & 1).ToString()))); // 0 = Game Object, 1 = Global

            var containerElement = new XElement("RandomSequenceContainer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                propList,
                new XElement("ReferenceList",
                    CreateOutputBusReference()),
                childrenList);

            // Reconstruct Playlist with simple ItemRefs (no children/attributes)
            if (container.Playlist != null && container.Playlist.Count > 0)
            {
                var playlistElement = new XElement("Playlist");
                foreach (var item in container.Playlist)
                {
                    if (_containerIdToGuid.TryGetValue(item.Id, out var childGuid))
                    {
                        playlistElement.Add(new XElement("ItemRef",
                            new XAttribute("Name", item.Id.ToString()),
                            new XAttribute("ID", childGuid)));
                    }
                }
                containerElement.Add(playlistElement);
            }

            ApplyNodeProperties(containerElement, container.NodeBaseParams);
            return containerElement;
        }



        /// <summary>
        /// Create a Sound element programmatically
        /// </summary>
        private XElement CreateSoundElement(uint wemId, uint soundId, string projectFolder, string soundGuid = null, string soundShortId = null)
        {
            soundGuid = soundGuid ?? GenerateGuid();
            soundShortId = soundShortId ?? GenerateShortId();
            string audioSourceGuid = GenerateGuid();
            string wemName = wemId.ToString();

            var soundElement = new XElement("Sound",
                new XAttribute("Name", wemName),
                new XAttribute("ID", soundGuid),
                new XAttribute("ShortID", soundShortId),
                new XElement("ReferenceList",
                    CreateOutputBusReference(),
                    CreateConversionReference()),
                new XElement("ChildrenList",
                    new XElement("AudioFileSource",
                        new XAttribute("Name", wemName),
                        new XAttribute("ID", audioSourceGuid),
                        new XAttribute("ShortID", wemId.ToString()), // CRITICAL: this becomes the SourceId in the compiled BNK
                        new XElement("Language", "SFX"),
                        new XElement("AudioFile", $"{wemId}.wav"),
                        new XElement("MediaIDList",
                            new XElement("MediaID", new XAttribute("ID", wemId.ToString()))))),
                new XElement("ActiveSourceList",
                    new XElement("ActiveSource",
                        new XAttribute("Name", wemName),
                        new XAttribute("ID", audioSourceGuid),
                        new XAttribute("Platform", "Linked"))));

            return RepairSoundElement(soundElement, wemId, projectFolder);
        }




        private XElement RepairSoundElement(XElement soundEl, uint wemId, string projectPath, string soundShortId = null)
        {
            if (!string.IsNullOrEmpty(soundShortId))
            {
                soundEl.SetAttributeValue("ShortID", soundShortId);
            }

            // Find AudioFile element
            var audioFileEl = soundEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "AudioFile");
            if (audioFileEl == null) return soundEl;

            // Search for the file in Voices first, then SFX
            string voicesPath = $"Voices\\English(US)\\{wemId}.wav";
            string sfxPath = $"SFX\\{wemId}.wav";
            
            string fullVoicesPath = Path.Combine(projectPath, "Originals", voicesPath);
            string fullSfxPath = Path.Combine(projectPath, "Originals", sfxPath);

            if (File.Exists(fullSfxPath))
            {
                audioFileEl.Value = $"{wemId}.wav"; // Just the filename
                // Also update Language if it exists
                var langEl = audioFileEl.Parent?.Elements().FirstOrDefault(e => e.Name.LocalName == "Language");
                if (langEl != null) langEl.Value = "SFX";
            }
            else if (File.Exists(fullVoicesPath))
            {
                audioFileEl.Value = $"{wemId}.wav"; // Just the filename
                var langEl = audioFileEl.Parent?.Elements().FirstOrDefault(e => e.Name.LocalName == "Language");
                if (langEl != null) langEl.Value = "English(US)";
            }

            // CRITICAL: Force ShortID on AudioFileSource to the wemId.
            // This is what Wwise uses as SourceId when compiling the BNK.
            var audioFileSource = audioFileEl.Parent;
            if (audioFileSource != null)
            {
                audioFileSource.SetAttributeValue("ShortID", wemId.ToString());
                
                // CRITICAL: Wwise 2022.1 uses MediaIDList to store the compiled SourceId.
                // If we don't force this, Wwise generates a random ID.
                var mediaIdList = audioFileSource.Elements().FirstOrDefault(e => e.Name.LocalName == "MediaIDList");
                if (mediaIdList == null)
                {
                    mediaIdList = new XElement("MediaIDList");
                    audioFileSource.Add(mediaIdList);
                }
                
                var mediaId = mediaIdList.Elements().FirstOrDefault(e => e.Name.LocalName == "MediaID");
                if (mediaId == null)
                {
                    mediaId = new XElement("MediaID");
                    mediaIdList.Add(mediaId);
                }
                mediaId.SetAttributeValue("ID", wemId.ToString());
            }

            // CRITICAL: Ensure every sound has a unique GUID in the hierarchy to avoid ObjectIDConflict
            soundEl.SetAttributeValue("ID", GenerateGuid());

            // Force IsStreamingEnabled to True so Wwise doesn't require media to be embedded in a SoundBank
            var propertyList = soundEl.Elements().FirstOrDefault(e => e.Name.LocalName == "PropertyList");
            if (propertyList == null)
            {
                propertyList = new XElement("PropertyList");
                // Add after Name/ID attributes, usually at start of elements
                soundEl.AddFirst(propertyList);
            }

            var streamingProp = propertyList.Elements().FirstOrDefault(p => p.Name.LocalName == "Property" && p.Attribute("Name")?.Value == "IsStreamingEnabled");
            if (streamingProp == null)
            {
                propertyList.Add(new XElement("Property",
                    new XAttribute("Name", "IsStreamingEnabled"),
                    new XAttribute("Type", "bool"),
                    new XAttribute("Value", "True")
                ));
            }
            else
            {
                streamingProp.SetAttributeValue("Value", "True");
            }

            // Ensure the sound has an OutputBus reference
            var refList = soundEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ReferenceList");
            if (refList == null)
            {
                refList = new XElement("ReferenceList");
                soundEl.Add(refList);
            }
            
            if (!refList.Elements().Any(e => e.Attribute("Name")?.Value == "OutputBus"))
            {
                refList.Add(CreateOutputBusReference());
            }

            if (!refList.Elements().Any(e => e.Attribute("Name")?.Value == "Conversion"))
            {
                refList.Add(CreateConversionReference());
            }

            // Ensure ActiveSource points to the correct AudioFileSource ID
            string newSourceGuid = GenerateGuid();
            if (audioFileSource != null)
            {
                audioFileSource.SetAttributeValue("ID", newSourceGuid);
            }
            
            var activeSourceList = soundEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ActiveSourceList");
            if (activeSourceList != null)
            {
                var activeSource = activeSourceList.Elements().FirstOrDefault(e => e.Name.LocalName == "ActiveSource");
                if (activeSource != null)
                {
                    activeSource.SetAttributeValue("ID", newSourceGuid);
                    activeSource.SetAttributeValue("Platform", "Linked");
                }
            }

            return soundEl;
        }

        private XElement CreateOutputBusReference()
        {
            var reference = new XElement("Reference",
                new XAttribute("Name", "OutputBus"));

            if (!string.IsNullOrEmpty(_masterBusId))
            {
                var objRef = new XElement("ObjectRef",
                    new XAttribute("Name", "Master Audio Bus"),
                    new XAttribute("ID", _masterBusId));

                if (!string.IsNullOrEmpty(_masterBusWorkUnitId))
                    objRef.Add(new XAttribute("WorkUnitID", _masterBusWorkUnitId));

                reference.Add(objRef);
            }
            else
            {
                // Fallback to a standard Master Audio Bus ID if detection failed
                reference.Add(new XElement("ObjectRef",
                    new XAttribute("Name", "Master Audio Bus"),
                    new XAttribute("ID", "{1514A4D8-1DA6-412A-A17E-75CA0C2149F3}"),
                    new XAttribute("WorkUnitID", "{369ABE4C-65A8-4E42-B131-19EA9DCE2363}")));
            }

            return reference;
        }

        private XElement CreateConversionReference()
        {
            var reference = new XElement("Reference",
                new XAttribute("Name", "Conversion"));

            if (!string.IsNullOrEmpty(_conversionSettingsId))
            {
                var objRef = new XElement("ObjectRef",
                    new XAttribute("Name", "Default Conversion Settings"),
                    new XAttribute("ID", _conversionSettingsId));

                if (!string.IsNullOrEmpty(_conversionSettingsWorkUnitId))
                    objRef.Add(new XAttribute("WorkUnitID", _conversionSettingsWorkUnitId));

                reference.Add(objRef);
            }
            else
            {
                // Fallback to a standard Default Conversion Settings ID if detection failed
                reference.Add(new XElement("ObjectRef",
                    new XAttribute("Name", "Default Conversion Settings"),
                    new XAttribute("ID", "{6D1B890C-9826-4384-BF07-C15223E9FB56}"),
                    new XAttribute("WorkUnitID", "{F68BC201-9040-4209-A45F-53C79A46C444}")));
            }

            return reference;
        }

        private void ApplyNodeProperties(XElement element, NodeBaseParams nbp)
        {
            if (nbp == null) return;

            var propertyList = element.Element("PropertyList");
            if (propertyList == null)
            {
                propertyList = new XElement("PropertyList");
                element.AddFirst(propertyList);
            }

            // Map BNK Property IDs to Wwise XML Property Names
            foreach (var prop in nbp.Props)
            {
                string propName = prop.ID switch
                {
                    0x06 => element.Name.LocalName == "Sound" ? "Volume" : "MakeUpGain",
                    0x07 => "Priority",
                    0x02 => "Pitch",
                    0x04 => "LFEUsage",
                    _ => null
                };

                if (propName != null)
                {
                    // Ensure we don't add duplicate properties
                    var existing = propertyList.Elements("Property").FirstOrDefault(e => e.Attribute("Name")?.Value == propName);
                    if (existing != null)
                    {
                        existing.SetAttributeValue("Value", prop.Value.ToString("F1"));
                    }
                    else
                    {
                        propertyList.Add(new XElement("Property",
                            new XAttribute("Name", propName),
                            new XAttribute("Type", "Real64"),
                            new XAttribute("Value", prop.Value.ToString("F1"))));
                    }
                }
            }

            // Map BNK Integer Property IDs
            foreach (var prop in nbp.PropsInt)
            {
                // Currently no specific integer properties to map for announcer, 
                // but we'll add the generic structure to be safe.
                string propName = prop.ID switch
                {
                    _ => null
                };

                if (propName != null)
                {
                    var existing = propertyList.Elements("Property").FirstOrDefault(e => e.Attribute("Name")?.Value == propName);
                    if (existing != null)
                    {
                        existing.SetAttributeValue("Value", prop.Value.ToString());
                    }
                    else
                    {
                        propertyList.Add(new XElement("Property",
                            new XAttribute("Name", propName),
                            new XAttribute("Type", "int32"),
                            new XAttribute("Value", prop.Value.ToString())));
                    }
                }
            }

            // AdvSettingsParams
            if ((nbp.AdvBits & 0x01) != 0) // bit0 = Kill Newest
            {
                // In Wwise XML, this corresponds to MaxReachedBehavior = 1 (Kill Newest)
                // and we must also enable the limit (MaxReachedBehavior = 1).
                // Wait, MaxReachedBehavior = 1 is the ENUM for Kill Newest.
                
                SetProperty(propertyList, "MaxReachedBehavior", "int16", "1"); // Kill Newest
                SetProperty(propertyList, "OverLimitBehavior", "int16", "1");  // Kill Newest
            }

            if (nbp.BelowThresholdBehavior != 0) // 1 = Kill Voice
            {
                SetProperty(propertyList, "BelowThresholdBehavior", "int16", nbp.BelowThresholdBehavior.ToString());
            }

            if (nbp.VirtualQueueBehavior != 0)
            {
                SetProperty(propertyList, "VirtualVoiceQueueBehavior", "int16", nbp.VirtualQueueBehavior.ToString());
            }

            if (nbp.MaxNumInstance > 0)
            {
                SetProperty(propertyList, "MaxInstances", "int16", nbp.MaxNumInstance.ToString());
            }
        }

        private void ModifyActorMixerWorkUnit(XDocument doc, List<XElement> containers)
        {
            var workUnit = doc.Descendants("WorkUnit")
                .FirstOrDefault(wu => wu.Attribute("Name")?.Value == "Default Work Unit");

            if (workUnit == null) return;

            var childrenList = workUnit.Element("ChildrenList");
            if (childrenList == null)
            {
                childrenList = new XElement("ChildrenList");
                workUnit.Add(childrenList);
            }

            // Clear existing children to start fresh
            childrenList.RemoveAll();

            // Add all containers
            foreach (var container in containers)
            {
                childrenList.Add(container);
            }
        }

        private List<XElement> BuildEventsHierarchy(
            BNKSections sections,
            Dictionary<uint, string> eventNameLookup)
        {
            var events = new List<XElement>();

            foreach (var bnkEvent in sections.Events)
            {
                string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var name)
                    ? name
                    : $"Event_{bnkEvent.SelfId}";

                string eventGuid = GenerateGuid();
                string eventShortId = bnkEvent.SelfId.ToString();

                var actionsList = new XElement("ChildrenList");

                foreach (var eventActionId in bnkEvent.EventIds)
                {
                    var eventAction = sections.EventActions.FirstOrDefault(ea => ea.SelfId == eventActionId);
                    if (eventAction == null) continue;

                    if (_containerIdToGuid.TryGetValue(eventAction.SoundObjectId, out var containerGuid))
                    {
                        var actionElement = CreatePlayAction(eventName, containerGuid, eventAction.SelfId.ToString(), eventAction.FadeCurve);
                        actionsList.Add(actionElement);
                    }
                }

                if (actionsList.HasElements)
                {
                    var eventElement = new XElement("Event",
                        new XAttribute("Name", eventName),
                        new XAttribute("ID", eventGuid),
                        new XAttribute("ShortID", eventShortId),
                        actionsList);

                    events.Add(eventElement);
                }
            }

            return events;
        }

        private XElement CreatePlayAction(string targetName, string targetGuid, string actionShortId, byte fadeCurve = 4)
        {
            string actionGuid = GenerateGuid();
            
            var propList = new XElement("PropertyList",
                new XElement("Property",
                    new XAttribute("Name", "ActionType"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", "1"))); // 1 = Play

            if (fadeCurve != 4) // Default is Linear (4), but BNK might have others
            {
                propList.Add(new XElement("Property",
                    new XAttribute("Name", "FadeCurve"),
                    new XAttribute("Type", "int16"),
                    new XAttribute("Value", (fadeCurve & 0x07).ToString())));
            }

            return new XElement("Action",
                new XAttribute("Name", ""),
                new XAttribute("ID", actionGuid),
                new XAttribute("ShortID", actionShortId),
                propList,
                new XElement("ReferenceList",
                    new XElement("Reference",
                        new XAttribute("Name", "Target"),
                        new XElement("ObjectRef",
                            new XAttribute("Name", targetName),
                            new XAttribute("ID", targetGuid),
                            new XAttribute("WorkUnitID", _actorMixerWorkUnitId)))));
        }

        private void ModifyEventsWorkUnit(XDocument doc, List<XElement> events)
        {
            var workUnit = doc.Descendants("WorkUnit")
                .FirstOrDefault(wu => wu.Attribute("Name")?.Value == "Default Work Unit");

            if (workUnit == null) return;

            var childrenList = workUnit.Element("ChildrenList");
            if (childrenList == null)
            {
                childrenList = new XElement("ChildrenList");
                workUnit.Add(childrenList);
            }

            // Clear existing events
            childrenList.RemoveAll();

            // Add new events
            foreach (var eventElement in events)
            {
                childrenList.Add(eventElement);
            }
        }

        private XDocument CreateDefaultSoundBanksWorkUnit()
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("WwiseDocument",
                    new XAttribute("Type", "WorkUnit"),
                    new XAttribute("ID", "{" + Guid.NewGuid().ToString().ToUpper() + "}"),
                    new XAttribute("SchemaVersion", "110"),
                    new XElement("SoundBanks",
                        new XElement("WorkUnit",
                            new XAttribute("Name", "Default Work Unit"),
                            new XAttribute("ID", "{" + Guid.NewGuid().ToString().ToUpper() + "}"),
                            new XAttribute("PersistMode", "Standalone")
                        )
                    )
                )
            );
        }

        private void ModifySoundBanksWorkUnit(XDocument doc, string eventsWorkUnitId, List<XElement> events)
        {
            var workUnit = doc.Descendants("WorkUnit")
                .FirstOrDefault(wu => wu.Attribute("Name")?.Value == "Default Work Unit");

            if (workUnit == null) return;

            var childrenList = workUnit.Element("ChildrenList");
            if (childrenList == null)
            {
                childrenList = new XElement("ChildrenList");
                workUnit.Add(childrenList);
            }

            // Check if the soundbank already exists
            string bankName = "announcer_global_female1_vo_events";
            var existingBank = childrenList.Elements("SoundBank").FirstOrDefault(sb => sb.Attribute("Name")?.Value == bankName);
            if (existingBank != null)
            {
                existingBank.Remove();
            }

            string bankGuid = GenerateGuid();
            // SoundBank ShortID: Wwise uses FNV32 hash of the bank name.
            // Hash of "announcer_global_female1_vo_events" = 2707607260
            // Using this exact value ensures compatibility with the game engine.
            string bankShortId = "2707607260";

            var inclusionList = new XElement("ObjectInclusionList");
            foreach (var evt in events)
            {
                inclusionList.Add(new XElement("ObjectRef",
                    new XAttribute("Name", evt.Attribute("Name")?.Value ?? "UnknownEvent"),
                    new XAttribute("ID", evt.Attribute("ID")?.Value ?? ""),
                    new XAttribute("WorkUnitID", eventsWorkUnitId),
                    new XAttribute("Origin", "Manual"),
                    new XAttribute("Filter", "3") // 3 = Events and Structures only (Media unchecked)
                ));
            }

            var soundBankElement = new XElement("SoundBank",
                new XAttribute("Name", bankName),
                new XAttribute("ID", bankGuid),
                new XAttribute("ShortID", bankShortId),
                new XElement("PropertyList",
                    new XElement("Property",
                        new XAttribute("Name", "UseStreamingManager"),
                        new XAttribute("Type", "bool"),
                        new XAttribute("Value", "True"))),
                inclusionList,
                new XElement("ObjectExclusionList"),
                new XElement("GameSyncExclusionList")
            );

            childrenList.Add(soundBankElement);
        }

        private string GenerateGuid()
        {
            return Guid.NewGuid().ToString("B").ToUpper();
        }

        private static readonly Random _random = new Random();
        private static readonly HashSet<uint> _usedShortIds = new HashSet<uint>();

        private string GenerateShortId()
        {
            uint shortId;
            do
            {
                shortId = (uint)_random.Next(100000000, int.MaxValue);
            } while (_usedShortIds.Contains(shortId));

            _usedShortIds.Add(shortId);
            return shortId.ToString();
        }

        public static bool ValidateWwiseProject(string folderPath, out string error)
        {
            error = "";

            if (!Directory.Exists(folderPath))
            {
                error = "Folder does not exist";
                return false;
            }

            var wprojFiles = Directory.GetFiles(folderPath, "*.wproj");
            if (wprojFiles.Length == 0)
            {
                error = "No .wproj file found.";
                return false;
            }

            string actorMixerPath = Path.Combine(folderPath, "Actor-Mixer Hierarchy");
            if (!Directory.Exists(actorMixerPath))
            {
                error = "Actor-Mixer Hierarchy folder not found";
                return false;
            }

            string eventsPath = Path.Combine(folderPath, "Events");
            if (!Directory.Exists(eventsPath))
            {
                error = "Events folder not found";
                return false;
            }

            string defaultWwu = Path.Combine(actorMixerPath, "Default Work Unit.wwu");
            if (!File.Exists(defaultWwu))
            {
                error = "Default Work Unit.wwu not found in Actor-Mixer Hierarchy";
                return false;
            }

            return true;
        }

        private XElement CreateSwitchContainer(
            string name,
            string guid,
            string shortId,
            SwitchContainer container,
            BNKSections sections,
            Dictionary<uint, XElement> soundMap,
            string projectFolder)
        {
            var childrenList = new XElement("ChildrenList");
            
            var childIds = container.Children.ToList();
            
            // Ensure all items in switch groups are also treated as children
            if (container.SwitchGroups != null)
            {
                foreach (var group in container.SwitchGroups)
                {
                    foreach (var itemId in group.Items)
                    {
                        if (!childIds.Contains(itemId))
                        {
                            childIds.Add(itemId);
                        }
                    }
                }
            }

            foreach (var childId in childIds)
            {
                var childElement = BuildHierarchyRecursive(childId, sections, soundMap, projectFolder);
                if (childElement != null)
                {
                    childrenList.Add(childElement);
                }
            }

            var containerElement = new XElement("SwitchContainer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                new XElement("ReferenceList",
                    CreateOutputBusReference()),
                childrenList);

            ApplyNodeProperties(containerElement, container.NodeBaseParams);
            return containerElement;
        }

        private XElement CreateActorMixer(
            string name,
            string guid,
            string shortId,
            ActorMixer container,
            BNKSections sections,
            Dictionary<uint, XElement> soundMap,
            string projectFolder)
        {
            var childrenList = new XElement("ChildrenList");
            
            var childIds = container.Children;
            foreach (var childId in childIds)
            {
                var childElement = BuildHierarchyRecursive(childId, sections, soundMap, projectFolder);
                if (childElement != null)
                {
                    childrenList.Add(childElement);
                }
            }

            var containerElement = new XElement("ActorMixer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                new XElement("ReferenceList",
                    CreateOutputBusReference()),
                childrenList);

            ApplyNodeProperties(containerElement, container.NodeBaseParams);
            return containerElement;
        }

        private void SetProperty(XElement propertyList, string name, string type, string value)
        {
            var p = propertyList.Elements("Property").FirstOrDefault(e => e.Attribute("Name")?.Value == name);
            if (p != null)
            {
                p.SetAttributeValue("Value", value);
            }
            else
            {
                propertyList.Add(new XElement("Property",
                    new XAttribute("Name", name),
                    new XAttribute("Type", type),
                    new XAttribute("Value", value)));
            }
        }
    }
}
