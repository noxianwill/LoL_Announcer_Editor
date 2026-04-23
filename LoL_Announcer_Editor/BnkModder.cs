using System;
using System.Collections.Generic;
using System.Linq;

namespace LoL_Announcer_Editor
{
    public class BnkModder
    {
        private BnkFullFidelity _bnk;
        private BNKSections _sections;

        public BnkModder(BnkFullFidelity bnk, BNKSections sections)
        {
            _bnk = bnk;
            _sections = sections;
        }

        public void AddWemToEvent(string eventName, uint wemId, uint length, Dictionary<string, uint> lookup, uint? preferredContainerId = null)
        {
            if (!lookup.TryGetValue(eventName, out uint eventId))
            {
                WemAdditionLogger.Log($"ERROR: Event name {eventName} not found in lookup");
                return;
            }

            var evt = _sections.Events.FirstOrDefault(e => e.SelfId == eventId);
            if (evt == null) return;

            foreach (var actionId in evt.EventIds)
            {
                var action = _sections.EventActions.FirstOrDefault(a => a.SelfId == actionId);
                if (action == null) continue;

                uint targetContainerId = preferredContainerId ?? action.SoundObjectId;
                var container = _sections.RandomContainers.FirstOrDefault(c => c.SelfId == targetContainerId);

                if (container != null)
                {
                    uint newSoundId = GenerateNewObjectId();
                    var template    = _sections.Sounds.FirstOrDefault() ?? new Sound();
                    var newSound    = CreateSoundFromTemplate(template, newSoundId, wemId, length, container.SelfId);
                    _sections.Sounds.Add(newSound);
                    _bnk.AllObjectsInOrder.Add(new BnkObjectRecord {
                        Type = 2, ObjectId = newSoundId, ParsedObject = newSound, IsChanged = true
                    });

                    if (!container.SoundIds.Contains(newSoundId))
                    {
                        container.SoundIds.Add(newSoundId);
                        container.Playlist.Add(new PlaylistItem { Id = newSoundId, Weight = 50 });
                        var containerRecord = _bnk.AllObjectsInOrder.FirstOrDefault(o => o.ObjectId == container.SelfId);
                        if (containerRecord != null) containerRecord.IsChanged = true;
                    }
                }
            }
        }

        private Sound CreateSoundFromTemplate(Sound template, uint newSoundId, uint wemId, uint length, uint parentId)
        {
            var nbp = template.NodeBaseParams ?? new NodeBaseParams();
            return new Sound
            {
                SelfId            = newSoundId,
                PluginId          = template.PluginId,
                StreamType        = 1,                 // 1 = Streamed
                SourceId          = wemId,
                InMemoryMediaSize = length,
                SourceBits        = template.SourceBits,
                NodeBaseParams = new NodeBaseParams {
                    OutputBusId              = nbp.OutputBusId,
                    DirectParentId           = parentId,
                    OverrideParentFX         = nbp.OverrideParentFX,
                    NumFx                    = nbp.NumFx,
                    OverrideParentMetadata   = nbp.OverrideParentMetadata,
                    OverrideAttachmentParams = nbp.OverrideAttachmentParams,
                    PositioningBits          = nbp.PositioningBits,
                    AuxBits                  = nbp.AuxBits
                }
            };
        }

        private uint GenerateNewObjectId() => 200000000 + (uint)(new Random().Next(1000000));
    }
}
