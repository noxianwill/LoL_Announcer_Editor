using System;
using System.IO;

namespace LoL_Announcer_Editor
{
    public static class BnkBinaryHelpers
    {
        public static void SkipInitialFxParams(BinaryReader reader, uint bnkVersion)
        {
            reader.BaseStream.Seek(1, SeekOrigin.Current);
            byte numFx = reader.ReadByte();
            if (numFx != 0) reader.BaseStream.Seek(1, SeekOrigin.Current);
            reader.BaseStream.Seek(numFx * (bnkVersion <= 0x91 ? 7 : 6), SeekOrigin.Current);
        }

        public static void SkipInitialParams(BinaryReader reader)
        {
            byte propCount = reader.ReadByte();
            reader.BaseStream.Seek(5 * propCount, SeekOrigin.Current);
            propCount = reader.ReadByte();
            reader.BaseStream.Seek(9 * propCount, SeekOrigin.Current);
        }

        public static void SkipPositioningParams(BinaryReader reader, uint bnkVersion)
        {
            byte positioningBits = reader.ReadByte();
            bool hasPositioning = (positioningBits & 1) != 0;
            bool has3d = false, hasAutomation = false;
            if (hasPositioning)
            {
                if (bnkVersion <= 0x59)
                {
                    bool has2d = reader.ReadByte() != 0;
                    has3d = reader.ReadByte() != 0;
                    if (has2d) reader.ReadByte();
                }
                else
                {
                    has3d = (positioningBits & 0x2) != 0;
                }
            }
            if (hasPositioning && has3d)
            {
                if (bnkVersion <= 0x59)
                {
                    hasAutomation = (reader.ReadByte() & 3) != 1;
                    reader.BaseStream.Seek(8, SeekOrigin.Current);
                }
                else
                {
                    hasAutomation = ((positioningBits >> 5) & 3) != 0;
                    reader.ReadByte();
                }
            }
            if (hasAutomation)
            {
                reader.BaseStream.Seek(bnkVersion <= 0x59 ? 9 : 5, SeekOrigin.Current);
                uint numVertices = reader.ReadUInt32();
                reader.BaseStream.Seek(16 * numVertices, SeekOrigin.Current);
                uint numPlaylistItems = reader.ReadUInt32();
                reader.BaseStream.Seek((bnkVersion <= 0x59 ? 16 : 20) * numPlaylistItems, SeekOrigin.Current);
            }
            else if (bnkVersion <= 0x59)
            {
                reader.ReadByte();
            }
        }

        public static void SkipAuxParams(BinaryReader reader, uint bnkVersion)
        {
            bool hasAux = ((reader.ReadByte() >> 3) & 1) != 0;
            if (hasAux) reader.BaseStream.Seek(4 * sizeof(uint), SeekOrigin.Current);
            if (bnkVersion > 0x87) reader.BaseStream.Seek(4, SeekOrigin.Current);
        }

        public static void SkipStateChunk(BinaryReader reader)
        {
            byte stateProps = reader.ReadByte();
            reader.BaseStream.Seek(3 * stateProps, SeekOrigin.Current);
            byte stateGroups = reader.ReadByte();
            for (int i = 0; i < stateGroups; i++)
            {
                reader.BaseStream.Seek(5, SeekOrigin.Current);
                byte states = reader.ReadByte();
                reader.BaseStream.Seek(8 * states, SeekOrigin.Current);
            }
        }

        public static void SkipRtpc(BinaryReader reader, uint bnkVersion)
        {
            ushort numRtpc = reader.ReadUInt16();
            for (int i = 0; i < numRtpc; i++)
            {
                reader.BaseStream.Seek(bnkVersion <= 0x59 ? 13 : 12, SeekOrigin.Current);
                ushort pointCount = reader.ReadUInt16();
                reader.BaseStream.Seek(12 * pointCount, SeekOrigin.Current);
            }
        }

        public static void SkipClipAutomation(BinaryReader reader)
        {
            uint numClipAutomation = reader.ReadUInt32();
            for (uint i = 0; i < numClipAutomation; i++)
            {
                reader.BaseStream.Seek(8, SeekOrigin.Current);
                uint pointCount = reader.ReadUInt32();
                reader.BaseStream.Seek(12 * pointCount, SeekOrigin.Current);
            }
        }

        public static uint SkipBaseParams(BinaryReader reader, uint bnkVersion, out uint outBusId)
        {
            SkipInitialFxParams(reader, bnkVersion);
            if (bnkVersion > 0x88)
            {
                reader.BaseStream.Seek(1, SeekOrigin.Current);
                byte numFx = reader.ReadByte();
                reader.BaseStream.Seek(6 * numFx, SeekOrigin.Current);
            }
            if (bnkVersion > 0x59 && bnkVersion <= 0x91) reader.BaseStream.Seek(1, SeekOrigin.Current);
            outBusId = reader.ReadUInt32();
            uint parentId = reader.ReadUInt32();
            reader.BaseStream.Seek(bnkVersion <= 0x59 ? 2 : 1, SeekOrigin.Current);
            SkipInitialParams(reader);
            SkipPositioningParams(reader, bnkVersion);
            SkipAuxParams(reader, bnkVersion);
            reader.BaseStream.Seek(6, SeekOrigin.Current);
            byte stateProps = reader.ReadByte();
            reader.BaseStream.Seek(3 * stateProps, SeekOrigin.Current);
            byte stateGroups = reader.ReadByte();
            for (int i = 0; i < stateGroups; i++)
            {
                reader.BaseStream.Seek(5, SeekOrigin.Current);
                byte states = reader.ReadByte();
                reader.BaseStream.Seek(8 * states, SeekOrigin.Current);
            }
            SkipRtpc(reader, bnkVersion);
            return parentId;
        }
    }
} 