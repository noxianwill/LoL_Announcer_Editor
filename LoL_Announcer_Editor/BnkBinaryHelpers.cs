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

        // ──────────────────────────── WRITE ───────────────────────────────────

        public static void WriteNodeBaseParams(BinaryWriter writer, NodeBaseParams p, uint bnkVersion)
        {
            // FX override
            writer.Write(p.OverrideParentFX);
            writer.Write(p.NumFx);
            if (p.NumFx > 0)
            {
                writer.Write(p.FXBypassBits);
                foreach (var fx in p.FxInfos)
                {
                    writer.Write(fx.Index);
                    writer.Write(fx.Id);
                    writer.Write(fx.IsShareSet);
                    writer.Write(fx.IsRendered);
                }
            }

            // Metadata
            writer.Write(p.OverrideParentMetadata);
            writer.Write((byte)p.Metadata.Count);
            foreach (var m in p.Metadata) 
            { 
                writer.Write(m.Id); 
                writer.Write((uint)m.Payload.Length);
                writer.Write(m.Payload); 
            }

            // Bus / parent
            writer.Write(p.OverrideAttachmentParams);
            writer.Write(p.OutputBusId);
            writer.Write(p.DirectParentId);
            writer.Write(p.OverrideParentPriority);
            writer.Write(p.OffsetPriority);

            // Props (float)
            writer.Write((byte)p.Props.Count);
            if (p.Props.Count > 0)
            {
                foreach (var prop in p.Props) writer.Write(prop.ID);
                foreach (var prop in p.Props) writer.Write(prop.Value);
            }

            // Props (int)
            writer.Write((byte)p.PropsInt.Count);
            if (p.PropsInt.Count > 0)
            {
                foreach (var prop in p.PropsInt) writer.Write(prop.ID);
                foreach (var prop in p.PropsInt) writer.Write(prop.Value);
            }

            // Positioning
            writer.Write(p.PositioningBits);
            if ((p.PositioningBits & 0x01) != 0)
            {
                bool has3d = (p.PositioningBits & 0x02) != 0;
                if (has3d) writer.Write(p.uBits3d);
                else writer.Write(p.bIsPannerEnabled);
            }

            // Aux
            writer.Write(p.AuxBits);
            if ((p.AuxBits & 0x08) != 0)
            {
                writer.Write(p.AuxBus0);
                writer.Write(p.AuxBus1);
                writer.Write(p.AuxBus2);
                writer.Write(p.AuxBus3);
            }

            // Virtual voice / HDR
            if (bnkVersion >= 135)
                writer.Write(p.ReflectionsAuxBus);

            // Virtual voice / HDR / Adv Settings
            if (bnkVersion >= 140)
            {
                writer.Write(p.AdvBits);
                writer.Write(p.VirtualQueueBehavior);
                writer.Write(p.MaxNumInstance);
                writer.Write(p.BelowThresholdBehavior);
                writer.Write(p.HdrBits);
            }
            else if (bnkVersion > 0x87) 
            {
                writer.Write(p.AdvBits);
            }

            // State props
            writer.Write((byte)p.StateProps.Count);
            foreach (var sp in p.StateProps) writer.Write(sp);

            // State groups
            writer.Write((byte)p.StateGroups.Count);
            foreach (var group in p.StateGroups)
            {
                writer.Write(group.Id);
                writer.Write(group.SyncType);
                writer.Write((byte)group.States.Count);
                foreach (var s in group.States) { writer.Write(s.StateId); writer.Write(s.InstanceId); }
            }

            // RTPCs
            writer.Write((ushort)p.Rtpcs.Count);
            foreach (var rtpc in p.Rtpcs)
            {
                writer.Write(rtpc.ControlId);
                writer.Write(rtpc.ParamId);
                writer.Write(rtpc.CurveId);
                writer.Write(rtpc.Scaling);
                writer.Write((ushort)rtpc.Points.Count);
                foreach (var pt in rtpc.Points) { writer.Write(pt.X); writer.Write(pt.Y); writer.Write(pt.Shape); }
            }
        }
    }
}