using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Professional WPK packager mirroring Riot's internal format.
    /// Bundles .wem files into a single .wpk container with proper alignment and headers.
    /// </summary>
    public static class WpkPackager
    {
        private const string WPK_MAGIC = "r3d2";
        private const uint WPK_VERSION = 1;
        private const int ALIGNMENT = 8; // Riot usually uses 8 or 16 byte alignment

        public static void PackageWems(string inputFolder, string outputPath)
        {
            WemAdditionLogger.LogSection("WPK PACKAGING START");
            WemAdditionLogger.Log($"Source Folder: {inputFolder}");
            WemAdditionLogger.Log($"Output File: {outputPath}");

            if (!Directory.Exists(inputFolder))
                throw new DirectoryNotFoundException($"Source folder not found: {inputFolder}");

            // 1. Gather WEM files and sort by ID (numerical)
            var wemFiles = Directory.GetFiles(inputFolder, "*.wem")
                .Select(path =>
                {
                    string filename = Path.GetFileNameWithoutExtension(path);
                    if (uint.TryParse(filename, out uint id))
                    {
                        return new { Id = id, Path = path };
                    }
                    return null;
                })
                .Where(x => x != null)
                .OrderBy(x => x.Id)
                .ToList();

            if (wemFiles.Count == 0)
                throw new FileNotFoundException("No .wem files found in the source folder.");

            WemAdditionLogger.Log($"Found {wemFiles.Count} WEM files.");

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // 2. Write Header
                writer.Write(Encoding.ASCII.GetBytes(WPK_MAGIC));
                writer.Write(WPK_VERSION);
                writer.Write((uint)wemFiles.Count);

                // 3. Reserve space for the Offset Table
                long offsetTableStart = fs.Position;
                int[] entryOffsets = new int[wemFiles.Count];
                fs.Position += wemFiles.Count * 4;
                
                // Align before metadata entries
                AlignStream(fs, ALIGNMENT);

                // 4. Write Metadata Entries (Headers)
                // We don't know the DataOffsets yet, so we'll do two passes or seek back.
                // Pass 1: Write placeholders for headers and calculate entry positions.
                for (int i = 0; i < wemFiles.Count; i++)
                {
                    entryOffsets[i] = (int)fs.Position;
                    
                    string filename = $"{wemFiles[i].Id}.wem";
                    
                    // Placeholder for DataOffset (uint32)
                    writer.Write(0u); 
                    // DataLength (uint32)
                    writer.Write((uint)new FileInfo(wemFiles[i].Path).Length);
                    // FilenameSize (uint32)
                    writer.Write((uint)filename.Length);
                    // Filename (UTF-16LE)
                    writer.Write(Encoding.Unicode.GetBytes(filename));
                    
                    // Align each entry
                    AlignStream(fs, ALIGNMENT);
                }

                // 5. Write Raw WEM Data and fill in DataOffsets
                for (int i = 0; i < wemFiles.Count; i++)
                {
                    uint dataOffset = (uint)fs.Position;
                    byte[] data = File.ReadAllBytes(wemFiles[i].Path);
                    
                    // Seek back to header to update DataOffset
                    long currentPos = fs.Position;
                    fs.Position = entryOffsets[i];
                    writer.Write(dataOffset);
                    
                    // Return to data section and write WEM
                    fs.Position = currentPos;
                    writer.Write(data);
                    
                    // Align after each WEM
                    AlignStream(fs, ALIGNMENT);
                    
                    WemAdditionLogger.Log($"  Packaged: {wemFiles[i].Id}.wem (Offset: {dataOffset}, Size: {data.Length})");
                }

                // 6. Final Pass: Fill in the Offset Table
                fs.Position = offsetTableStart;
                foreach (int offset in entryOffsets)
                {
                    writer.Write(offset);
                }
            }

            WemAdditionLogger.LogSuccess($"WPK packaged successfully: {outputPath}");
        }

        private static void AlignStream(Stream stream, int alignment)
        {
            long pos = stream.Position;
            long mod = pos % alignment;
            if (mod != 0)
            {
                long padding = alignment - mod;
                for (int i = 0; i < padding; i++)
                    stream.WriteByte(0);
            }
        }
    }
}
