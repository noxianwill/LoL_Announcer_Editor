using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoL_Announcer_Editor
{
    public static class BnkExporter
    {
        public static void ExportToJson(BNKSections sections, Dictionary<uint, string> eventNameLookup, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var root = new {
                Events = BuildEventsHierarchy(sections, eventNameLookup),
                UnreferencedMedia = FindUnreferencedMedia(sections)
            };

            string json = JsonSerializer.Serialize(root, options);
            File.WriteAllText(filePath, json);
        }

        private static List<object> BuildEventsHierarchy(BNKSections sections, Dictionary<uint, string> eventNameLookup)
        {
            var eventsList = new List<object>();

            foreach (var bnkEvent in sections.Events)
            {
                string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var name)
                    ? name
                    : $"Event_{bnkEvent.SelfId}";

                var actionsList = new List<object>();

                foreach (var eventActionId in bnkEvent.EventIds)
                {
                    var eventAction = sections.EventActions.FirstOrDefault(ea => ea.SelfId == eventActionId);
                    if (eventAction == null) continue;

                    var targetNode = BuildTargetNode(eventAction.SoundObjectId, sections);
                    
                    actionsList.Add(new {
                        Id = eventAction.SelfId,
                        Type = "PlayAction",
                        Target = targetNode
                    });
                }

                eventsList.Add(new {
                    Id = bnkEvent.SelfId,
                    Name = eventName,
                    Type = "Event",
                    Actions = actionsList
                });
            }

            return eventsList;
        }

        private static object BuildTargetNode(uint targetId, BNKSections sections)
        {
            // Is it a Sound?
            var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == targetId);
            if (sound != null)
            {
                return new {
                    Id = sound.SelfId,
                    Type = "Sound",
                    WemId = sound.SourceId
                };
            }

            // Is it a RandomContainer?
            var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == targetId);
            if (randomContainer != null)
            {
                var children = new List<object>();
                var childIds = randomContainer.ChildrenIds.Count > 0 ? randomContainer.ChildrenIds : randomContainer.SoundIds;
                
                foreach (var childId in childIds)
                {
                    children.Add(BuildTargetNode(childId, sections));
                }

                return new {
                    Id = randomContainer.SelfId,
                    Type = "RandomSequenceContainer",
                    Children = children
                };
            }

            // Is it a SwitchContainer?
            var switchContainer = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == targetId);
            if (switchContainer != null)
            {
                var children = new List<object>();
                foreach (var childId in switchContainer.Children)
                {
                    children.Add(BuildTargetNode(childId, sections));
                }

                return new {
                    Id = switchContainer.SelfId,
                    Type = "SwitchContainer",
                    Children = children
                };
            }

            // Is it an ActorMixer?
            var actorMixer = sections.ActorMixers.FirstOrDefault(a => a.SelfId == targetId);
            if (actorMixer != null)
            {
                var children = new List<object>();
                foreach (var childId in actorMixer.Children)
                {
                    children.Add(BuildTargetNode(childId, sections));
                }

                return new {
                    Id = actorMixer.SelfId,
                    Type = "ActorMixer",
                    Children = children
                };
            }

            // Fallback for unknown/unsupported target type
            return new {
                Id = targetId,
                Type = "Unknown"
            };
        }

        private static List<uint> FindUnreferencedMedia(BNKSections sections)
        {
            var referencedIds = new HashSet<uint>();
            
            // Traverse all events to collect referenced sound SourceIds
            foreach (var evt in sections.Events)
            {
                foreach (var actionId in evt.EventIds)
                {
                    var action = sections.EventActions.FirstOrDefault(a => a.SelfId == actionId);
                    if (action != null)
                    {
                        CollectReferencedSounds(action.SoundObjectId, sections, referencedIds);
                    }
                }
            }

            // Any Sound whose SourceId (WEM ID) wasn't visited is unreferenced
            var unreferenced = new List<uint>();
            foreach (var sound in sections.Sounds)
            {
                if (sound.SourceId != 0 && !referencedIds.Contains(sound.SourceId))
                {
                    if (!unreferenced.Contains(sound.SourceId))
                        unreferenced.Add(sound.SourceId);
                }
            }

            return unreferenced;
        }

        private static void CollectReferencedSounds(uint objectId, BNKSections sections, HashSet<uint> referencedIds)
        {
            var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == objectId);
            if (sound != null)
            {
                referencedIds.Add(sound.SourceId);
                return;
            }

            var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == objectId);
            if (randomContainer != null)
            {
                var childIds = randomContainer.ChildrenIds.Count > 0 ? randomContainer.ChildrenIds : randomContainer.SoundIds;
                foreach (var childId in childIds) CollectReferencedSounds(childId, sections, referencedIds);
                return;
            }

            var switchContainer = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == objectId);
            if (switchContainer != null)
            {
                foreach (var childId in switchContainer.Children) CollectReferencedSounds(childId, sections, referencedIds);
                return;
            }

            var actorMixer = sections.ActorMixers.FirstOrDefault(a => a.SelfId == objectId);
            if (actorMixer != null)
            {
                foreach (var childId in actorMixer.Children) CollectReferencedSounds(childId, sections, referencedIds);
                return;
            }
        }
    }
}
