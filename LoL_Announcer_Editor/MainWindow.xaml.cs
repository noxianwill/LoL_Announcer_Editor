using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace LoL_Announcer_Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private WaveOutEvent? _outputDevice;
        private AudioFileReader? _audioFile;
        private CancellationTokenSource? _playbackCts;
        private readonly object _playLock = new object();
        private string? _currentTempWem;
        private string? _currentTempWav;
        private Dictionary<uint, string> _wemWavCache = new Dictionary<uint, string>();
        private ObservableCollection<TreeNode> _rootNodes = new ObservableCollection<TreeNode>();
        
        // New fields for WEM addition feature
        private BNKSections? _currentBnkSections;
        private List<AudioData>? _currentWpkWemFiles;
        private WemInformation? _currentWemInformation;  // Stores slot mapping for preserving WPK structure
        private string? _currentBnkPath;
        private string? _currentWpkPath;
        private Dictionary<uint, string> _eventNameLookup = new Dictionary<uint, string>();

        // Replace old BNK load/save logic with full-fidelity model
        private BnkFullFidelity _fullFidelityBnk = new BnkFullFidelity();
        private byte[]? _originalBnkBytes = null;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                this.Closed += MainWindow_Closed;
                
                // Log application startup

                
                // Load settings on startup
                SettingsManager.LoadSettings();
                
                // Validate critical dependencies
                ValidateDependencies();
                

            }
            catch (Exception ex)
            {

                System.Windows.MessageBox.Show($"Failed to initialize application:\n{ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw; // Re-throw to let the global handler catch it
            }
        }

        private void ValidateDependencies()
        {
            try
            {
                // Check if vgmstream-cli.exe exists
                string vgmstreamExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundlibs", "vgmstream", "vgmstream-cli.exe");
                if (!System.IO.File.Exists(vgmstreamExe))
                {

                }
                else
                {

                }

                // Check if temp directory can be created
                string tempDir = GetCustomTempDir();


                // Test NAudio initialization
                try
                {
                    var testDevice = new WaveOutEvent();
                    testDevice.Dispose();

                }
                catch (Exception ex)
                {

                }
            }
            catch (Exception ex)
            {

            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            string tempDir = GetCustomTempDir();
            try
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(tempDir))
                    {
                        try { System.IO.File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        private void OpenBinFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "BIN file (*.bin)|*.bin|All files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                BinFilePath.Text = dialog.FileName;
            }
        }

        private void OpenWpkFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "WPK files (*.wpk)|*.wpk|All files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                WpkFilePath.Text = dialog.FileName;
            }
        }

        private void OpenBnkFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "BNK files (*.bnk)|*.bnk|All files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                BnkFilePath.Text = dialog.FileName;
            }
        }

        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OptionsWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        private void ExportWwiseProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate that we have parsed data
                if (_currentBnkSections == null || _rootNodes.Count == 0)
                {
                    System.Windows.MessageBox.Show(
                        "Please parse BIN, WPK, and BNK files first before exporting.",
                        "Export Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Step 1: Select source Wwise project folder
                var sourceFolderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select the source Wwise project folder (with imported audio)",
                    ShowNewFolderButton = false
                };

                if (sourceFolderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                string sourceFolder = sourceFolderDialog.SelectedPath;

                // Validate the source folder is a Wwise project
                if (!WwiseProjectExporter.ValidateWwiseProject(sourceFolder, out string validationError))
                {
                    System.Windows.MessageBox.Show(
                        $"Invalid Wwise project folder:\n{validationError}",
                        "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Step 2: Select destination folder
                var destFolderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select destination folder for the exported Wwise project",
                    ShowNewFolderButton = true
                };

                if (destFolderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                string destFolder = destFolderDialog.SelectedPath;

                // Step 3: Perform the export
                var exporter = new WwiseProjectExporter();
                exporter.Export(
                    _rootNodes,
                    _currentBnkSections,
                    _eventNameLookup,
                    sourceFolder,
                    destFolder);

                // Get the output folder name
                string projectName = Path.GetFileNameWithoutExtension(
                    Directory.GetFiles(sourceFolder, "*.wproj").FirstOrDefault() ?? "project");
                string outputPath = Path.Combine(destFolder, projectName + "_LoLTemplate");

                System.Windows.MessageBox.Show(
                    $"Wwise project template exported successfully!\n\nOutput: {outputPath}\n\nOpen this folder in Wwise to see the generated hierarchy.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"An error occurred during export:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ParseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string binPath = BinFilePath.Text;
                string wpkPath = WpkFilePath.Text;
                string bnkPath = BnkFilePath.Text;





                // Validate file paths
                if (string.IsNullOrEmpty(binPath) || !System.IO.File.Exists(binPath))
                {

                    System.Windows.MessageBox.Show("Please select a valid BIN file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(wpkPath) || !System.IO.File.Exists(wpkPath))
                {

                    System.Windows.MessageBox.Show("Please select a valid WPK file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(bnkPath) || !System.IO.File.Exists(bnkPath))
                {

                    System.Windows.MessageBox.Show("Please select a valid BNK file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // CRITICAL FIX: Load the original BNK bytes first for full-fidelity saving

                try
                {
                    _originalBnkBytes = File.ReadAllBytes(bnkPath);

                }
                catch (Exception ex)
                {

                    System.Windows.MessageBox.Show($"Failed to load BNK file: {ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Parse BIN for event name mapping

                var stringHashes = BinUtils.ParseBinFile(binPath);
                var eventNameLookup = new Dictionary<uint, string>();
                if (stringHashes != null)
                {
                    foreach (var sh in stringHashes)
                    {
                        eventNameLookup[sh.Hash] = sh.String;
                    }

                }
                else
                {

                }

                // Parse BNK using BNKSections and ParseEventBnkFile

                var sections = new BNKSections();
                if (!BnkExtractor.ParseEventBnkFile(bnkPath, sections))
                {

                    System.Windows.MessageBox.Show("Failed to parse BNK file. Please check if the file is valid.", "Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }



                
                // Store BNK structure in memory for WEM addition feature
                _currentBnkSections = sections;
                _currentBnkPath = bnkPath;
                _currentWpkPath = wpkPath;

                // Initialize full-fidelity BNK structure

                _fullFidelityBnk = new BnkFullFidelity();
                bool fullFidelityOk = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, _fullFidelityBnk, sections);
                if (!fullFidelityOk)
                {

                }
                else
                {


                    // Log original BNK structure for debugging


                    LogBnkStructureToDebug(_fullFidelityBnk, "ORIGINAL");
                }

                // Build a set of BNK event IDs
                var bnkEventIds = new HashSet<uint>(sections.Events.Select(e => e.SelfId));


                // Parse WPK with stringHashes and bnkEventIds

                var wemInfo = WpkExtractor.ParseWpkFile(wpkPath, stringHashes ?? new List<StringHash>(), bnkEventIds);
#pragma warning disable CS8602 // Dereference of a possibly null reference

#pragma warning restore CS8602


                
                // Store WPK WEM files and slot information in memory for WEM addition feature
                _currentWpkWemFiles = wemInfo.SortedWemDataList;
                _currentWemInformation = wemInfo;  // Store full WemInformation for slot preservation

                // Build tree structure: root node is WPK file, children are events, grandchildren are WEM files
                var root = new TreeNode { Name = System.IO.Path.GetFileName(wpkPath), NodeType = NodeType.Root, ShowCheckbox = true };

                // Build the tree in the BNK event loop (as before)

                foreach (var bnkEvent in sections.Events)
                {
                    string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var name) ? name : $"Event_{bnkEvent.SelfId}";
                    var eventNode = new TreeNode { Name = eventName, NodeType = NodeType.Event, ShowCheckbox = true };



                    foreach (var eventId in bnkEvent.EventIds)
                    {
                        var eventAction = sections.EventActions.Find(ea => ea.SelfId == eventId);
                        if (eventAction == null)
                        {

                            continue;
                        }


                        // Check if the SoundObjectId is a container (RandomContainer, SwitchContainer, etc.)
                        var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == eventAction.SoundObjectId);
                        var switchContainer = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == eventAction.SoundObjectId);

                        TreeNode parentForWems = eventNode; // Default: add WEMs directly to event

                        // If there's a container, create a container node
                        if (randomContainer != null || switchContainer != null)
                        {
                            uint containerId = randomContainer?.SelfId ?? switchContainer.SelfId;
                            var containerNode = new TreeNode
                            {
                                Name = $"{containerId}",
                                NodeType = NodeType.Container,
                                ShowCheckbox = true
                            };
                            containerNode.Parent = eventNode;
                            eventNode.Children.Add(containerNode);
                            parentForWems = containerNode; // Add WEMs to container instead

                        }

                        // Now add the WEM files
                        var fileIds = ResolveObjectToFileIds(eventAction.SoundObjectId, sections);
                        foreach (var fileId in fileIds)
                        {
                            var wemFile = wemInfo.SortedWemDataList.Find(w => w.Id == fileId);
                            if (wemFile == null)
                            {

                                continue;
                            }

                            // Create a deep copy of the AudioData to avoid shared references
                            var wemDataCopy = new AudioData
                            {
                                Id = wemFile.Id,
                                Length = wemFile.Length,
                                Data = wemFile.Data.ToArray() // Deep copy the byte array
                            };

                            var wemNode = new TreeNode
                            {
                                Name = $"{fileId}",
                                WemData = wemDataCopy, // Use the copy instead of the original
                                NodeType = NodeType.WemFile,
                                ShowCheckbox = true
                            };
                            wemNode.Parent = parentForWems; // Set parent (either event or container)
                            parentForWems.Children.Add(wemNode);

                        }
                    }
                    // Always add the event node to the root, even if it has zero WEM children
                    eventNode.Parent = root; // Set parent before adding to children
                    root.Children.Add(eventNode);
                }

                // Sort event nodes in the treeview according to the order in the bin file
                var eventOrder = stringHashes?.Select(sh => sh.String).ToList() ?? new List<string>();
                root.Children = new ObservableCollection<TreeNode>(
                    root.Children
                        .OrderBy(node => {
                            int idx = eventOrder.IndexOf(node.Name);
                            return idx == -1 ? int.MaxValue : idx;
                        })
                );

                // Use ObservableCollection for root nodes
                _rootNodes.Clear();
                // Add an 'Unreferenced WEMs' group for any WPK files not mapped from BNK
                var referencedIds = new HashSet<uint>();
                foreach (var evt in root.Children)
                {
                    foreach (var child in evt.Children)
                    {
                        if (child.NodeType == NodeType.WemFile && child.WemData != null)
                        {
                            referencedIds.Add(child.WemData.Id);
                        }
                        else if (child.NodeType == NodeType.Container)
                        {
                            foreach (var gc in child.Children)
                            {
                                if (gc.NodeType == NodeType.WemFile && gc.WemData != null)
                                    referencedIds.Add(gc.WemData.Id);
                            }
                        }
                    }
                }
                var unref = new TreeNode { Name = "Unreferenced WEMs", NodeType = NodeType.Root, ShowCheckbox = true };
                if (wemInfo?.SortedWemDataList != null)
                {
                    foreach (var ad in wemInfo.SortedWemDataList)
                    {
                        if (!referencedIds.Contains(ad.Id))
                        {
                            var node = new TreeNode { Name = $"{ad.Id}", NodeType = NodeType.WemFile, ShowCheckbox = true, WemData = new AudioData{ Id=ad.Id, Length=ad.Length, Data=ad.Data.ToArray() } };
                            node.Parent = unref;
                            unref.Children.Add(node);
                        }
                    }
                }
                if (unref.Children.Count > 0)
                {
                    unref.Parent = root;
                    root.Children.Add(unref);

                }
                _rootNodes.Add(root);
                ExpandAllNodes(root);
                TreeView.ItemsSource = _rootNodes;
                // Defs.LogAction($"ParseFilesButton_Click: TreeView populated with {root.Children.Count} root nodes");

                // Log events in the order of the bin file
                if (stringHashes != null)
                {
                    foreach (var sh in stringHashes)
                    {
                        var bnkEvent = sections.Events.FirstOrDefault(e => e.SelfId == sh.Hash);
                        if (bnkEvent == null)
                            continue;
                        string eventName = sh.String;
                        var eventNode = root.Children.FirstOrDefault(n => n.Name == eventName);
                        if (eventNode == null)
                            continue;
                        // Separator for readability

                        // Log all event parameters






                        foreach (var wemNode in eventNode.Children)
                        {
                            var wem = wemNode.WemData;
                            if (wem != null)
                            {



                            }
                        }
                    }
                }

                // Log totals at the end




                // Count WEM usage across all events
                var wemUsage = new Dictionary<uint, int>();
                foreach (var eventNode in root.Children)
                {
                    foreach (var wemNode in eventNode.Children)
                    {
                        if (wemNode.WemData != null)
                        {
                            if (!wemUsage.ContainsKey(wemNode.WemData.Id))
                                wemUsage[wemNode.WemData.Id] = 0;
                            wemUsage[wemNode.WemData.Id]++;
                        }
                    }
                }
                int wemUsageTotal = wemUsage.Values.Sum();

                
                // Enable the Save BNK file button since we now have BNK structure in memory
                SaveBnkFileButton.IsEnabled = true;


                // Store event name lookup
                _eventNameLookup = eventNameLookup;
            }
            catch (Exception ex)
            {

                System.Windows.MessageBox.Show($"An error occurred while parsing files:\n{ex.Message}", "Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Add this method after the existing methods but before ParseFilesButton_Click
        private List<uint> ResolveObjectToFileIds(uint objectId, BNKSections sections)
        {
            var fileIds = new List<uint>();
            
            // First, check if it's a direct Sound
            var sound = sections.Sounds.FirstOrDefault(s => s.SelfId == objectId);
            if (sound != null)
            {
                if (sound.StreamFileID != 0)
                {
                    fileIds.Add(sound.StreamFileID);
                }
                else
                {
                    fileIds.Add(sound.FileId);
                }
                return fileIds;
            }
            
            // Check if it's a RandomContainer
            var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == objectId);
            if (randomContainer != null)
            {
                // Prefer ChildrenIds (hierarchy children) over SoundIds (playlist)
                var childIds = randomContainer.ChildrenIds.Count > 0
                    ? randomContainer.ChildrenIds
                    : randomContainer.SoundIds;
                foreach (var childId in childIds)
                {
                    var childFileIds = ResolveObjectToFileIds(childId, sections);
                    fileIds.AddRange(childFileIds);
                }
                return fileIds;
            }
            
            // Check if it's a SwitchContainer
            var switchContainer = sections.SwitchContainers.FirstOrDefault(s => s.SelfId == objectId);
            if (switchContainer != null)
            {
                foreach (var childId in switchContainer.Children)
                {
                    var childFileIds = ResolveObjectToFileIds(childId, sections);
                    fileIds.AddRange(childFileIds);
                }
                return fileIds;
            }
            
            // Check if it's a MusicSegment or MusicPlaylist (MusicContainer)
            var musicContainer = sections.MusicSegments.FirstOrDefault(m => m.SelfId == objectId)
                ?? sections.MusicPlaylists.FirstOrDefault(m => m.SelfId == objectId);
            if (musicContainer != null)
            {
                foreach (var childId in musicContainer.MusicTrackIds)
                {
                    var childFileIds = ResolveObjectToFileIds(childId, sections);
                    fileIds.AddRange(childFileIds);
                }
                return fileIds;
            }
            
            // Check if it's a MusicTrack
            var musicTrack = sections.MusicTracks.FirstOrDefault(m => m.SelfId == objectId);
            if (musicTrack != null)
            {
                fileIds.AddRange(musicTrack.FileIds);
                return fileIds;
            }
            
            // Check if it's a MusicSwitch
            var musicSwitch = sections.MusicSwitches.FirstOrDefault(m => m.SelfId == objectId);
            if (musicSwitch != null)
            {
                foreach (var childId in musicSwitch.Children)
                {
                    var childFileIds = ResolveObjectToFileIds(childId, sections);
                    fileIds.AddRange(childFileIds);
                }
                return fileIds;
            }
            
            return fileIds;
        }

        private void DeleteTreeView_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                if (TreeView.ItemsSource is ObservableCollection<TreeNode> roots)
                {

                    foreach (var root in roots)
                    {

                    }
                }
                else
                {

                }
                _rootNodes.Clear();
                TreeView.ItemsSource = null;

                // Unload all file paths
                BinFilePath.Text = string.Empty;
                WpkFilePath.Text = string.Empty;
                BnkFilePath.Text = string.Empty;

            }
            catch (Exception ex)
            {

                throw;
            }
        }

        private async void ExtractSelection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = GetSelectedItems();
                if (selectedItems.Count == 0)
                {
                    System.Windows.MessageBox.Show("Please select at least one item to extract.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Ask user for extraction directory
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
                folderDialog.Description = "Select folder to extract files to";
                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                string extractDir = folderDialog.SelectedPath;


                // Calculate total files to extract (WEM + OGG)
                int totalFiles = 0;
                foreach (var item in selectedItems)
                {
                    totalFiles += CountFilesToExtract(item);
                }

                // Show progress bar
                ExtractionProgressBar.Visibility = Visibility.Visible;
                ProgressText.Visibility = Visibility.Visible;
                ExtractionProgressBar.Value = 0;
                ProgressText.Text = $"Extracting... 0 / {totalFiles}";

                int extractedCount = 0;
                int progress = 0;
                await Task.Run(() =>
                {
                    foreach (var item in selectedItems)
                    {
                        ExtractItemWithProgress(item, extractDir, totalFiles, ref progress, ref extractedCount);
                    }
                });

                ExtractionProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;

                System.Windows.MessageBox.Show($"Successfully extracted {extractedCount} out of {totalFiles} files to:\n{extractDir}",
                    "Extraction Complete", MessageBoxButton.OK, MessageBoxImage.Information);


            }
            catch (Exception ex)
            {
                ExtractionProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;

                System.Windows.MessageBox.Show($"Extraction failed:\n{ex.Message}", "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Recursively count files to extract (WEM + OGG)
        private int CountFilesToExtract(TreeNode node)
        {
            int count = 0;
            if (node.NodeType == NodeType.WemFile)
            {
                count += 1; // WEM
                if (SettingsManager.ExtractOgg)
                    count += 1; // OGG
            }
            else
            {
                foreach (var child in node.Children)
                {
                    count += CountFilesToExtract(child);
                }
            }
            return count;
        }

        // Recursively extract and update progress for each file
        private void ExtractItemWithProgress(TreeNode item, string extractDir, int totalFiles, ref int progress, ref int extractedCount)
        {
            if (item.NodeType == NodeType.WemFile)
            {
                // Find parent event node for folder name
                string eventFolder = extractDir;
                TreeNode parentEvent = item.Parent;
                while (parentEvent != null && parentEvent.NodeType != NodeType.Event)
                {
                    parentEvent = parentEvent.Parent;
                }
                if (parentEvent != null)
                {
                    eventFolder = System.IO.Path.Combine(extractDir, SanitizeFileName(parentEvent.Name));
                    if (!Directory.Exists(eventFolder))
                        Directory.CreateDirectory(eventFolder);
                }

                // Extract WEM
                if (item.WemData != null)
                {
                    string wemFileName = !string.IsNullOrEmpty(item.ReplacementFileName)
                        ? System.IO.Path.GetFileNameWithoutExtension(item.ReplacementFileName)
                        : item.Name;
                    string wemPath = System.IO.Path.Combine(eventFolder, SanitizeFileName(wemFileName) + ".wem");
                    try
                    {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                        File.WriteAllBytes(wemPath, item.WemData.Data);
#pragma warning restore CS8602
#pragma warning disable CS8602 // Dereference of a possibly null reference
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type

#pragma warning restore CS8600
#pragma warning restore CS8602
                        extractedCount++;
                    }
                    catch { }
                }
                progress++;
                int currentProgress = progress;
                Dispatcher.Invoke(() =>
                {
                    ExtractionProgressBar.Value = (double)currentProgress / totalFiles * 100;
                    ProgressText.Text = $"Extracting... {currentProgress} / {totalFiles}";
                });

                // Extract OGG if enabled
                if (SettingsManager.ExtractOgg && item.WemData != null)
                {
                    string oggPath = System.IO.Path.Combine(eventFolder, SanitizeFileName(item.Name) + ".ogg");
                    try
                    {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                        if (ConvertWemToOgg(item.WemData.Data, oggPath))
#pragma warning restore CS8602
                        {
                            extractedCount++;
                        }
                    }
                    catch { }
                    progress++;
                    currentProgress = progress;
                    Dispatcher.Invoke(() =>
                    {
                        ExtractionProgressBar.Value = (double)currentProgress / totalFiles * 100;
                        ProgressText.Text = $"Extracting... {currentProgress} / {totalFiles}";
                    });
                }
            }
            else
            {
                foreach (var child in item.Children)
                {
                    ExtractItemWithProgress(child, extractDir, totalFiles, ref progress, ref extractedCount);
                }
            }
        }

        private List<TreeNode> GetSelectedItems()
        {
            var selected = new List<TreeNode>();
            foreach (var node in GetAllNodes())
            {
                // Add checked root node
                if (node.NodeType == NodeType.Root && node.IsChecked == true)
                {
                    selected.Add(node);
                }
                // Add checked events (but not if root is checked)
                else if (node.NodeType == NodeType.Event && node.IsChecked == true && (node.Parent == null || node.Parent.IsChecked != true))
                {
                    selected.Add(node);
                }
                // Add checked WEM files (but not if parent event or root is checked)
                else if (node.NodeType == NodeType.WemFile && node.IsChecked == true && (node.Parent == null || node.Parent.IsChecked != true) && (node.Parent?.Parent == null || node.Parent.Parent.IsChecked != true))
                {
                    selected.Add(node);
                }
            }
            return selected;
        }

        private List<TreeNode> GetAllNodes()
        {
            var result = new List<TreeNode>();
            foreach (var root in TreeView.ItemsSource as IEnumerable<TreeNode> ?? new List<TreeNode>())
                CollectAllNodes(root, result);
            return result;
        }
        private void CollectAllNodes(TreeNode node, List<TreeNode> result)
        {
            result.Add(node);
            foreach (var child in node.Children)
                CollectAllNodes(child, result);
        }

        private bool ExtractItem(TreeNode item, string extractDir)
        {
            try
            {

                switch (item.NodeType)
                {
                    case NodeType.Root:
                        // Extract all events and their WEM files
                        foreach (var eventNode in item.Children)
                        {
                            ExtractEventNode(eventNode, extractDir);
                        }
                        return true;
                    case NodeType.Event:
                        // Extract this event and all its WEM files
                        return ExtractEventNode(item, extractDir);
                    case NodeType.Container:
                        // Extract container's WEM files to parent event folder
                        return ExtractWemFileToEventFolder(item, extractDir);
                    case NodeType.WemFile:
                        // Always extract to parent event folder
                        return ExtractWemFileToEventFolder(item, extractDir);
                    default:

                        return false;
                }
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        private bool ExtractEventNode(TreeNode eventNode, string extractDir)
        {
            try
            {
                // Create event folder
                string eventFolder = System.IO.Path.Combine(extractDir, SanitizeFileName(eventNode.Name));
                Directory.CreateDirectory(eventFolder);
                
                int extractedCount = 0;
                
                // Helper to recursively extract WEM files
                void ExtractWemFilesRecursive(TreeNode node, string folder)
                {
                    foreach (var child in node.Children)
                    {
                        if (child.NodeType == NodeType.WemFile)
                        {
                            if (ExtractWemFile(child, folder))
                            {
                                extractedCount++;
                            }
                        }
                        else if (child.NodeType == NodeType.Container)
                        {
                            // Recursively extract from container
                            ExtractWemFilesRecursive(child, folder);
                        }
                    }
                }

                // Extract all WEM files under this event (including those in containers)
                ExtractWemFilesRecursive(eventNode, eventFolder);
                

                return extractedCount > 0;
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        private bool ExtractWemFile(TreeNode wemNode, string extractDir)
        {
            try
            {
                if (wemNode.WemData == null)
                {

                    return false;
                }

                // Always extract as WEM file
                string wemFileName2 = !string.IsNullOrEmpty(wemNode.ReplacementFileName)
                    ? System.IO.Path.GetFileNameWithoutExtension(wemNode.ReplacementFileName)
                    : wemNode.Name;
                string wemPath = System.IO.Path.Combine(extractDir, SanitizeFileName(wemFileName2) + ".wem");
                File.WriteAllBytes(wemPath, wemNode.WemData.Data);


                // Optionally extract as OGG file
                if (SettingsManager.ExtractOgg)
                {
                    string oggPath = System.IO.Path.Combine(extractDir, SanitizeFileName(wemNode.Name) + ".ogg");
                    if (ConvertWemToOgg(wemNode.WemData.Data, oggPath))
                    {

                    }
                }

                return true;
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        private bool ConvertWemToOgg(byte[] wemData, string oggPath)
        {
            try
            {
                // Use vgmstream to convert WEM to OGG
                string vgmstreamExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundlibs", "vgmstream", "vgmstream-cli.exe");
                
                if (!File.Exists(vgmstreamExe))
                {

                    return false;
                }

                // Create temporary WEM file
                string tempWemPath = System.IO.Path.Combine(GetCustomTempDir(), $"temp_{Guid.NewGuid()}.wem");
                File.WriteAllBytes(tempWemPath, wemData);

                // Convert using vgmstream
                var startInfo = new ProcessStartInfo
                {
                    FileName = vgmstreamExe,
                    Arguments = $"\"{tempWemPath}\" -o \"{oggPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                    process.WaitForExit();
#pragma warning restore CS8602
                    
                    // Clean up temp file
                    try { File.Delete(tempWemPath); } catch { }
                    
                    if (process.ExitCode == 0 && File.Exists(oggPath))
                    {
                        return true;
                    }
                    else
                    {

                        return false;
                    }
                }
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove or replace invalid characters
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            
            // Limit length
            if (fileName.Length > 200)
            {
                fileName = fileName.Substring(0, 200);
            }
            
            return fileName;
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedNode = TreeView.SelectedItem as TreeNode;
            
            // Update SaveAsBnkWpkButton enabled state
            SaveAsBnkWpkButton.IsEnabled = IsRootNodeSelected();
            
            // Update AddWemFileButton enabled state - enable when an event or container is selected
            AddWemFileButton.IsEnabled = selectedNode != null && (selectedNode.NodeType == NodeType.Event || selectedNode.NodeType == NodeType.Container);
            
            if (selectedNode != null && selectedNode.NodeType == NodeType.WemFile && selectedNode.WemData != null)
            {
                // Only auto-play if the setting is enabled
                if (SettingsManager.PlayOnClick)
                {

#pragma warning disable CS8602 // Dereference of a possibly null reference
                    PlayWem(selectedNode.WemData!);
#pragma warning restore CS8602
                }
                else
                {

                }
            }
        }

        private void PlaySoundButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedNode = TreeView.SelectedItem as TreeNode;
            if (selectedNode != null && selectedNode.NodeType == NodeType.WemFile && selectedNode.WemData != null)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                PlayWem(selectedNode.WemData!);
#pragma warning restore CS8602
            }
        }

        private string GetCustomTempDir()
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LoL Announcer Manager");
            if (!System.IO.Directory.Exists(tempDir))
                System.IO.Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        private async void PlayWem(AudioData wemData)
        {
            try
            {
                // Cancel any ongoing playback/conversion
                _playbackCts?.Cancel();
                await Task.Delay(50); // Give a moment for cleanup
                _playbackCts = new CancellationTokenSource();
                var token = _playbackCts.Token;

                if (wemData?.Data == null || wemData.Data.Length == 0)
                {

                    return;
                }

                string tempDir = GetCustomTempDir();
                string wavFilePath;
                uint wemId = wemData.Id;
                if (_wemWavCache.TryGetValue(wemId, out wavFilePath) && System.IO.File.Exists(wavFilePath))
                {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
                    // Play from cache

#pragma warning restore CS8600
                    lock (_playLock)
                    {
                        _audioFile = new AudioFileReader(wavFilePath);
                        _outputDevice = new WaveOutEvent();
                        _outputDevice.Init(_audioFile);
                        _outputDevice.PlaybackStopped += (s, e) =>
                        {
                            CleanupPlayback();
                        };
                        _outputDevice.Play();
                    }
                    return;
                }

                string vgmstreamExe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundlibs", "vgmstream", "vgmstream-cli.exe");
                if (!System.IO.File.Exists(vgmstreamExe))
                {

                    System.Windows.MessageBox.Show("vgmstream-cli.exe not found. Please ensure it's in the soundlibs/vgmstream directory.");
                    return;
                }

                string wemFileName = Guid.NewGuid().ToString() + ".wem";
                string wavFileName = Guid.NewGuid().ToString() + ".wav";
                _currentTempWem = System.IO.Path.Combine(tempDir, wemFileName);
                _currentTempWav = System.IO.Path.Combine(tempDir, wavFileName);
                

                System.IO.File.WriteAllBytes(_currentTempWem, wemData.Data);

                try
                {
                    var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = vgmstreamExe;
                    process.StartInfo.Arguments = $"-o \"{_currentTempWav}\" \"{_currentTempWem}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    process.Start();
                    await process.WaitForExitAsync(token);

                    if (token.IsCancellationRequested)
                    {

                        CleanupPlayback();
                        return;
                    }

                    if (!System.IO.File.Exists(_currentTempWav))
                    {

                        System.Windows.MessageBox.Show("Failed to convert WEM to WAV.");
                        // Do not delete temp files here
                        return;
                    }

                    // Add to cache
                    _wemWavCache[wemId] = _currentTempWav;


                    lock (_playLock)
                    {
                        _audioFile = new AudioFileReader(_currentTempWav);
                        _outputDevice = new WaveOutEvent();
                        _outputDevice.Init(_audioFile);
                        _outputDevice.PlaybackStopped += (s, e) =>
                        {
                            CleanupPlayback();
                        };
                        _outputDevice.Play();
                    }
                }
                catch (OperationCanceledException)
                {

                    CleanupPlayback();
                }
                catch (Exception ex)
                {

                    System.Windows.MessageBox.Show($"Failed to play sound: {ex.Message}");
                    CleanupPlayback();
                }
            }
            catch (Exception ex)
            {

                System.Windows.MessageBox.Show($"Unexpected error playing sound: {ex.Message}");
            }
        }

        private void StopAllSoundsButton_Click(object sender, RoutedEventArgs e)
        {
            StopAllSounds();
        }

        private void StopAllSounds()
        {
            lock (_playLock)
            {
                _playbackCts?.Cancel();
                _outputDevice?.Stop();
                CleanupPlayback();
            }
        }

        private void CleanupPlayback()
        {
            lock (_playLock)
            {
                _audioFile?.Dispose();
                _outputDevice?.Dispose();
                _audioFile = null;
                _outputDevice = null;
                _playbackCts = null;
                // Do not delete temp files here
            }
        }

        private void CleanupTempFiles() { /* No-op for session caching */ }

        private bool ExtractWemFileToEventFolder(TreeNode wemNode, string extractDir)
        {
            if (wemNode.NodeType != NodeType.WemFile || wemNode.Parent == null)
                return false;
            string eventFolder = System.IO.Path.Combine(extractDir, wemNode.Parent.Name);
            Directory.CreateDirectory(eventFolder);
            return ExtractWemFile(wemNode, eventFolder);
        }

        private void ReplaceWemData_Click(object sender, RoutedEventArgs e)
        {

            // Use selected WEM node(s) instead of checked nodes
            var selectedWemNodes = GetSelectedWemNodesFromSelection();

            if (selectedWemNodes.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one WEM file node to replace.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);

                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "WEM audio files (*.wem)|*.wem|All files (*.*)|*.*";
            dialog.Multiselect = true;
            var dialogResult = dialog.ShowDialog();

            if (dialogResult != true)
            {

                return;
            }

            var wemFiles = dialog.FileNames;

            if (wemFiles.Length == 0)
            {

                return;
            }

            try
            {
                var allWemNodes = GetAllNodes().Where(n => n.NodeType == NodeType.WemFile).ToList();
                if (wemFiles.Length == 1)
                {
                    // Single file: use for all selected nodes (and all nodes with the same WEM ID)
                    var wemBytes = File.ReadAllBytes(wemFiles[0]);

                    foreach (var selectedNode in selectedWemNodes)
                    {
                        // Find all nodes with the same WEM ID
                        foreach (var node in allWemNodes.Where(n => n.Name == selectedNode.Name))
                        {
#pragma warning disable CS8602 // Dereference of a possibly null reference

#pragma warning restore CS8602
                            var newData = new byte[wemBytes.Length];
                            Array.Copy(wemBytes, newData, wemBytes.Length);
                            node.WemData.Data = newData;
                            node.WemData.Length = (uint)wemBytes.Length;
                            node.ReplacementFileName = System.IO.Path.GetFileName(wemFiles[0]);



                            RefreshTreeNodeUI(node);
                        }
                    }
                    System.Windows.MessageBox.Show($"Replaced {selectedWemNodes.Count} WEM(s) (and all with same ID) with {System.IO.Path.GetFileName(wemFiles[0])}.", "WEM Replacement", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Multiple files: match by filename (WEM ID)
                    int replaced = 0, notMatched = 0;
                    var wemFileMap = wemFiles.ToDictionary(f => System.IO.Path.GetFileNameWithoutExtension(f), f => f);

                    foreach (var selectedNode in selectedWemNodes)
                    {
                        if (wemFileMap.TryGetValue(selectedNode.Name, out var filePath))
                        {
                            var wemBytes = File.ReadAllBytes(filePath);
                            foreach (var node in allWemNodes.Where(n => n.Name == selectedNode.Name))
                            {
#pragma warning disable CS8602 // Dereference of a possibly null reference

#pragma warning restore CS8602
                                var newData = new byte[wemBytes.Length];
                                Array.Copy(wemBytes, newData, wemBytes.Length);
                                node.WemData.Data = newData;
                                node.WemData.Length = (uint)wemBytes.Length;
                                node.ReplacementFileName = System.IO.Path.GetFileName(filePath);

#pragma warning disable CS8602 // Dereference of a possibly null reference

#pragma warning restore CS8602
                                RefreshTreeNodeUI(node);
                            }
                            replaced++;
                        }
                        else
                        {
                            notMatched++;
#pragma warning disable CS8602 // Dereference of a possibly null reference

#pragma warning restore CS8602
                        }
                    }
                    System.Windows.MessageBox.Show($"Replaced {replaced} WEM(s) (and all with same ID). {notMatched} did not match any selected file.", "WEM Replacement", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // After replacement, clear cache for replaced WEMs
                foreach (var selectedNode in selectedWemNodes)
                {
                    foreach (var node in allWemNodes.Where(n => n.Name == selectedNode.Name))
                    {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                        uint wemId = node.WemData.Id;
#pragma warning restore CS8602
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
                        if (_wemWavCache.TryGetValue(wemId, out string cachedWav))
#pragma warning restore CS8600
                        {
                            if (System.IO.File.Exists(cachedWav))
                            {
                                try { System.IO.File.Delete(cachedWav); } catch { }
                            }
                            _wemWavCache.Remove(wemId);

                        }
                    }
                }

                // After all replacements, force full TreeView UI refresh
                // No longer needed: TreeView.ItemsSource = null; TreeView.ItemsSource = currentItems;
                
                // Verify data integrity after replacement

                VerifyWemDataIntegrity();
            }
            catch (Exception ex)
            {

                System.Windows.MessageBox.Show($"Error during WEM replacement: {ex.Message}", "WEM Replacement Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Add this helper method to force a TreeView node UI refresh
        private void RefreshTreeNodeUI(TreeNode node)
        {
            // Remove and re-add the node in its parent's Children collection to force UI update
            if (node.Parent != null)
            {
                var parent = node.Parent;
                int idx = parent.Children.IndexOf(node);
                if (idx >= 0)
                {
                    parent.Children.RemoveAt(idx);
                    parent.Children.Insert(idx, node);
                }
            }
        }

        // Add this helper to get selected WEM nodes from TreeView selection
        private List<TreeNode> GetSelectedWemNodesFromSelection()
        {
            var result = new List<TreeNode>();
            if (TreeView.SelectedItem is TreeNode node && node.NodeType == NodeType.WemFile)
            {
                result.Add(node);
            }
            return result;
        }

        // Debug method to verify WEM data integrity
        private void VerifyWemDataIntegrity()
        {
            try
            {

                if (_rootNodes.Count == 0 || _rootNodes[0]?.Children.Count == 0)
                {

                    return;
                }

                int totalWemNodes = 0;
                int nodesWithData = 0;
                int nodesWithReplacements = 0;
                var replacementDetails = new List<string>();

                foreach (var eventNode in _rootNodes[0].Children)
                {
                    foreach (var wemNode in eventNode.Children)
                    {
                        if (wemNode.NodeType == NodeType.WemFile && wemNode.WemData != null)
                        {
                            totalWemNodes++;
                            
                            bool hasReplacement = !string.IsNullOrEmpty(wemNode.ReplacementFileName);
                            if (hasReplacement)
                            {
                                nodesWithReplacements++;
                                replacementDetails.Add($"Node {wemNode.Name}: Replacement={wemNode.ReplacementFileName}, DataLength={wemNode.WemData.Data.Length}, Length={wemNode.WemData.Length}");
                            }
                            
                            // Verify data consistency
                            if (wemNode.WemData.Data.Length != wemNode.WemData.Length)
                            {

                            }
                        }
                    }
                }


                
                if (replacementDetails.Count > 0)
                {

                    foreach (var detail in replacementDetails)
                    {

                    }
                }
            }
            catch (Exception ex)
            {

            }
        }

        // Check if the root node is selected
        private bool IsRootNodeSelected()
        {
            if (TreeView.SelectedItem is TreeNode selectedNode)
            {
                return selectedNode.NodeType == NodeType.Root;
            }
            return false;
        }

        private void SaveAsBnkWpk_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                
                // Check if root node is selected
                if (!IsRootNodeSelected())
                {
                    System.Windows.MessageBox.Show("Please select the root file item (WPK file) to save.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Information);

                    return;
                }
                
                // Verify data integrity before saving
                VerifyWemDataIntegrity();
                
#pragma warning disable CS8602 // Dereference of a possibly null reference
                if (_rootNodes.Count == 0 || _rootNodes[0].Children.Count == 0)
#pragma warning restore CS8602
                {
                    System.Windows.MessageBox.Show("No data to save. Please parse files first.", "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Information);

                    return;
                }
                
                // Enhanced logging for debugging

#pragma warning disable CS8602 // Dereference of a possibly null reference

#pragma warning restore CS8602
                
                // Start with ALL WEM files from the original WPK to preserve unreferenced files
                List<AudioData> wemDataList;

                // Declare these at method scope so they're accessible for slot-preserving write
                var wemNodes = new List<TreeNode>();
                var deletedWemIds = new HashSet<uint>();
                var newWemFiles = new List<AudioData>();

                if (_currentWpkWemFiles != null && _currentWpkWemFiles.Count > 0)
                {


                    // Build a dictionary of WEM ID -> AudioData from original
                    var wemDataById = new Dictionary<uint, AudioData>();
                    foreach (var wem in _currentWpkWemFiles)
                    {
                        wemDataById[wem.Id] = wem;
                    }

                    // Collect WEM nodes from tree to find: deletions, replacements, and additions
                    int totalWemNodesFound = 0;
                    int replacedWemNodes = 0;
                    int removedWemNodes = 0;

                    void CollectWemNodes(TreeNode node)
                    {
                        foreach (var child in node.Children)
                        {
                            totalWemNodesFound++;

                            if (child.NodeType == NodeType.WemFile)
                            {
                                // Check if marked for deletion
                                if (child.IsMarkedForDeletion && child.WemData != null)
                                {
                                    deletedWemIds.Add(child.WemData.Id);
                                    removedWemNodes++;

                                }
                                else if (child.WemData != null)
                                {
                                    wemNodes.Add(child);

                                    // Check for replacements
                                    if (!string.IsNullOrEmpty(child.ReplacementFileName))
                                    {
                                        replacedWemNodes++;
                                        // Update the dictionary with replaced data
                                        wemDataById[child.WemData.Id] = child.WemData;

                                    }
                                }
                            }
                            else if (child.NodeType == NodeType.Container)
                            {
                                CollectWemNodes(child); // Recursively collect from container
                            }
                        }
                    }

                    // Collect modifications from tree
                    foreach (var eventNode in _rootNodes[0].Children)
                    {
                        CollectWemNodes(eventNode);
                    }

                    // Collect new WEM files that weren't in the original WPK
                    foreach (var wemNode in wemNodes)
                    {
                        if (wemNode.WemData != null && !_currentWemInformation!.WemIdToSlotIndex.ContainsKey(wemNode.WemData.Id))
                        {
                            // This is a NEW file (not in original WPK)
                            newWemFiles.Add(wemNode.WemData);
                            wemDataById[wemNode.WemData.Id] = wemNode.WemData;

                        }
                    }

                    // DON'T remove deleted WEMs from wemDataById - the slot-preserving method handles them
                    // Just log what will be deleted
                    foreach (var deletedId in deletedWemIds)
                    {

                    }

                    // Convert to list for legacy compatibility (but we'll use slot-preserving method)
                    wemDataList = wemDataById.Values.Where(w => !deletedWemIds.Contains(w.Id)).ToList();




                }
                else
                {
                    // Fallback to old behavior if _currentWpkWemFiles is not available

                    System.Windows.MessageBox.Show("No original WPK data available. Cannot save WPK file.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Log details about the data being saved
                for (int i = 0; i < Math.Min(10, wemDataList.Count); i++)
                {
                    var wemData = wemDataList[i];

                }

                if (wemDataList.Count > 10)
                {

                }
                
                // Prompt for file name
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.Filter = "WPK files (*.wpk)|*.wpk|All files (*.*)|*.*";
                dialog.Title = "Save as WPK";
                if (dialog.ShowDialog() != true)
                {

                    return;
                }
                string outputPath = dialog.FileName;

                
                // Call WriteWpkFile with slot preservation to maintain game compatibility
                try
                {
                    if (_currentWemInformation != null && _currentWemInformation.OriginalSlotCount > 0)
                    {
                        // Rebuild required data structures for the slot-preserving method
                        var wemDataByIdForWrite = new Dictionary<uint, AudioData>();
                        foreach (var wem in _currentWpkWemFiles!)
                        {
                            wemDataByIdForWrite[wem.Id] = wem;
                        }
                        // Apply replacements
                        foreach (var wemNode in wemNodes)
                        {
                            if (wemNode.WemData != null && !string.IsNullOrEmpty(wemNode.ReplacementFileName))
                            {
                                wemDataByIdForWrite[wemNode.WemData.Id] = wemNode.WemData;
                            }
                        }


                        Utility.WriteWpkFilePreservingSlots(
                            wemDataByIdForWrite,
                            _currentWemInformation.OriginalSlotCount,
                            _currentWemInformation.WemIdToSlotIndex,
                            deletedWemIds,
                            newWemFiles,
                            outputPath,
                            _currentWemInformation.OriginalOffsets,
                            _currentWemInformation.SlotIndexToEntry);
                    }
                    else
                    {
                        // Fallback to legacy method if no slot info available

                        Utility.WriteWpkFile(wemDataList, outputPath);
                    }

                }
                catch (Exception writeEx)
                {

                    throw;
                }

                // For WPK save, log size and optionally re-parse WPK for sanity
                try
                {
                    var info = new FileInfo(outputPath);


                    var parsed = WpkExtractor.ParseWpkFile(outputPath, new List<StringHash>(), null);
                    if (parsed == null)
                    {


                    }
                    else
                    {

                        if (_currentWemInformation != null)
                        {
                            int originalCount = _currentWemInformation.OriginalOffsets.Count;
                            int currentCount = parsed.OriginalOffsets.Count;
                            int compareCount = Math.Min(originalCount, currentCount);
                            int mismatches = 0;
                            int zerosInCurrent = 0;
                            for (int i = 0; i < compareCount; i++)
                            {
                                var orig = _currentWemInformation.OriginalOffsets[i];
                                var curr = parsed.OriginalOffsets[i];
                                if (orig != 0 && curr == 0) zerosInCurrent++;
                                if (orig != 0 && curr != 0 && orig != curr) mismatches++;
                            }

                            if (currentCount > originalCount)
                            {
                                var appendedOffset = parsed.OriginalOffsets[originalCount];

                            }
                        }
                    }
                }
                catch (Exception validateEx)
                {



                }

                // Verify the saved file
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);

                }
                else
                {

                }
                
                System.Windows.MessageBox.Show($"Saved {wemDataList.Count} WEM(s) to:\n{outputPath}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);



            }
            catch (Exception ex)
            {


                System.Windows.MessageBox.Show($"Error saving WPK file:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Add this helper method to expand all nodes recursively
        private void ExpandAllNodes(TreeNode node)
        {
            node.IsExpanded = true;
            foreach (var child in node.Children)
            {
                ExpandAllNodes(child);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void AddWemFile_Click(object sender, RoutedEventArgs e)
        {

            try
            {


                
                // Check if an event or container is selected
                var selectedNode = TreeView.SelectedItem as TreeNode;
                if (selectedNode == null || (selectedNode.NodeType != NodeType.Event && selectedNode.NodeType != NodeType.Container))
                {
                    System.Windows.MessageBox.Show("Please select an event or container to add WEM files to.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Information);


                    return;
                }

                // If a container is selected, we need to find its parent event
                TreeNode eventNode = selectedNode.NodeType == NodeType.Event ? selectedNode : selectedNode.Parent;
                if (eventNode == null || eventNode.NodeType != NodeType.Event)
                {
                    System.Windows.MessageBox.Show("Could not find parent event for selected container.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Error);


                    return;
                }
                
                // Check if we have BNK structure in memory
                if (_currentBnkSections == null)
                {
                    System.Windows.MessageBox.Show("No BNK structure loaded. Please parse files first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);


                    return;
                }
                

                
                // Open file dialog to select WEM file
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "WEM files (*.wem)|*.wem|All files (*.*)|*.*";
                dialog.Title = "Select WEM file to add";
                if (dialog.ShowDialog() != true)
                {


                    return;
                }
                
                string wemFilePath = dialog.FileName;

                
                // Read the WEM file
                byte[] wemData = File.ReadAllBytes(wemFilePath);

                
                // Derive WEM ID from filename to keep BNK/WPK consistent with tools
                uint newWemId = 0;
                var fileName = Path.GetFileName(wemFilePath);
                var baseName = fileName.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - 4)
                    : fileName;
                if (!uint.TryParse(baseName, out newWemId))
                {
                    newWemId = GetNextAvailableWemId();

                }
                else
                {

                }
                
                // Create AudioData for the new WEM
                var newWemAudioData = new AudioData
                {
                    Id = newWemId,
                    Length = (uint)wemData.Length,
                    Data = wemData
                };
                
                // Do NOT write to the original WPK path here.
                // Only update in-memory mapping; disk writes happen during Save As.
                if (_currentWemInformation != null)
                {
                    int newSlotIndex = _currentWemInformation.OriginalSlotCount;
                    _currentWemInformation.WemIdToSlotIndex[newWemAudioData.Id] = newSlotIndex;
                    _currentWemInformation.OriginalSlotCount++;
                    if (_currentWpkWemFiles != null)
                    {
                        _currentWpkWemFiles.Add(newWemAudioData);

                    }
                }
                
                uint? preferredContainerId = null;
                if (selectedNode.NodeType == NodeType.Container)
                {
                    if (uint.TryParse(selectedNode.Name, out var parsedId))
                    {
                        preferredContainerId = parsedId;
                    }
                }


                AddWemToBnkStructure_FullFidelity(eventNode.Name, newWemId, preferredContainerId);
                
                // Update the tree view to show the new WEM
                var newWemNode = new TreeNode
                {
                    Name = $"{newWemId}",
                    WemData = newWemAudioData,
                    NodeType = NodeType.WemFile,
                    ShowCheckbox = true
                };

                // Add to the selected node (either container or event)
                newWemNode.Parent = selectedNode;
                selectedNode.Children.Add(newWemNode);

                // Update the in-memory WPK WEM files list
                if (_currentWpkWemFiles != null)
                {
                    _currentWpkWemFiles.Add(newWemAudioData);
                }

                string targetDescription = selectedNode.NodeType == NodeType.Container ? $"container {selectedNode.Name} in event {eventNode.Name}" : $"event {eventNode.Name}";



                System.Windows.MessageBox.Show($"Successfully added WEM file (ID: {newWemId}) to {targetDescription}.\n\nRemember to save the BNK file to persist the changes.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {



                System.Windows.MessageBox.Show($"Error adding WEM file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveWemButton_Click(object sender, RoutedEventArgs e)
        {

            try
            {


                // Get selected items (checkboxes)
                var selectedItems = GetSelectedItems();
                if (selectedItems.Count == 0)
                {
                    System.Windows.MessageBox.Show("Please select at least one WEM file to remove using the checkboxes.\n\nTip: Check the boxes next to the WEM files (or entire events) you want to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);


                    return;
                }

                // Collect WEM file nodes - either directly selected WEM files, or WEM files from selected events/containers
                var wemNodesToRemove = new List<TreeNode>();

                // Helper to recursively collect WEM nodes
                void CollectWemNodesRecursive(TreeNode node)
                {
                    foreach (var child in node.Children)
                    {
                        if (child.NodeType == NodeType.WemFile)
                        {
                            wemNodesToRemove.Add(child);
                        }
                        else if (child.NodeType == NodeType.Container)
                        {
                            CollectWemNodesRecursive(child);
                        }
                    }
                }

                foreach (var item in selectedItems)
                {
                    if (item.NodeType == NodeType.WemFile)
                    {
                        wemNodesToRemove.Add(item);
                    }
                    else if (item.NodeType == NodeType.Event)
                    {
                        // Add all WEM files from this event (including those in containers)
                        CollectWemNodesRecursive(item);
                    }
                    else if (item.NodeType == NodeType.Container)
                    {
                        // Add all WEM files from this container
                        CollectWemNodesRecursive(item);
                    }
                }

                if (wemNodesToRemove.Count == 0)
                {
                    System.Windows.MessageBox.Show("No WEM files found in selection.\n\nPlease check the boxes next to WEM files or events containing WEM files.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Information);


                    return;
                }



                // Confirm with user
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove {wemNodesToRemove.Count} WEM file(s)?\n\n" +
                    "The files will be marked for deletion and excluded when you save the WPK and BNK files.\n\n" +
                    "This action can be undone by re-parsing the original files.",
                    "Confirm Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {


                    return;
                }

                // Mark WEM files for deletion
                int markedCount = 0;
                foreach (var wemNode in wemNodesToRemove)
                {
                    wemNode.IsMarkedForDeletion = true;
                    wemNode.IsChecked = false; // Uncheck the checkbox

                    markedCount++;
                }




                System.Windows.MessageBox.Show(
                    $"Successfully marked {markedCount} WEM file(s) for deletion.\n\n" +
                    "The WEM files will be excluded when you save the WPK and BNK files.\n\n" +
                    "Remember to save both files to persist the changes.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {


                System.Windows.MessageBox.Show($"Error removing WEM file(s):\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveBnkFile_Click(object sender, RoutedEventArgs e)
        {

            try
            {


                
                // Check if we have BNK structure in memory
                if (_currentBnkSections == null)
                {
                    System.Windows.MessageBox.Show("No BNK structure loaded. Please parse files first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);


                    return;
                }
                

                
                if (!ValidateEventContainerRouting())
                {
                    System.Windows.MessageBox.Show("Validation failed: Event → Container routing changed unexpectedly.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    return;
                }

                if (!ValidateWemIdsAgainstWpk())
                {
                    System.Windows.MessageBox.Show("Validation failed: BNK references WEM IDs not present in WPK.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    return;
                }

                // Prompt for save location
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.Filter = "BNK files (*.bnk)|*.bnk|All files (*.*)|*.*";
                dialog.Title = "Save BNK file";
                if (dialog.ShowDialog() != true)
                {


                    return;
                }
                
                string outputPath = dialog.FileName;

                
                // Validate _fullFidelityBnk
                if (_fullFidelityBnk == null || _fullFidelityBnk.AllObjectsInOrder == null || _fullFidelityBnk.AllObjectsInOrder.Count == 0)
                {

                    System.Windows.MessageBox.Show("Full-fidelity BNK data not loaded. Please parse files first.", "Missing Data", MessageBoxButton.OK, MessageBoxImage.Error);

                    return;
                }


                // Collect IDs of WEMs marked for deletion
                var deletedWemIds = new HashSet<uint>();
                if (_rootNodes.Count > 0)
                {
                    foreach (var eventNode in _rootNodes[0].Children)
                    {
                        foreach (var wemNode in eventNode.Children)
                        {
                            if (wemNode.NodeType == NodeType.WemFile && wemNode.IsMarkedForDeletion && wemNode.WemData != null)
                            {
                                deletedWemIds.Add(wemNode.WemData.Id);

                            }
                        }
                    }
                }


                // Filter out objects related to deleted WEMs from full-fidelity structure
                if (deletedWemIds.Count > 0)
                {
                    var filteredBnk = FilterDeletedWemsFromFullFidelity(_fullFidelityBnk, deletedWemIds);


                    // Log modified BNK structure before saving for debugging

                    LogBnkStructureToDebug(filteredBnk, "MODIFIED");

                    // Write the filtered BNK file from memory using full-fidelity serialization
                    bool writeSuccess = BnkWriter.WriteBnkFile_FullFidelity(filteredBnk, outputPath);
                    if (!writeSuccess)
                    {

                        System.Windows.MessageBox.Show("Failed to write BNK file. Check the log for details.", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);

                        return;
                    }
                }
                else
                {
                    // No deletions, write as-is
                    // Log modified BNK structure before saving for debugging

                    LogBnkStructureToDebug(_fullFidelityBnk, "MODIFIED");

                    bool writeSuccess = BnkWriter.WriteBnkFile_FullFidelity(_fullFidelityBnk, outputPath);
                    if (!writeSuccess)
                    {

                        System.Windows.MessageBox.Show("Failed to write BNK file. Check the log for details.", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);

                        return;
                    }
                }

                // Verify the saved file
                if (File.Exists(outputPath))
                {
                var fileInfo = new FileInfo(outputPath);


                    
                    if (fileInfo.Length == 0)
                    {

                        System.Windows.MessageBox.Show("Saved file is 0 bytes. This indicates a serious problem with the save process.", "File Size Error", MessageBoxButton.OK, MessageBoxImage.Error);

                        return;
                    }
                }
                else
                {

                    System.Windows.MessageBox.Show("Saved file does not exist after write operation.", "File Creation Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    return;
                }
                

                
                // Validate the written file

                bool validationSuccess = BnkWriter.ValidateWrittenBnkFile(outputPath);
                if (!validationSuccess)
                {

                    System.Windows.MessageBox.Show("BNK file was saved but validation failed. The file may not be compatible with tools or the game.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {


                }
                


                System.Windows.MessageBox.Show($"Successfully saved BNK file to:\n{outputPath}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {





                System.Windows.MessageBox.Show($"Error saving BNK file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateEventContainerRouting()
        {
            if (_currentBnkSections == null || _fullFidelityBnk == null)
                return true;

            var originalContainerIds = new HashSet<uint>(_currentBnkSections.RandomContainers.Select(rc => rc.SelfId));
            foreach (var evt in _currentBnkSections.Events)
            {
                if (evt.EventIds == null || evt.EventIds.Count == 0)
                    continue;
                uint actionId = evt.EventIds.First();
                var originalEa = _currentBnkSections.EventActions.FirstOrDefault(x => x.SelfId == actionId);
                if (originalEa == null)
                    continue;
                uint originalTarget = originalEa.SoundObjectId;
                if (!originalContainerIds.Contains(originalTarget))
                    continue;

                var currentEaObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.ParsedObject is EventAction ea && ea.SelfId == actionId);
                if (currentEaObj == null)
                    return false;
                var currentEa = currentEaObj.ParsedObject as EventAction;
                if (currentEa.SoundObjectId != originalTarget)
                    return false;
                if (currentEaObj.IsChanged)
                    return false;
            }
            return true;
        }

        private bool ValidateWemIdsAgainstWpk()
        {
            if (_currentWemInformation == null || _fullFidelityBnk == null)
                return true;
            var wemIds = new HashSet<uint>(_currentWemInformation.WemIdToSlotIndex.Keys);
            foreach (var obj in _fullFidelityBnk.AllObjectsInOrder)
            {
                if (obj.ParsedObject is Sound s)
                {
                    uint candidateId = s.StreamFileID != 0 ? s.StreamFileID : s.FileId;
                    if (!wemIds.Contains(candidateId))
                        return false;
                }
            }
            return true;
        }

        private uint GetNextAvailableWemId()
        {
            if (_currentWpkWemFiles == null || _currentWpkWemFiles.Count == 0)
            {
                return 1; // Start with ID 1 if no existing files
            }

            // Find the highest existing ID and add 1
            uint maxId = _currentWpkWemFiles.Max(w => w.Id);
            return maxId + 1;
        }

        private BnkFullFidelity FilterDeletedWemsFromFullFidelity(BnkFullFidelity originalBnk, HashSet<uint> deletedWemIds)
        {


            var filteredBnk = new BnkFullFidelity
            {
                BnkVersion = originalBnk.BnkVersion,
                AllObjectsInOrder = new List<BnkObjectRecord>()
            };

            // Track which Sound, RandomContainer, and EventAction objects reference deleted WEMs
            var deletedSoundIds = new HashSet<uint>();
            var deletedRandomContainerIds = new HashSet<uint>();
            var deletedEventActionIds = new HashSet<uint>();

            // First pass: identify Sound objects that reference deleted WEMs
            foreach (var obj in originalBnk.AllObjectsInOrder)
            {
                if (obj.Type == 2) // Sound object
                {
                    // Use the ObjectId if available, otherwise try to parse
                    if (obj.ParsedObject is Sound soundObj)
                    {
                        if (deletedWemIds.Contains(soundObj.FileId))
                        {
                            deletedSoundIds.Add(soundObj.SelfId);

                        }
                    }
                    else if (obj.ObjectId.HasValue)
                    {
                        // Fallback: try to extract FileId from raw data
                        try
                        {
                            using (var ms = new MemoryStream(obj.RawData))
                            using (var reader = new BinaryReader(ms))
                            {
                                reader.ReadByte(); // Type
                                reader.ReadUInt32(); // Length
                                uint selfId = reader.ReadUInt32(); // SelfId
                                reader.ReadBytes(4); // Skip some bytes
                                uint fileId = reader.ReadUInt32(); // FileId

                                if (deletedWemIds.Contains(fileId))
                                {
                                    deletedSoundIds.Add(selfId);

                                }
                            }
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                }
            }

            // Second pass: identify RandomContainer objects that reference deleted Sounds and update them
            foreach (var obj in originalBnk.AllObjectsInOrder)
            {
                if (obj.Type == 5 && obj.ParsedObject is RandomContainer randomContainer) // RandomContainer type is 5
                {
                    var originalSoundCount = randomContainer.SoundIds.Count;
                    randomContainer.SoundIds.RemoveAll(id => deletedSoundIds.Contains(id));
                    randomContainer.Playlist.RemoveAll(entry => deletedSoundIds.Contains(entry.PlayID));

                    if (randomContainer.SoundIds.Count != originalSoundCount)
                    {


                        // If RandomContainer is now empty, mark it for deletion
                        if (randomContainer.SoundIds.Count == 0)
                        {
                            deletedRandomContainerIds.Add(randomContainer.SelfId);

                        }
                        else
                        {
                            // Update the RandomContainer
                            randomContainer.SoundIdAmount = (uint)randomContainer.Playlist.Count;

                            // Mark the RandomContainer as changed - it will be serialized by BnkWriter.SerializeObject()
                            obj.IsChanged = true;


                        }
                    }
                }
            }

            // Third pass: identify EventAction objects that reference deleted Sounds or RandomContainers
            foreach (var obj in originalBnk.AllObjectsInOrder)
            {
                if (obj.Type == 3) // EventAction object
                {
                    // Use the ParsedObject if available
                    if (obj.ParsedObject is EventAction eventActionObj)
                    {
                        // Check if EventAction references deleted Sound or RandomContainer
                        if (deletedSoundIds.Contains(eventActionObj.SoundObjectId) ||
                            deletedRandomContainerIds.Contains(eventActionObj.SoundObjectId))
                        {
                            deletedEventActionIds.Add(eventActionObj.SelfId);

                        }
                    }
                    else if (obj.ObjectId.HasValue)
                    {
                        // Fallback: try to extract from raw data
                        try
                        {
                            using (var ms = new MemoryStream(obj.RawData))
                            using (var reader = new BinaryReader(ms))
                            {
                                reader.ReadByte(); // Type
                                reader.ReadUInt32(); // Length
                                uint selfId = reader.ReadUInt32(); // SelfId
                                reader.ReadUInt16(); // Scope
                                reader.ReadByte(); // ActionType
                                uint targetId = reader.ReadUInt32(); // TargetId

                                if (deletedSoundIds.Contains(targetId))
                                {
                                    deletedEventActionIds.Add(selfId);

                                }
                            }
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                }
            }

            // Third pass: copy objects and update Events to remove deleted EventAction references
            int removedCount = 0;
            foreach (var obj in originalBnk.AllObjectsInOrder)
            {
                bool shouldRemove = false;

                // Check if this object should be removed based on its ObjectId
                if (obj.ObjectId.HasValue)
                {
                    if (obj.Type == 2) // Sound
                    {
                        shouldRemove = deletedSoundIds.Contains(obj.ObjectId.Value);
                    }
                    else if (obj.Type == 3) // EventAction
                    {
                        shouldRemove = deletedEventActionIds.Contains(obj.ObjectId.Value);
                    }
                    else if (obj.Type == 5) // RandomContainer
                    {
                        shouldRemove = deletedRandomContainerIds.Contains(obj.ObjectId.Value);
                    }
                }

                if (!shouldRemove)
                {
                    // Special handling for Event objects - remove references to deleted EventActions
                    if (obj.Type == 4 && obj.ParsedObject is Event eventObj)
                    {
                        var originalCount = eventObj.EventIds.Count;
                        eventObj.EventIds.RemoveAll(id => deletedEventActionIds.Contains(id));

                        if (eventObj.EventIds.Count != originalCount)
                        {
                            // Event was modified - update it
                            eventObj.EventAmount = (byte)eventObj.EventIds.Count;
                            obj.IsChanged = true;

                            // Rebuild the RawData
                            using (var ms = new MemoryStream())
                            using (var writer = new BinaryWriter(ms))
                            {
                                writer.Write((byte)4); // Event type
                                uint eventLength = (uint)(4 + 1 + eventObj.EventIds.Count * 4);
                                writer.Write(eventLength);
                                writer.Write(eventObj.SelfId);
                                writer.Write(eventObj.EventAmount);
                                foreach (var eventId in eventObj.EventIds)
                                {
                                    writer.Write(eventId);
                                }
                                obj.RawData = ms.ToArray();
                                obj.Length = eventLength;
                            }


                        }
                    }

                    filteredBnk.AllObjectsInOrder.Add(obj);
                }
                else
                {
                    removedCount++;
                }
            }


            return filteredBnk;
        }

        private void AddWemToBnkStructure_FullFidelity(string eventName, uint wemId, uint? preferredContainerId = null)
        {
            // Only new/modified objects are marked as IsChanged = true.
            // All other objects retain their original RawData and IsChanged = false.
            // This ensures the output BNK is byte-for-byte identical to the original except for the new WEM and its references.
            WemAdditionLogger.LogSection("AddWemToBnkStructure_FullFidelity START");
            WemAdditionLogger.Log($"Adding WEM {wemId} to event '{eventName}', preferredContainerId={preferredContainerId}");
            try
            {

                if (_fullFidelityBnk == null || _fullFidelityBnk.AllObjectsInOrder == null)
                {
                    WemAdditionLogger.Log("ERROR: No full-fidelity BNK structure available");
                    return;
                }

                // Find the event by name in the full-fidelity structure
                uint targetEventId = 0;
                foreach (var kvp in _eventNameLookup)
                {
                    if (kvp.Value == eventName)
                    {
                        targetEventId = kvp.Key;
                        break;
                    }
                }
                if (targetEventId == 0)
                {
                    return;
                }

                // Find the event object in the full-fidelity structure
                BnkObjectRecord targetEvent = null;
                foreach (var obj in _fullFidelityBnk.AllObjectsInOrder)
                {
                    if (obj.ParsedObject is Event evt && evt.SelfId == targetEventId)
                    {
                        targetEvent = obj;
                        break;
                    }
                }
                if (targetEvent == null)
                {
                    return;
                }
                var eventObj = targetEvent.ParsedObject as Event;
                if (eventObj == null)
                {
                    return;
                }

                var eventActionId = 0u;
                if (preferredContainerId.HasValue)
                {
                    foreach (var eaId in eventObj.EventIds)
                    {
                        var eaMatch = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.ParsedObject is EventAction ea && ea.SelfId == eaId);
                        if (eaMatch != null)
                        {
                            var eaParsed = eaMatch.ParsedObject as EventAction;
                            if (eaParsed.SoundObjectId == preferredContainerId.Value)
                            {
                                eventActionId = eaId;
                                break;
                            }
                        }
                    }
                }
                if (eventActionId == 0)
                {
                    eventActionId = eventObj.EventIds.FirstOrDefault();
                }
                if (eventActionId == 0)
                {
                    return;
                }

                // Find the EventAction to see what it points to
                var eventActionObj = _fullFidelityBnk.AllObjectsInOrder
                    .FirstOrDefault(o => o.Type == 3 && o.ParsedObject is EventAction ea && ea.SelfId == eventActionId);

                if (eventActionObj == null)
                {
                    return;
                }

                var eventAction = eventActionObj.ParsedObject as EventAction;
                uint targetObjectId = eventAction.SoundObjectId;
                if (preferredContainerId.HasValue && preferredContainerId.Value != targetObjectId)
                {
                    targetObjectId = preferredContainerId.Value;
                }

                // Check if the target is a RandomContainer
                var randomContainerObj = _fullFidelityBnk.AllObjectsInOrder
                    .FirstOrDefault(o => o.Type == 5 && o.ParsedObject is RandomContainer rc && rc.SelfId == targetObjectId);

                if (randomContainerObj != null)
                {
                    // This event uses a RandomContainer - add the new Sound to it

                    uint newSoundId = GenerateNewObjectId();

                    // Clone a template Sound
                    var templateSoundObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 2 && o.RawData != null && o.ParsedObject is Sound);
                    if (templateSoundObj == null)
                    {
                        return;
                    }

                    var templateSound = templateSoundObj.ParsedObject as Sound;
                    if (templateSound == null)
                    {
                        return;
                    }


                    // Clone the template RawData and patch specific byte offsets
                    byte[] soundRaw = (byte[])templateSoundObj.RawData.Clone();

                    // DEBUG: Log the original template RawData in hex

                    // DEBUG: Parse and log what each field currently contains
                    if (soundRaw.Length >= 21)
                    {
                        byte objType = soundRaw[0];
                        uint objLength = BitConverter.ToUInt32(soundRaw, 1);
                        uint selfIdInRaw = BitConverter.ToUInt32(soundRaw, 5);
                        uint field9 = BitConverter.ToUInt32(soundRaw, 9);
                        uint field13 = BitConverter.ToUInt32(soundRaw, 13);
                        uint field17 = BitConverter.ToUInt32(soundRaw, 17);
                        if (soundRaw.Length >= 25)
                        {
                            uint field21 = BitConverter.ToUInt32(soundRaw, 21);
                        }
                        if (soundRaw.Length >= 29)
                        {
                            uint field25 = BitConverter.ToUInt32(soundRaw, 25);
                        }
                        if (soundRaw.Length >= 33)
                        {
                            uint field29 = BitConverter.ToUInt32(soundRaw, 29);
                        }
                    }

                    // Patch SelfId at offset 5
                    Array.Copy(BitConverter.GetBytes(newSoundId), 0, soundRaw, 5, 4);

                    // Sound RawData structure for BNK version > 0x70:
                    // Offset 0: Type (1 byte)
                    // Offset 1-4: Length (4 bytes)
                    // Offset 5-8: SelfId (4 bytes)
                    // Offset 9-12: Unknown1 (4 bytes)
                    // Offset 13: IsStreamed (1 byte)
                    // Offset 14-17: FileId (4 bytes) <- C# software reads THIS as WEM reference!
                    // Offset 18+: Extended fields (reserved0, streamSourceType, streamFileID, etc.)

                    // Patch FileId at offset 14 - THIS IS WHAT THE C# SOFTWARE USES!
                    // C# parser reads: SelfId(5-8) + Unknown1(9-12) + IsStreamed(13) + FileId(14-17)
                    if (soundRaw.Length > 18)
                    {
                        uint oldValueAt14 = BitConverter.ToUInt32(soundRaw, 14);
                        Array.Copy(BitConverter.GetBytes(wemId), 0, soundRaw, 14, 4);
                        uint newValueAt14 = BitConverter.ToUInt32(soundRaw, 14);
                    }

                    // NOTE: Do NOT patch offset 17 - it would corrupt sourceID!
                    // sourceID is at offset 14 and is 4 bytes, so patching 17 would overwrite part of it

                    // Patch DirectParentID at offset 32 - this links the Sound to its parent container
                    // Without this, the Sound points to the wrong container and won't play correctly
                    if (soundRaw.Length > 36)
                    {
                        Array.Copy(BitConverter.GetBytes(targetObjectId), 0, soundRaw, 32, 4);
                    }

                    // Create the new Sound object with cloned RawData
                    var newSound = new Sound
                    {
                        SelfId = newSoundId,
                        Unknown1 = templateSound.Unknown1,
                        IsStreamed = templateSound.IsStreamed,
                        Unknown2 = templateSound.Unknown2 != null ? (byte[])templateSound.Unknown2.Clone() : new byte[0],
                        Unknown3 = templateSound.Unknown3 != null ? (byte[])templateSound.Unknown3.Clone() : new byte[0],
                        FileId = wemId,
                        StreamFileID = wemId,
                        Reserved0 = templateSound.Reserved0,
                        StreamSourceType = templateSound.StreamSourceType,
                        StreamSourceID = templateSound.StreamSourceID,
                        StreamFileOffset = templateSound.StreamFileOffset,
                        StreamFileLength = templateSound.StreamFileLength,
                        SoundType = templateSound.SoundType,
                        SoundStructure = templateSound.SoundStructure,
                        RawData = soundRaw
                    };

                    // Add the new Sound object - use RawData (IsChanged = false) since we patched it correctly
                    var newSoundRecord = new BnkObjectRecord
                    {
                        Type = 2,
                        Length = (uint)(soundRaw.Length - 5), // Length doesn't include type byte and length field
                        RawData = soundRaw,
                        ParsedObject = newSound,
                        IsChanged = false,  // Use patched RawData
                        ObjectId = newSoundId
                    };
                    _fullFidelityBnk.AllObjectsInOrder.Add(newSoundRecord);

                    WemAdditionLogger.Log($"Created new Sound object: SelfId={newSoundId}, FileId={wemId}");
                    WemAdditionLogger.LogSoundObject(newSound, "NEW SOUND");
                    WemAdditionLogger.LogBnkObjectRecord(newSoundRecord, "NEW SOUND RECORD");

                    // Update the RandomContainer to include the new Sound (preserve EventAction → Container)
                    var randomContainer = randomContainerObj.ParsedObject as RandomContainer;

                    // Log RandomContainer state before modification
                    WemAdditionLogger.LogRandomContainer(randomContainer, "BEFORE MODIFICATION");

                    // Playlist format is 8 bytes per entry (playID: u32 + weight: u32)
                    // Use weight from first existing entry for consistency, or default to 50000
                    uint defaultWeight = randomContainer.Playlist.Count > 0 ? randomContainer.Playlist[0].Weight : 50000;
                    var newPlaylistEntry = new PlaylistEntry { PlayID = newSoundId, Weight = defaultWeight, RsGroupID = 0 };

                    randomContainer.Playlist.Add(newPlaylistEntry);
                    randomContainer.SoundIds.Add(newSoundId);  // Keep backward compatibility
                    randomContainer.SoundIdAmount = (uint)randomContainer.SoundIds.Count;
                    randomContainer.PlaylistLength = (ushort)randomContainer.Playlist.Count;  // Update PlaylistLength
                    randomContainerObj.IsChanged = true;

                    // Log RandomContainer state after modification
                    WemAdditionLogger.LogRandomContainer(randomContainer, "AFTER MODIFICATION");
                    WemAdditionLogger.Log($"RandomContainer {randomContainer.SelfId} marked as IsChanged=true");

                }
                else
                {
                    // No RandomContainer - use the old direct EventAction → Sound approach

                    uint newSoundId = GenerateNewObjectId();
                    uint newEventActionId = GenerateNewObjectId();

                    var templateSoundObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 2 && o.RawData != null && o.ParsedObject is Sound);
                    var templateEventActionObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.RawData != null && o.ParsedObject is EventAction);
                    if (templateSoundObj == null || templateEventActionObj == null)
                    {
                        return;
                    }

                    var templateSound = templateSoundObj.ParsedObject as Sound;
                    var templateEventAction = templateEventActionObj.ParsedObject as EventAction;
                    if (templateSound == null || templateEventAction == null)
                    {
                        return;
                    }


                    // Clone the template RawData and patch specific byte offsets for Sound
                    byte[] soundRaw = (byte[])templateSoundObj.RawData.Clone();

                    // Sound RawData structure for BNK version > 0x70:
                    // Offset 0: Type (1 byte)
                    // Offset 1-4: Length (4 bytes)
                    // Offset 5-8: SelfId (4 bytes)
                    // Offset 9-12: Unknown1 (4 bytes)
                    // Offset 13: IsStreamed (1 byte)
                    // Offset 14-17: FileId (4 bytes) <- C# software reads THIS as WEM reference!
                    // Offset 18+: Extended fields (reserved0, streamSourceType, streamFileID, etc.)

                    // Patch SelfId at offset 5
                    Array.Copy(BitConverter.GetBytes(newSoundId), 0, soundRaw, 5, 4);

                    // Patch FileId at offset 14 - THIS IS WHAT THE C# SOFTWARE USES!
                    if (soundRaw.Length > 18)
                    {
                        Array.Copy(BitConverter.GetBytes(wemId), 0, soundRaw, 14, 4);
                    }

                    // NOTE: Do NOT patch offset 17 - it would corrupt sourceID!

                    // NOTE: DirectParentID at offset 32 is NOT patched here because this code path
                    // creates standalone Sounds not belonging to a container. For announcer files,
                    // this path should not be reached since all sounds belong to containers.
                    // If this path is used, the Sound may not play correctly in-game.

                    // Clone the template RawData and patch specific byte offsets for EventAction
                    byte[] eventActionRaw = (byte[])templateEventActionObj.RawData.Clone();

                    // EventAction structure: Type (1) + Length (4) + SelfId (4) + Scope (1) + Type (1) + SoundObjectId (4) + ...
                    // Patch SelfId at offset 5
                    Array.Copy(BitConverter.GetBytes(newEventActionId), 0, eventActionRaw, 5, 4);

                    // Patch SoundObjectId at offset 11 (after Type(1) + Length(4) + SelfId(4) + Scope(1) + Type(1))
                    Array.Copy(BitConverter.GetBytes(newSoundId), 0, eventActionRaw, 11, 4);

                    var newSound = new Sound
                    {
                        SelfId = newSoundId,
                        Unknown1 = templateSound.Unknown1,
                        IsStreamed = templateSound.IsStreamed,
                        Unknown2 = templateSound.Unknown2 != null ? (byte[])templateSound.Unknown2.Clone() : new byte[0],
                        Unknown3 = templateSound.Unknown3 != null ? (byte[])templateSound.Unknown3.Clone() : new byte[0],
                        FileId = wemId,
                        StreamFileID = wemId,
                        Reserved0 = templateSound.Reserved0,
                        StreamSourceType = templateSound.StreamSourceType,
                        StreamSourceID = templateSound.StreamSourceID,
                        StreamFileOffset = templateSound.StreamFileOffset,
                        StreamFileLength = templateSound.StreamFileLength,
                        SoundType = templateSound.SoundType,
                        SoundStructure = templateSound.SoundStructure,
                        RawData = soundRaw
                    };

                    var newEventAction = new EventAction
                    {
                        SelfId = newEventActionId,
                        Scope = templateEventAction.Scope,
                        Type = templateEventAction.Type,
                        SoundObjectId = newSoundId,
                        SwitchGroupId = templateEventAction.SwitchGroupId,
                        RawData = eventActionRaw
                    };

                    // Add Sound with patched RawData
                    _fullFidelityBnk.AllObjectsInOrder.Add(new BnkObjectRecord
                    {
                        Type = 2,
                        Length = (uint)(soundRaw.Length - 5),
                        RawData = soundRaw,
                        ParsedObject = newSound,
                        IsChanged = false,  // Use patched RawData
                        ObjectId = newSoundId
                    });

                    // Add EventAction with patched RawData
                    _fullFidelityBnk.AllObjectsInOrder.Add(new BnkObjectRecord
                    {
                        Type = 3,
                        Length = (uint)(eventActionRaw.Length - 5),
                        RawData = eventActionRaw,
                        ParsedObject = newEventAction,
                        IsChanged = false,  // Use patched RawData
                        ObjectId = newEventActionId
                    });


                    // Update the existing event to reference the new EventAction
                    eventObj.EventIds.Add(newEventActionId);
                    eventObj.EventAmount = (byte)eventObj.EventIds.Count;
                    targetEvent.IsChanged = true;

                }

                WemAdditionLogger.LogSection("AddWemToBnkStructure_FullFidelity SUCCESS");
                WemAdditionLogger.Log($"Total objects in BNK: {_fullFidelityBnk.AllObjectsInOrder.Count}");
                CompareObjectStructures();
            }
            catch (Exception ex)
            {
                WemAdditionLogger.Log($"ERROR in AddWemToBnkStructure_FullFidelity: {ex.Message}");
                WemAdditionLogger.Log($"Stack trace: {ex.StackTrace}");
            }
        }



        private uint GenerateNewObjectId()
        {
            // Generate a unique ID that doesn't conflict with existing objects
            // Wwise uses FNV hash-style IDs (large 32-bit numbers), not sequential IDs
            // Generate a random large ID in the Wwise range (avoid low numbers like 1, 2, 3)
            var random = new Random();
            uint newId;

            if (_fullFidelityBnk?.AllObjectsInOrder != null)
            {
                var existingIds = _fullFidelityBnk.AllObjectsInOrder
                    .Where(obj => obj.ObjectId.HasValue)
                    .Select(obj => obj.ObjectId.Value)
                    .ToHashSet();

                // Generate random IDs in the Wwise range (100000000 to 4000000000)
                // Keep trying until we find one that doesn't conflict
                do
                {
                    newId = (uint)random.Next(100000000, int.MaxValue);
                } while (existingIds.Contains(newId));
            }
            else
            {
                newId = (uint)random.Next(100000000, int.MaxValue);
            }

            return newId;
        }

        private uint GetNextAvailableEventActionId()
        {
            if (_currentBnkSections?.EventActions == null || _currentBnkSections.EventActions.Count == 0)
            {
                return 1; // Start with ID 1 if no existing event actions
            }
            
            // Find the highest existing event action ID and add 1
            uint maxId = _currentBnkSections.EventActions.Max(ea => ea.SelfId);
            return maxId + 1;
        }

        private uint GetNextAvailableSoundId()
        {
            if (_currentBnkSections?.Sounds == null || _currentBnkSections.Sounds.Count == 0)
            {
                return 1; // Start with ID 1 if no existing sounds
            }
            
            // Find the highest existing sound ID and add 1
            uint maxId = _currentBnkSections.Sounds.Max(s => s.SelfId);
            return maxId + 1;
        }

        private void LoadBnkFile(string path)
        {
            _originalBnkBytes = File.ReadAllBytes(path);
            _fullFidelityBnk = new BnkFullFidelity();
            _currentBnkSections = new BNKSections();
            bool ok = BnkExtractor.ParseEventBnkFile_FullFidelity(path, _fullFidelityBnk, _currentBnkSections);
            if (!ok)
            {
                System.Windows.MessageBox.Show("Failed to parse BNK file.");
                return;
            }
            // TODO: Update UI from _currentBnkSections as before
        }

        private void SaveBnkFile(string path)
        {
            if (_originalBnkBytes != null)
            {
                bool ok = BnkWriter.WriteBnkFile_CopyRaw(_originalBnkBytes, path);
                if (!ok)
                {
                    System.Windows.MessageBox.Show("Failed to save BNK file.");
                    return;
                }
            }
        }

        // Helper method to compare original and new object structures
        private void CompareObjectStructures()
        {
            try
            {
                
                if (_fullFidelityBnk?.AllObjectsInOrder == null)
                {
                    return;
                }
                
                var originalSounds = _fullFidelityBnk.AllObjectsInOrder
                    .Where(obj => obj.Type == 2 && !obj.IsChanged)
                    .Select(obj => obj.ParsedObject as Sound)
                    .Where(s => s != null)
                    .ToList();
                
                var newSounds = _fullFidelityBnk.AllObjectsInOrder
                    .Where(obj => obj.Type == 2 && obj.IsChanged)
                    .Select(obj => obj.ParsedObject as Sound)
                    .Where(s => s != null)
                    .ToList();
                
                var originalEventActions = _fullFidelityBnk.AllObjectsInOrder
                    .Where(obj => obj.Type == 3 && !obj.IsChanged)
                    .Select(obj => obj.ParsedObject as EventAction)
                    .Where(ea => ea != null)
                    .ToList();
                
                var newEventActions = _fullFidelityBnk.AllObjectsInOrder
                    .Where(obj => obj.Type == 3 && obj.IsChanged)
                    .Select(obj => obj.ParsedObject as EventAction)
                    .Where(ea => ea != null)
                    .ToList();
                
                
                if (originalSounds.Count > 0 && newSounds.Count > 0)
                {
                    var templateSound = originalSounds.First();
                    var newSound = newSounds.First();
                    
                }
                
                if (originalEventActions.Count > 0 && newEventActions.Count > 0)
                {
                    var templateEventAction = originalEventActions.First();
                    var newEventAction = newEventActions.First();
                    
                }
                
            }
            catch (Exception ex)
            {
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if we have a BNK loaded
                if (_fullFidelityBnk?.AllObjectsInOrder == null || _fullFidelityBnk.AllObjectsInOrder.Count == 0)
                {
                    System.Windows.MessageBox.Show("No BNK file loaded. Please load a BNK file first.", "Export Error", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Create save file dialog
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export BNK to JSON",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"bnk_export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    
                    // Create export data structure
                    var exportData = new
                    {
                        ExportInfo = new
                        {
                            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            BnkVersion = _fullFidelityBnk.BnkVersion.ToString("X"),
                            TotalObjects = _fullFidelityBnk.AllObjectsInOrder.Count,
                            OriginalBnkPath = _currentBnkPath ?? "Unknown"
                        },
                        Objects = _fullFidelityBnk.AllObjectsInOrder.Select(obj => {
                            // For sections (type 0xFF), extract section name
                            string sectionName = null;
                            if (obj.Type == 0xFF && obj.RawData != null && obj.RawData.Length >= 4)
                            {
                                try
                                {
                                    sectionName = Encoding.ASCII.GetString(obj.RawData, 0, 4);
                                }
                                catch { }
                            }

                            return new
                            {
                                Type = obj.Type,
                                TypeName = GetObjectTypeName(obj.Type),
                                SectionName = sectionName, // Only populated for type 0xFF
                                ObjectId = obj.ObjectId,
                                Length = obj.Length,
                                IsChanged = obj.IsChanged,
                                RawDataLength = obj.RawData?.Length ?? 0,
                                ParsedObject = SerializeParsedObject(obj.ParsedObject, obj.Type)
                            };
                        }).ToList()
                    };

                    // Serialize to JSON with pretty formatting
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    };

                    string jsonString = System.Text.Json.JsonSerializer.Serialize(exportData, jsonOptions);
                    
                    // Write to file
                    File.WriteAllText(saveFileDialog.FileName, jsonString);
                    
                    System.Windows.MessageBox.Show($"BNK structure exported successfully to:\n{saveFileDialog.FileName}", 
                        "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private string GetObjectTypeName(int type)
        {
            return type switch
            {
                1 => "Settings",
                2 => "Sound",
                3 => "EventAction",
                4 => "Event",
                5 => "SequenceContainer",
                6 => "SwitchContainer",
                7 => "ActorMixer",
                8 => "AudioBus",
                9 => "BlendContainer",
                10 => "MusicSegment",
                11 => "MusicTrack",
                12 => "MusicSwitchContainer",
                13 => "MusicPlaylistContainer",
                14 => "Attenuation",
                15 => "DialogueEvent",
                16 => "MotionBus",
                17 => "MotionFX",
                18 => "Effect",
                19 => "AuxiliaryBus",
                255 => "BNK_Section", // 0xFF - Non-HIRC sections like BKHD, DIDX, DATA, etc.
                _ => $"Unknown_{type}"
            };
        }

        private object? SerializeParsedObject(object? parsedObject, int type)
        {
            if (parsedObject == null)
                return null;

            try
            {
                // Create a simplified representation for each object type
                return type switch
                {
                    2 when parsedObject is Sound sound => new
                    {
                        SelfId = sound.SelfId,
                        Unknown1 = sound.Unknown1,
                        IsStreamed = sound.IsStreamed,
                        FileId = sound.FileId,
                        Reserved0 = sound.Reserved0,
                        StreamSourceType = sound.StreamSourceType,
                        StreamFileID = sound.StreamFileID,
                        StreamSourceID = sound.StreamSourceID,
                        SoundType = sound.SoundType,
                        SoundStructure = sound.SoundStructure != null ? new
                        {
                            PluginID = sound.SoundStructure.PluginID,
                            Type = sound.SoundStructure.Type,
                            Company = sound.SoundStructure.Company,
                            StreamType = sound.SoundStructure.StreamType,
                            SourceID = sound.SoundStructure.SourceID,
                            InMemoryMediaSize = sound.SoundStructure.InMemoryMediaSize,
                            SourceBits = sound.SoundStructure.SourceBits,
                            IsLanguageSpecific = sound.SoundStructure.IsLanguageSpecific,
                            Prefetch = sound.SoundStructure.Prefetch,
                            NonCachable = sound.SoundStructure.NonCachable,
                            HasSource = sound.SoundStructure.HasSource,
                            BIsOverrideParentFX = sound.SoundStructure.BIsOverrideParentFX,
                            NumFx = sound.SoundStructure.NumFx,
                            BIsOverrideParentMetadata = sound.SoundStructure.BIsOverrideParentMetadata
                        } : null
                    },
                    3 when parsedObject is EventAction eventAction => new
                    {
                        SelfId = eventAction.SelfId,
                        Scope = eventAction.Scope,
                        Type = eventAction.Type,
                        SoundObjectId = eventAction.SoundObjectId,
                        SwitchGroupId = eventAction.SwitchGroupId
                    },
                    4 when parsedObject is Event evt => new
                    {
                        SelfId = evt.SelfId,
                        EventAmount = evt.EventAmount,
                        EventActionIds = evt.EventIds?.ToList() ?? new List<uint>()
                    },
                    5 when parsedObject is RandomContainer sequenceContainer => new
                    {
                        SelfId = sequenceContainer.SelfId,
                        SwitchContainerId = sequenceContainer.SwitchContainerId,
                        SoundIdAmount = sequenceContainer.SoundIdAmount,
                        SoundIds = sequenceContainer.SoundIds?.ToList() ?? new List<uint>()
                    },
                    6 when parsedObject is SwitchContainer switchContainer => new
                    {
                        SelfId = switchContainer.SelfId,
                        ParentId = switchContainer.ParentId,
                        GroupType = switchContainer.GroupType,
                        GroupId = switchContainer.GroupId,
                        NumChildren = switchContainer.NumChildren,
                        Children = switchContainer.Children?.ToList() ?? new List<uint>()
                    },
                    _ => new { Type = parsedObject.GetType().Name, Note = "Not fully parsed - check structure definition" }
                };
            }
            catch (Exception ex)
            {
                return new { Error = $"Serialization failed: {ex.Message}" };
            }
        }

        private void LogBnkStructureToDebug(BnkFullFidelity bnk, string label)
        {
            try
            {





                // Group objects by type for easier analysis
                var objectsByType = bnk.AllObjectsInOrder.GroupBy(o => o.Type).OrderBy(g => g.Key);

                foreach (var typeGroup in objectsByType)
                {


                    foreach (var obj in typeGroup)
                    {
                        // Log basic object info


                        // Log parsed object details
                        if (obj.ParsedObject != null)
                        {
                            switch (obj.Type)
                            {
                                case 2 when obj.ParsedObject is Sound sound:
                                    LogSound(sound);
                                    break;
                                case 3 when obj.ParsedObject is EventAction ea:
                                    LogEventAction(ea);
                                    break;
                                case 4 when obj.ParsedObject is Event evt:
                                    LogEvent($"Event_{evt.SelfId}", evt.SelfId, evt);
                                    break;
                                case 5 when obj.ParsedObject is RandomContainer rc:
                                    LogRandomContainer(rc);
                                    break;
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {


            }
        }

        private void LogEvent(string eventName, uint eventId, Event eventObj)
        {




        }

        private void LogEventAction(EventAction ea)
        {




        }

        private void LogRandomContainer(RandomContainer rc)
        {




        }

        private void LogSound(Sound s)
        {



        }

        private byte[] RegenerateRandomContainerRawData(RandomContainer randomContainer, byte[] originalRawData)
        {
            try
            {
                // The RandomContainer RawData structure from ReadRandomContainerObject:
                // - Type byte (1 byte)
                // - Length (4 bytes)
                // - SelfId (4 bytes)
                // - BaseParams (variable, followed by 24 bytes skip)
                // - SoundIdAmount (4 bytes)
                // - SoundIds (4 bytes each)

                // We need to find where SoundIdAmount is located in the original RawData
                // and replace it along with the SoundIds list

                // Search for the SoundIdAmount position by looking for the pattern:
                // The old SoundIdAmount value should be somewhere after the SelfId

                using (var ms = new MemoryStream(originalRawData))
                using (var reader = new BinaryReader(ms))
                {
                    byte type = reader.ReadByte();
                    uint length = reader.ReadUInt32();
                    uint selfId = reader.ReadUInt32();

                    // Read until we find a position that could be SoundIdAmount
                    // We need to scan for the old sound ID list to find the exact position
                    long soundIdAmountPosition = -1;
                    long currentPos = reader.BaseStream.Position;

                    // Skip to near the end where SoundIdAmount should be
                    // We know the structure ends with: SoundIdAmount (4 bytes) + SoundIds (N * 4 bytes)
                    // So calculate backwards from the end
                    int oldSoundCount = (int)((originalRawData.Length - 5 - 4) % 4 == 0 ? 0 : -1);
                    if (oldSoundCount < 0)
                    {
                        // Try to find it by reading the original count from the stored value
                        // The SoundIdAmount should match the number of IDs at the end
                        for (long pos = 9; pos < originalRawData.Length - 4; pos++)
                        {
                            ms.Position = pos;
                            uint potentialCount = reader.ReadUInt32();

                            // Check if this could be the SoundIdAmount
                            // by verifying there's enough space for that many UInt32s
                            if (potentialCount > 0 && potentialCount < 1000 &&
                                pos + 4 + (potentialCount * 4) == originalRawData.Length - 5)
                            {
                                soundIdAmountPosition = pos;
                                break;
                            }
                        }
                    }

                    if (soundIdAmountPosition < 0)
                    {

                        return originalRawData; // Return unchanged if we can't find it
                    }

                    // Build new RawData
                    using (var outMs = new MemoryStream())
                    using (var writer = new BinaryWriter(outMs))
                    {
                        // Write everything before SoundIdAmount
                        writer.Write(originalRawData, 0, (int)soundIdAmountPosition);

                        // Write new SoundIdAmount and SoundIds
                        writer.Write(randomContainer.SoundIdAmount);
                        foreach (var soundId in randomContainer.SoundIds)
                        {
                            writer.Write(soundId);
                        }

                        // Update length field
                        byte[] newRawData = outMs.ToArray();
                        uint newLength = (uint)(newRawData.Length - 5); // Exclude type byte and length field

                        // Patch the length field at position 1-4
                        Array.Copy(BitConverter.GetBytes(newLength), 0, newRawData, 1, 4);

                        return newRawData;
                    }
                }
            }
            catch (Exception ex)
            {

                return originalRawData; // Return unchanged on error
            }
        }
    }
}
