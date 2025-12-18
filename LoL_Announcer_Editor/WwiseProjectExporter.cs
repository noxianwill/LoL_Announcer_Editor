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
            ObservableCollection<TreeNode> rootNodes,
            BNKSections sections,
            Dictionary<uint, string> eventNameLookup,
            string sourceWwiseProject,
            string destinationFolder)
        {
            if (rootNodes == null || rootNodes.Count == 0)
                throw new ArgumentException("No parsed data to export");

            if (!Directory.Exists(sourceWwiseProject))
                throw new DirectoryNotFoundException($"Source Wwise project not found: {sourceWwiseProject}");

            var wprojFiles = Directory.GetFiles(sourceWwiseProject, "*.wproj");
            if (wprojFiles.Length == 0)
                throw new InvalidOperationException("No .wproj file found in source folder.");

            string projectName = Path.GetFileNameWithoutExtension(wprojFiles[0]);
            string actorMixerPath = Path.Combine(sourceWwiseProject, "Actor-Mixer Hierarchy");
            string eventsPath = Path.Combine(sourceWwiseProject, "Events");

            if (!Directory.Exists(actorMixerPath))
                throw new DirectoryNotFoundException("Actor-Mixer Hierarchy folder not found");
            if (!Directory.Exists(eventsPath))
                throw new DirectoryNotFoundException("Events folder not found");

            // Copy project to destination
            string destProjectPath = Path.Combine(destinationFolder, projectName + "_LoLTemplate");
            CopyDirectory(sourceWwiseProject, destProjectPath);

            // Load work units
            string destActorMixerWwu = Path.Combine(destProjectPath, "Actor-Mixer Hierarchy", "Default Work Unit.wwu");
            if (!File.Exists(destActorMixerWwu))
                throw new FileNotFoundException("Default Work Unit.wwu not found in Actor-Mixer Hierarchy");

            var actorMixerDoc = XDocument.Load(destActorMixerWwu);
            ExtractReferences(actorMixerDoc);

            // Build hierarchy
            var containers = BuildActorMixerHierarchy(sections, eventNameLookup);
            ModifyActorMixerWorkUnit(actorMixerDoc, containers);
            actorMixerDoc.Save(destActorMixerWwu);

            // Build events
            string destEventsWwu = Path.Combine(destProjectPath, "Events", "Default Work Unit.wwu");
            var eventsDoc = XDocument.Load(destEventsWwu);
            var eventElements = BuildEventsHierarchy(sections, eventNameLookup);
            ModifyEventsWorkUnit(eventsDoc, eventElements);
            eventsDoc.Save(destEventsWwu);
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
            var workUnit = doc.Descendants("WorkUnit")
                .FirstOrDefault(wu => wu.Attribute("Name")?.Value == "Default Work Unit");

            if (workUnit != null)
                _actorMixerWorkUnitId = workUnit.Attribute("ID")?.Value ?? "";

            var outputBusRef = doc.Descendants("Reference")
                .FirstOrDefault(r => r.Attribute("Name")?.Value == "OutputBus");

            if (outputBusRef != null)
            {
                var objRef = outputBusRef.Element("ObjectRef");
                if (objRef != null)
                {
                    _masterBusId = objRef.Attribute("ID")?.Value ?? "{1514A4D8-1DA6-412A-A17E-75CA0C2149F3}";
                    _masterBusWorkUnitId = objRef.Attribute("WorkUnitID")?.Value ?? "";
                }
            }

            var conversionRef = doc.Descendants("Reference")
                .FirstOrDefault(r => r.Attribute("Name")?.Value == "Conversion");

            if (conversionRef != null)
            {
                var objRef = conversionRef.Element("ObjectRef");
                if (objRef != null)
                {
                    _conversionSettingsId = objRef.Attribute("ID")?.Value ?? "";
                    _conversionSettingsWorkUnitId = objRef.Attribute("WorkUnitID")?.Value ?? "";
                }
            }
        }

        private List<XElement> BuildActorMixerHierarchy(
            BNKSections sections,
            Dictionary<uint, string> eventNameLookup)
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
                            sections);

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
                                sound);

                            containers.Add(containerElement);
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
            BNKSections sections)
        {
            var childrenList = new XElement("ChildrenList");

            var childIds = container.ChildrenIds.Count > 0
                ? container.ChildrenIds
                : container.SoundIds;

            foreach (var childId in childIds)
            {
                var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == childId);
                if (sound != null)
                {
                    uint wemId = sound.StreamFileID != 0 ? sound.StreamFileID : sound.FileId;
                    var soundElement = CreateSoundElement(wemId, sound.SelfId);
                    childrenList.Add(soundElement);
                    continue;
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
                        if (nestedSound == null) continue;

                        uint wemId = nestedSound.StreamFileID != 0 ? nestedSound.StreamFileID : nestedSound.FileId;
                        var soundElement = CreateSoundElement(wemId, nestedSound.SelfId);
                        childrenList.Add(soundElement);
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
            Sound sound)
        {
            var childrenList = new XElement("ChildrenList");

            uint wemId = sound.StreamFileID != 0 ? sound.StreamFileID : sound.FileId;
            var soundElement = CreateSoundElement(wemId, sound.SelfId);
            childrenList.Add(soundElement);

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
        private XElement CreateSoundElement(uint wemId, uint soundId)
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
                new XElement("ChildrenList"),
                new XElement("ActiveSourceList",
                    new XElement("ActiveSource",
                        new XAttribute("Name", wemName),
                        new XAttribute("ID", audioSourceGuid),
                        new XAttribute("Platform", "Linked"))),
                new XElement("SourceList",
                    new XElement("Source",
                        new XAttribute("ID", audioSourceGuid),
                        new XAttribute("Platform", "Linked"),
                        new XElement("AudioFileSource",
                            new XAttribute("Name", wemName),
                            new XAttribute("ID", audioSourceGuid)))));

            return soundElement;
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
