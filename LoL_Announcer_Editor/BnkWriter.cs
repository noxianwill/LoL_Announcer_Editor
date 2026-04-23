using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Writes a BnkFullFidelity object graph back to a binary .bnk file.
    /// Every known type is serialized from its structured ParsedObject.
    /// For genuinely unknown type IDs the original RawData bytes are emitted unchanged.
    /// </summary>
    public static class BnkWriter
    {
        public static bool WriteBnkFile_CopyRaw(BnkFullFidelity fullFidelity, string outputPath)
            => WriteBnkFile_FullFidelity(fullFidelity, outputPath);

        public static bool WriteBnkFile_FullFidelity(BnkFullFidelity fullFidelity, string outputPath)
        {
            try
            {
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // BKHD header (24 bytes)
                    writer.Write(Encoding.ASCII.GetBytes("BKHD"));
                    writer.Write((uint)20);
                    writer.Write(fullFidelity.BnkVersion);
                    writer.Write(new byte[16]);

                    // HIRC section
                    var objects = fullFidelity.AllObjectsInOrder;
                    if (objects.Count > 0)
                    {
                        writer.Write(Encoding.ASCII.GetBytes("HIRC"));

                        // Pre-compute section size
                        uint hircSize = 4; // object count field
                        foreach (var obj in objects)
                            hircSize += (uint)SerializeObject(obj, fullFidelity.BnkVersion).Length;

                        writer.Write(hircSize);
                        writer.Write((uint)objects.Count);

                        foreach (var obj in objects)
                            writer.Write(SerializeObject(obj, fullFidelity.BnkVersion));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogException("WriteBnkFile_FullFidelity", ex);
                return false;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Serializer dispatcher
        // ──────────────────────────────────────────────────────────────────────
        private static byte[] SerializeObject(BnkObjectRecord obj, uint bnkVersion)
        {
            // Unknown type — emit original bytes unchanged
            if (obj.ParsedObject == null)
                return obj.RawData ?? Array.Empty<byte>();

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                switch (obj.ParsedObject)
                {
                    case Sound sound:
                        WriteWithHeader(w, 2, bnkVersion, inner =>
                        {
                            inner.Write(sound.SelfId);
                            inner.Write(sound.PluginId);
                            inner.Write(sound.StreamType);
                            inner.Write(sound.SourceId);
                            inner.Write(sound.InMemoryMediaSize);
                            inner.Write(sound.SourceBits);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, sound.NodeBaseParams, bnkVersion);
                        });
                        break;

                    case EventAction ea:
                        WriteWithHeader(w, 3, bnkVersion, inner =>
                        {
                            inner.Write(ea.SelfId);
                            inner.Write(ea.Scope);
                            inner.Write(ea.ActionType);
                            inner.Write(ea.TargetId);
                            inner.Write((byte)ea.ParameterTypes.Count);
                            foreach (var t in ea.ParameterTypes) inner.Write(t);
                            foreach (var v in ea.ParameterValues) inner.Write(v);
                        });
                        break;

                    case Event evt:
                        WriteWithHeader(w, 4, bnkVersion, inner =>
                        {
                            inner.Write(evt.SelfId);
                            inner.Write(evt.EventAmount);
                            foreach (var id in evt.EventIds) inner.Write(id);
                        });
                        break;

                    case RandomContainer rc:
                        WriteWithHeader(w, 5, bnkVersion, inner =>
                        {
                            inner.Write(rc.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, rc.NodeBaseParams, bnkVersion);
                            inner.Write(rc.LoopCount);
                            inner.Write(rc.LoopModMin);
                            inner.Write(rc.LoopModMax);
                            inner.Write(rc.TransitionTime);
                            inner.Write(rc.TransitionTimeModMin);
                            inner.Write(rc.TransitionTimeModMax);
                            inner.Write(rc.TransitionMode);
                            inner.Write(rc.AvoidRepeatCount);
                            inner.Write(rc.RandomMode);
                            inner.Write(rc.SoundIdAmount);
                            foreach (var id in rc.SoundIds) inner.Write(id);
                            inner.Write((ushort)rc.Playlist.Count);
                            foreach (var e in rc.Playlist)
                            {
                                inner.Write(e.Id);
                                inner.Write(e.Weight);
                            }
                        });
                        break;

                    case SwitchContainer sc:
                        WriteWithHeader(w, 6, bnkVersion, inner =>
                        {
                            inner.Write(sc.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, sc.NodeBaseParams, bnkVersion);
                            inner.Write((byte)0); // loop flags
                            inner.Write(sc.GroupType);
                            inner.Write(sc.GroupId);
                            inner.Write(sc.DefaultSwitchId);
                            inner.Write((byte)(sc.IsContinuousValidation ? 1 : 0));
                            inner.Write((uint)sc.Children.Count);
                            foreach (var id in sc.Children) inner.Write(id);
                            inner.Write((uint)sc.SwitchGroups.Count);
                            foreach (var g in sc.SwitchGroups)
                            {
                                inner.Write(g.SwitchId);
                                inner.Write((uint)g.Items.Count);
                                foreach (var item in g.Items) inner.Write(item);
                            }
                        });
                        break;

                    case ActorMixer am:
                        WriteWithHeader(w, 7, bnkVersion, inner =>
                        {
                            inner.Write(am.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, am.NodeBaseParams, bnkVersion);
                            inner.Write((uint)am.Children.Count);
                            foreach (var id in am.Children) inner.Write(id);
                        });
                        break;

                    case AudioBus ab:
                        WriteWithHeader(w, 8, bnkVersion, inner =>
                        {
                            inner.Write(ab.SelfId);
                            inner.Write(ab.ParentBusId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, ab.NodeBaseParams, bnkVersion);
                        });
                        break;

                    case BlendContainer bc:
                        WriteWithHeader(w, 9, bnkVersion, inner =>
                        {
                            inner.Write(bc.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, bc.NodeBaseParams, bnkVersion);
                            inner.Write((uint)bc.Children.Count);
                            foreach (var id in bc.Children) inner.Write(id);
                        });
                        break;

                    case MusicSegment ms2:
                        WriteWithHeader(w, 10, bnkVersion, inner =>
                        {
                            inner.Write(ms2.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, ms2.NodeBaseParams, bnkVersion);
                            inner.Write((uint)ms2.MusicTrackIds.Count);
                            foreach (var id in ms2.MusicTrackIds) inner.Write(id);
                        });
                        break;

                    case MusicTrack mt:
                        WriteWithHeader(w, 11, bnkVersion, inner =>
                        {
                            inner.Write(mt.SelfId);
                            inner.Write((uint)mt.FileIds.Count);
                            foreach (var id in mt.FileIds) inner.Write(id);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, mt.NodeBaseParams, bnkVersion);
                        });
                        break;

                    case MusicSwitch msw:
                        WriteWithHeader(w, 12, bnkVersion, inner =>
                        {
                            inner.Write(msw.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, msw.NodeBaseParams, bnkVersion);
                            inner.Write((uint)msw.Children.Count);
                            foreach (var id in msw.Children) inner.Write(id);
                        });
                        break;

                    case MusicPlaylist mpl:
                        WriteWithHeader(w, 13, bnkVersion, inner =>
                        {
                            inner.Write(mpl.SelfId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, mpl.NodeBaseParams, bnkVersion);
                            inner.Write((uint)mpl.MusicTrackIds.Count);
                            foreach (var id in mpl.MusicTrackIds) inner.Write(id);
                        });
                        break;

                    case Attenuation att:
                        WriteWithHeader(w, 14, bnkVersion, inner =>
                        {
                            inner.Write(att.SelfId);
                            inner.Write(att.EnableHeightSpread);
                        });
                        break;

                    case DialogueEvent de:
                        WriteWithHeader(w, 15, bnkVersion, inner =>
                        {
                            inner.Write(de.SelfId);
                            inner.Write(de.DecisionTreeDepth);
                        });
                        break;

                    case MotionBus mob:
                        WriteWithHeader(w, 16, bnkVersion, inner =>
                        {
                            inner.Write(mob.SelfId);
                            inner.Write(mob.ParentId);
                        });
                        break;

                    case MotionFx mfx:
                        WriteWithHeader(w, 17, bnkVersion, inner =>
                        {
                            inner.Write(mfx.SelfId);
                            inner.Write(mfx.MotionFxPluginId);
                        });
                        break;

                    case Effect eff:
                        WriteWithHeader(w, 18, bnkVersion, inner =>
                        {
                            inner.Write(eff.SelfId);
                            inner.Write(eff.PluginId);
                            inner.Write(eff.IsShareSet);
                        });
                        break;

                    case AuxiliaryBus axb:
                        WriteWithHeader(w, 19, bnkVersion, inner =>
                        {
                            inner.Write(axb.SelfId);
                            inner.Write(axb.ParentBusId);
                            BnkBinaryHelpers.WriteNodeBaseParams(inner, axb.NodeBaseParams, bnkVersion);
                        });
                        break;

                    case Settings st:
                        WriteWithHeader(w, 1, bnkVersion, inner =>
                        {
                            inner.Write(st.SelfId);
                            inner.Write(st.NumSettings);
                            foreach (var t in st.SettingTypes) inner.Write(t);
                            foreach (var v in st.SettingValues) inner.Write(v);
                        });
                        break;

                    default:
                        // Completely unknown type — passthrough
                        return obj.RawData ?? Array.Empty<byte>();
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Writes a type-byte + length-uint32 header, then delegates to <paramref name="writeBody"/>
        /// to write the payload, then back-patches the length field.
        /// </summary>
        private static void WriteWithHeader(BinaryWriter w, byte type, uint bnkVersion, Action<BinaryWriter> writeBody)
        {
            w.Write(type);
            long lenPos = w.BaseStream.Position;
            w.Write((uint)0); // placeholder
            writeBody(w);
            long endPos = w.BaseStream.Position;
            w.BaseStream.Position = lenPos;
            w.Write((uint)(endPos - lenPos - 4));
            w.BaseStream.Position = endPos;
        }
    }
}
