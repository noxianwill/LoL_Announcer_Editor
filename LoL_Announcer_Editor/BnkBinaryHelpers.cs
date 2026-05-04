using System;
using System.IO;
using System.Collections.Generic;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Low-level binary helpers for reading and writing Wwise NodeBaseParams.
    /// All field names follow the Wwise SDK / wwiser conventions.
    /// RTPC layout per wwiser: ControlId(4) ParamId(1) CurveId(4) Scaling(1) PointCount(2) Points[]
    /// </summary>
    public static class BnkBinaryHelpers
    {
        // ──────────────────────────── READ ────────────────────────────────────

        public static NodeBaseParams ReadNodeBaseParams(BinaryReader reader, uint bnkVersion)
        {
            var p = new NodeBaseParams();

            // FX override
            p.OverrideParentFX = reader.ReadByte();
            p.NumFx = reader.ReadByte();
            if (p.NumFx > 0)
            {
                p.FXBypassBits = reader.ReadByte();
                for (int i = 0; i < p.NumFx; i++)
                {
                    p.FxInfos.Add(new FxInfo {
                        Index      = reader.ReadByte(),
                        Id         = reader.ReadUInt32(),
                        IsShareSet = reader.ReadByte(),
                        IsRendered = reader.ReadByte()
                    });
                }
            }

            // Metadata override
            p.OverrideParentMetadata = reader.ReadByte();
            p.NumMetadata            = reader.ReadByte();
            for (int i = 0; i < p.NumMetadata; i++)
            {
                uint metaId = reader.ReadUInt32();
                uint metaSize = reader.ReadUInt32();
                p.Metadata.Add(new MetadataEntry { 
                    Id = metaId, 
                    Payload = reader.ReadBytes((int)metaSize) 
                }); 
            }

            // Bus / parent
            p.OverrideAttachmentParams = reader.ReadByte();
            p.OutputBusId              = reader.ReadUInt32();
            p.DirectParentId           = reader.ReadUInt32();
            
            // Priority/Midi bits (1 byte in 145+)
            p.OverrideParentPriority = reader.ReadByte(); 
            // Note: Priority value itself is now usually in Props (ID 7)

            // Props (float)
            byte propCount = reader.ReadByte();
            if (propCount > 0)
            {
                byte[] ids = reader.ReadBytes(propCount);
                for (int i = 0; i < propCount; i++)
                    p.Props.Add(new PropValue { ID = ids[i], Value = reader.ReadSingle() });
            }

            // Props (int)
            propCount = reader.ReadByte();
            if (propCount > 0)
            {
                byte[] ids = reader.ReadBytes(propCount);
                for (int i = 0; i < propCount; i++)
                    p.PropsInt.Add(new PropValueInt { ID = ids[i], Value = reader.ReadInt32() });
            }

            // Positioning
            p.PositioningBits = reader.ReadByte();
            if ((p.PositioningBits & 0x01) != 0) // Override Parent
            {
                // In BNK 145: bit 0x02 indicates 3D (Listener Relative Routing)
                if ((p.PositioningBits & 0x02) != 0) 
                {
                    p.uBits3d = reader.ReadByte(); 
                }
                else 
                {
                    p.bIsPannerEnabled = reader.ReadByte(); 
                }
            }

            // Aux busses
            p.AuxBits = reader.ReadByte();
            if ((p.AuxBits & 0x08) != 0)
            {
                p.AuxBus0 = reader.ReadUInt32();
                p.AuxBus1 = reader.ReadUInt32();
                p.AuxBus2 = reader.ReadUInt32();
                p.AuxBus3 = reader.ReadUInt32();
            }
            else if ((p.AuxBits & 0x04) != 0) 
            {
                p.AuxBus0 = reader.ReadUInt32();
            }
            
            // reflectionsAuxBus (4 bytes in 135+)
            if (bnkVersion >= 135)
                p.ReflectionsAuxBus = reader.ReadUInt32();

            // Adv settings (6-byte block in BNK 140+)
            if (bnkVersion >= 140) 
            {
                p.AdvBits = reader.ReadByte();
                p.VirtualQueueBehavior = reader.ReadByte();
                p.MaxNumInstance = reader.ReadUInt16();
                p.BelowThresholdBehavior = reader.ReadByte();
                p.HdrBits = reader.ReadByte();
            }
            else if (bnkVersion > 0x87) 
            {
                p.AdvBits = reader.ReadByte();
            }

            // State properties
            byte statePropsCount = reader.ReadByte();
            for (int i = 0; i < statePropsCount; i++)
                p.StateProps.Add(reader.ReadBytes(3));

            // State groups
            byte stateGroupsCount = reader.ReadByte();
            for (int i = 0; i < stateGroupsCount; i++)
            {
                var group = new StateGroup {
                    Id       = reader.ReadUInt32(),
                    SyncType = reader.ReadByte()
                };
                byte stateCount = reader.ReadByte();
                for (int j = 0; j < stateCount; j++)
                    group.States.Add(new StateInstance {
                        StateId    = reader.ReadUInt32(),
                        InstanceId = reader.ReadUInt32()
                    });
                p.StateGroups.Add(group);
            }

            // RTPCs — layout: ControlId(4) ParamId(1) CurveId(4) Scaling(1) PointCount(2) Points[]
            ushort rtpcCount = reader.ReadUInt16();
            for (int i = 0; i < rtpcCount; i++)
            {
                var rtpc = new RtpcEntry {
                    ControlId = reader.ReadUInt32(),
                    ParamId   = reader.ReadByte(),
                    CurveId   = reader.ReadUInt32(),
                    Scaling   = reader.ReadByte()
                };
                ushort pointCount = reader.ReadUInt16();
                for (int j = 0; j < pointCount; j++)
                    rtpc.Points.Add(new RtpcPoint {
                        X     = reader.ReadSingle(),
                        Y     = reader.ReadSingle(),
                        Shape = reader.ReadUInt32()
                    });
                p.Rtpcs.Add(rtpc);
            }

            return p;
        }
    }
}
