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
            
            WwiseExporterLogger.Log("Clearing existing children from Default Work Unit...");
            childrenList.RemoveAll();

            // Store references for event creation
            WwiseExporterLogger.Log("Extracting project references (Buses, Conversions)...");
            ExtractReferences(actorMixerDoc);

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
        }

        private HashSet<uint> GetUsedWemIds(BNKSections sections)
        {
            var used = new HashSet<uint>();
            foreach (var s in sections.Sounds)
                used.Add(s.SourceId);
            return used;
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

            foreach (var bnkEvent in sections.Events)
            {
                string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var name)
                    ? name
                    : $"Event_{bnkEvent.SelfId}";

                foreach (var eventActionId in bnkEvent.EventIds)
                {
                    var eventAction = sections.EventActions.FirstOrDefault(ea => ea.SelfId == eventActionId);
                    if (eventAction == null) continue;

                    var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == eventAction.SoundObjectId);

                    if (randomContainer != null)
                    {
                        if (_containerIdToGuid.ContainsKey(randomContainer.SelfId))
                            continue;

                        string containerGuid = GenerateGuid();
                        string containerShortId = GenerateShortId();
                        _containerIdToGuid[randomContainer.SelfId] = containerGuid;

                        var containerElement = CreateRandomSequenceContainer(
                            eventName,
                            containerGuid,
                            containerShortId,
                            randomContainer,
                            sections,
                            soundMap,
                            projectFolder);

                        containers.Add(containerElement);
                    }
                    else
                    {
                        var switchContainer = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == eventAction.SoundObjectId);
                        if (switchContainer != null)
                        {
                            if (_containerIdToGuid.ContainsKey(switchContainer.SelfId))
                                continue;

                            string containerGuid = GenerateGuid();
                            string containerShortId = GenerateShortId();
                            _containerIdToGuid[switchContainer.SelfId] = containerGuid;

                            var containerElement = CreateSwitchContainer(
                                eventName,
                                containerGuid,
                                containerShortId,
                                switchContainer,
                                sections,
                                soundMap,
                                projectFolder);

                            containers.Add(containerElement);
                        }
                        else
                        {
                            var actorMixer = sections.ActorMixers.FirstOrDefault(a => a.SelfId == eventAction.SoundObjectId);
                            if (actorMixer != null)
                            {
                                if (_containerIdToGuid.ContainsKey(actorMixer.SelfId))
                                    continue;

                                string containerGuid = GenerateGuid();
                                string containerShortId = GenerateShortId();
                                _containerIdToGuid[actorMixer.SelfId] = containerGuid;

                                var containerElement = CreateActorMixer(
                                    eventName,
                                    containerGuid,
                                    containerShortId,
                                    actorMixer,
                                    sections,
                                    soundMap,
                                    projectFolder);

                                containers.Add(containerElement);
                            }
                            else
                            {
                                var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == eventAction.SoundObjectId);
                                if (sound != null)
                                {
                                    if (_containerIdToGuid.ContainsKey(eventAction.SoundObjectId))
                                        continue;

                                    string containerGuid = GenerateGuid();
                                    string containerShortId = GenerateShortId();
                                    _containerIdToGuid[eventAction.SoundObjectId] = containerGuid;

                                    var containerElement = CreateRandomSequenceContainerForSingleSound(
                                        eventName,
                                        containerGuid,
                                        containerShortId,
                                        sound,
                                        soundMap,
                                        sections,
                                        projectFolder);

                                    containers.Add(containerElement);
                                }
                            }
                        }
                    }
                }
            }

            return containers;
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
            var childrenList = new XElement(XName.Get("ChildrenList", guid.Contains("{") ? "" : ""));
            WwiseExporterLogger.Log($"Building Container '{name}' (ID: {container.SelfId})");

            var childIds = container.ChildrenIds.Count > 0
                ? container.ChildrenIds
                : container.SoundIds;

            foreach (var childId in childIds)
            {
                var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == childId);
                if (sound != null)
                {
                    if (soundMap.TryGetValue(sound.SourceId, out var soundElement))
                    {
                        WwiseExporterLogger.Log($"  + Adding Sound {sound.SourceId} (SelfId: {childId})");
                        var repairedSound = RepairSoundElement(new XElement(soundElement), sound.SourceId, projectFolder);
                        childrenList.Add(repairedSound);
                        continue;
                    }
                    else
                    {
                        WwiseExporterLogger.Log($"  * Sound {sound.SourceId} (SelfId: {childId}) NOT found in project! Creating it...");
                        var newSound = CreateSoundElement(sound.SourceId, sound.SelfId, projectFolder);
                        childrenList.Add(newSound);
                        continue;
                    }
                }

                var nestedContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == childId);
                if (nestedContainer != null)
                {
                    var nestedSoundIds = nestedContainer.ChildrenIds.Count > 0
                        ? nestedContainer.ChildrenIds
                        : nestedContainer.SoundIds;

                    foreach (var nestedSoundId in nestedSoundIds)
                    {
                        var nestedSound = sections.Sounds.FirstOrDefault(s => s.SelfId == nestedSoundId);
                        if (nestedSound != null && soundMap.TryGetValue(nestedSound.SourceId, out var nestedSoundElement))
                        {
                            WwiseExporterLogger.Log($"  + Adding Nested Sound {nestedSound.SourceId} (SelfId: {nestedSoundId})");
                            var repairedSound = RepairSoundElement(new XElement(nestedSoundElement), nestedSound.SourceId, projectFolder);
                            childrenList.Add(repairedSound);
                        }
                    }
                }
            }

            var containerElement = new XElement("RandomSequenceContainer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                new XElement("PropertyList",
                    new XElement("Property",
                        new XAttribute("Name", "RandomOrSequence"),
                        new XAttribute("Type", "int16"),
                        new XAttribute("Value", "0"))),
                new XElement("ReferenceList",
                    CreateOutputBusReference()),
                childrenList);

            return containerElement;
        }

        private XElement CreateRandomSequenceContainerForSingleSound(
            string name,
            string guid,
            string shortId,
            Sound sound,
            Dictionary<uint, XElement> soundMap,
            BNKSections sections,
            string projectFolder)
        {
            var childrenList = new XElement("ChildrenList");

            if (soundMap.TryGetValue(sound.SourceId, out var soundElement))
            {
                var repairedSound = RepairSoundElement(new XElement(soundElement), sound.SourceId, projectFolder);
                childrenList.Add(repairedSound);
            }

            var containerElement = new XElement("RandomSequenceContainer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                new XElement("PropertyList",
                    new XElement("Property",
                        new XAttribute("Name", "RandomOrSequence"),
                        new XAttribute("Type", "int16"),
                        new XAttribute("Value", "0"))),
                new XElement("ReferenceList",
                    CreateOutputBusReference()),
                childrenList);

            return containerElement;
        }

        /// <summary>
        /// Create a Sound element programmatically
        /// </summary>
        private XElement CreateSoundElement(uint wemId, uint soundId, string projectFolder)
        {
            string soundGuid = GenerateGuid();
            string soundShortId = GenerateShortId();
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
                        new XElement("Language", "SFX"),
                        new XElement("AudioFile", $"{wemId}.wav"))),
                new XElement("ActiveSourceList",
                    new XElement("ActiveSource",
                        new XAttribute("Name", wemName),
                        new XAttribute("ID", audioSourceGuid),
                        new XAttribute("Platform", "Linked"))));

            return RepairSoundElement(soundElement, wemId, projectFolder);
        }

        private XElement CreateSwitchContainer(string name, string guid, string shortId, SwitchContainer sc, BNKSections sections, Dictionary<uint, XElement> soundMap, string projectFolder)
        {
            var childrenList = new XElement("ChildrenList");
            foreach (var childId in sc.Children)
            {
                var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == childId);
                if (sound != null)
                {
                    if (soundMap.TryGetValue(sound.SourceId, out var soundEl))
                        childrenList.Add(RepairSoundElement(new XElement(soundEl), sound.SourceId, projectFolder));
                    else
                        childrenList.Add(CreateSoundElement(sound.SourceId, sound.SelfId, projectFolder));
                }
            }

            return new XElement("SwitchContainer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                new XElement("ReferenceList", CreateOutputBusReference()),
                childrenList);
        }

        private XElement CreateActorMixer(string name, string guid, string shortId, ActorMixer am, BNKSections sections, Dictionary<uint, XElement> soundMap, string projectFolder)
        {
            var childrenList = new XElement("ChildrenList");
            foreach (var childId in am.Children)
            {
                var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == childId);
                if (sound != null)
                {
                    if (soundMap.TryGetValue(sound.SourceId, out var soundEl))
                        childrenList.Add(RepairSoundElement(new XElement(soundEl), sound.SourceId, projectFolder));
                    else
                        childrenList.Add(CreateSoundElement(sound.SourceId, sound.SelfId, projectFolder));
                }
            }

            return new XElement("ActorMixer",
                new XAttribute("Name", name),
                new XAttribute("ID", guid),
                new XAttribute("ShortID", shortId),
                new XElement("ReferenceList", CreateOutputBusReference()),
                childrenList);
        }

        private XElement RepairSoundElement(XElement soundEl, uint wemId, string projectPath)
        {
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

            return reference;
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
                string eventShortId = GenerateShortId();

                var actionsList = new XElement("ChildrenList");

                foreach (var eventActionId in bnkEvent.EventIds)
                {
                    var eventAction = sections.EventActions.FirstOrDefault(ea => ea.SelfId == eventActionId);
                    if (eventAction == null) continue;

                    if (_containerIdToGuid.TryGetValue(eventAction.SoundObjectId, out var containerGuid))
                    {
                        var actionElement = CreatePlayAction(eventName, containerGuid);
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

        private XElement CreatePlayAction(string targetName, string targetGuid)
        {
            string actionGuid = GenerateGuid();
            string actionShortId = GenerateShortId();

            return new XElement("Action",
                new XAttribute("Name", ""),
                new XAttribute("ID", actionGuid),
                new XAttribute("ShortID", actionShortId),
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
    }
}
