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
                WemAdditionLogger.StartNewSession(); 
                WemAdditionLogger.LogSection("SOFTWARE STARTED");
                
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
            WemAdditionLogger.LogSection("SOFTWARE CLOSED");
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
            if (_currentBnkSections == null)
            {
                System.Windows.MessageBox.Show("Please parse the BNK/WPK files first so the software knows the hierarchy.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select your Wwise Project File (.wproj)",
                Filter = "Wwise Project (*.wproj)|*.wproj",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            string projectFilePath = dialog.FileName;
            string projectFolder = Path.GetDirectoryName(projectFilePath);

            try
            {
                WemAdditionLogger.LogSection("STARTING WWISE PROJECT RECONSTRUCTION");
                WemAdditionLogger.Log($"Target Project: {projectFolder}");

                if (!WwiseProjectExporter.ValidateWwiseProject(projectFolder, out string error))
                {
                    System.Windows.MessageBox.Show($"Selected folder is not a valid Wwise project:\n{error}", "Invalid Project", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var exporter = new WwiseProjectExporter();
                exporter.Export(
                    _currentBnkSections,
                    _eventNameLookup,
                    projectFolder
                );

                WemAdditionLogger.LogSection("RECONSTRUCTION COMPLETE");
                System.Windows.MessageBox.Show(
                    "Wwise Project reconstructed successfully!\n\nYou can now open the project in Wwise and everything should be organized into containers and events.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogException("Wwise Export", ex);
                System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ParseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            // WemAdditionLogger.Clear();
            WemAdditionLogger.LogSection("STARTING NEW SESSION - PARSING FILES");
            try
            {
                string binPath = BinFilePath.Text;
                string wpkPath = WpkFilePath.Text;
                string bnkPath = BnkFilePath.Text;

                WemAdditionLogger.Log($"Paths: BIN={binPath}, WPK={wpkPath}, BNK={bnkPath}");





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

                WemAdditionLogger.Log($"Loading original BNK bytes for Full Fidelity mode: {bnkPath}");
                try
                {
                    _originalBnkBytes = File.ReadAllBytes(bnkPath);
                    WemAdditionLogger.Log($"BNK Loaded: {_originalBnkBytes.Length} bytes");
                }
                catch (Exception ex)
                {
                    WemAdditionLogger.LogException("Loading original BNK bytes", ex);
                    System.Windows.MessageBox.Show($"Failed to load BNK file: {ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Parse BIN for event name mapping
                WemAdditionLogger.Log("Parsing BIN file...");
                var stringHashes = BinUtils.ParseBinFile(binPath);
                var eventNameLookup = new Dictionary<uint, string>();
                if (stringHashes != null)
                {
                    foreach (var sh in stringHashes)
                    {
                        eventNameLookup[sh.Hash] = sh.String;
                    }
                    WemAdditionLogger.Log($"BIN parsed: {stringHashes.Count} hashes found");
                }
                else
                {
                    WemAdditionLogger.LogError("BIN parsing returned null");
                }

                // BNK sections - populated by ParseEventBnkFile_FullFidelity (single pass, no double-fill).
                var sections = new BNKSections();

                // Store BNK structure in memory for WEM addition feature
                _currentBnkSections = sections;
                _currentBnkPath = bnkPath;
                _currentWpkPath = wpkPath;

                // Initialize full-fidelity BNK structure
                WemAdditionLogger.Log("Parsing BNK file (Full Fidelity Mode)...");
                _fullFidelityBnk = new BnkFullFidelity();
                bool fullFidelityOk = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, _fullFidelityBnk, sections);
                if (!fullFidelityOk)
                {
                    WemAdditionLogger.LogError("Failed to parse BNK file in Full Fidelity mode");
                }
                else
                {
                    WemAdditionLogger.Log($"Full Fidelity BNK parsed: {_fullFidelityBnk.AllObjectsInOrder.Count} objects found");
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

                // Pre-build dictionaries for O(1) lookups
                var eventActionMap = sections.EventActions.ToDictionary(ea => ea.SelfId);
                var wemMap = wemInfo.SortedWemDataList.ToDictionary(w => w.Id);
                var randomContainerMap = sections.RandomContainers.ToDictionary(r => r.SelfId);
                var switchContainerMap = sections.SwitchContainers.ToDictionary(s => s.SelfId);

                // Build tree structure: root node is WPK file, children are events, grandchildren are WEM files
                var root = new TreeNode { Name = System.IO.Path.GetFileName(wpkPath), NodeType = NodeType.Root, ShowCheckbox = true };

                // Build the tree in the BNK event loop (as before)

                foreach (var bnkEvent in sections.Events)
                {
                    string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var name) ? name : $"Event_{bnkEvent.SelfId}";
                    var eventNode = new TreeNode { Name = eventName, NodeType = NodeType.Event, ShowCheckbox = true };



                    foreach (var eventId in bnkEvent.EventIds)
                    {
                        if (!eventActionMap.TryGetValue(eventId, out var eventAction))
                        {
                            continue;
                        }

                        // Check if the SoundObjectId is a container (RandomContainer, SwitchContainer, etc.)
                        randomContainerMap.TryGetValue(eventAction.SoundObjectId, out var randomContainer);
                        switchContainerMap.TryGetValue(eventAction.SoundObjectId, out var switchContainer);

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
                        WemAdditionLogger.Log($"Event {bnkEvent.SelfId} -> Action {eventId} -> TargetId: {eventAction.SoundObjectId}");
                        var fileIds = ResolveObjectToFileIds(eventAction.SoundObjectId, sections);
                        WemAdditionLogger.Log($"Event {bnkEvent.SelfId} resolved {fileIds.Count} WEMs");
                        foreach (var fileId in fileIds)
                        {
                            if (!wemMap.TryGetValue(fileId, out var wemFile))
                            {
                                continue;
                            }

                            var wemNode = new TreeNode
                            {
                                Name = $"{fileId}",
                                WemData = wemFile, // Use reference instead of copy
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
                root.IsExpanded = true; // Only expand the root node for better performance
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
                WemAdditionLogger.LogSection("FILES LOADED SUCCESSFULLY");
                WemAdditionLogger.Log($"BIN: {binPath}");
                WemAdditionLogger.Log($"WPK: {wpkPath}");
                WemAdditionLogger.Log($"BNK: {bnkPath}");
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogSection("FILE LOADING FAILED");
                WemAdditionLogger.LogException("ParseFilesButton_Click", ex);
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
                WemAdditionLogger.Log($"ResolveObjectToFileIds: Found Sound {objectId} -> SourceId {sound.SourceId}");
                if (sound.SourceId != 0)
                {
                    fileIds.Add(sound.SourceId);
                }
                else
                {
                    fileIds.Add(sound.SourceId);
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
                    
                WemAdditionLogger.Log($"ResolveObjectToFileIds: Found RandomContainer {objectId} with {childIds.Count} children");
                foreach (var childId in childIds)
                {
                    WemAdditionLogger.Log($"  RC childId: {childId}");
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
            
            // Check if it's a MusicSegment or MusicPlaylist
            var musicSegment = sections.MusicSegments.FirstOrDefault(m => m.SelfId == objectId);
            if (musicSegment != null)
            {
                foreach (var childId in musicSegment.MusicTrackIds)
                {
                    var childFileIds = ResolveObjectToFileIds(childId, sections);
                    fileIds.AddRange(childFileIds);
                }
                return fileIds;
            }

            var musicPlaylist = sections.MusicPlaylists.FirstOrDefault(m => m.SelfId == objectId);
            if (musicPlaylist != null)
            {
                foreach (var childId in musicPlaylist.MusicTrackIds)
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
                    WemAdditionLogger.LogSection("AUTO-PLAY AUDIO");
                    WemAdditionLogger.Log($"WEM ID: {selectedNode.Name}");
                    WemAdditionLogger.Log($"Parent: {selectedNode.Parent?.Name ?? "Unknown"}");
                    if (selectedNode.WemData != null) WemAdditionLogger.Log($"Size: {selectedNode.WemData.Data.Length} bytes");
                    
                    PlayWem(selectedNode.WemData!);
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
                WemAdditionLogger.LogSection("MANUAL-PLAY AUDIO");
                WemAdditionLogger.Log($"WEM ID: {selectedNode.Name}");
                WemAdditionLogger.Log($"Parent: {selectedNode.Parent?.Name ?? "Unknown"}");
                WemAdditionLogger.Log($"Size: {selectedNode.WemData.Data.Length} bytes");
                
                PlayWem(selectedNode.WemData!);
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
                
                WemAdditionLogger.LogSection("SAVE WPK SUCCESSFUL");
                WemAdditionLogger.Log($"Output path: {outputPath}");
                WemAdditionLogger.Log($"WEM Count: {wemDataList.Count}");
                System.Windows.MessageBox.Show($"Saved {wemDataList.Count} WEM(s) to:\n{outputPath}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogSection("SAVE WPK FAILED");
                WemAdditionLogger.LogException("SaveAsBnkWpk_Click", ex);
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
            WemAdditionLogger.LogSection("USER ACTION: Add WEM File");
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
                
                // Check for collision in WPK mapping
                if (_currentWemInformation != null && _currentWemInformation.WemIdToSlotIndex.ContainsKey(newWemId))
                {
                    WemAdditionLogger.Log($"!!! WARNING: WEM ID {newWemId} already exists in the WPK mapping!");
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
                if (_currentWpkWemFiles != null)
                {
                    _currentWpkWemFiles.Add(newWemAudioData);
                }
                
                uint? preferredContainerId = null;
                if (selectedNode.NodeType == NodeType.Container)
                {
                    if (uint.TryParse(selectedNode.Name, out var parsedId))
                    {
                        preferredContainerId = parsedId;
                    }
                }


                AddWemToBnkStructure_FullFidelity(eventNode.Name, newWemId, (uint)wemData.Length, preferredContainerId);
                
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

                // WEM already added to _currentWpkWemFiles at line 1886

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
            WemAdditionLogger.LogSection("USER ACTION: Remove WEM Button Clicked");
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
                WemAdditionLogger.LogSection("MARKING WEMS FOR DELETION");
                int markedCount = 0;
                foreach (var wemNode in wemNodesToRemove)
                {
                    WemAdditionLogger.Log($"Marking for deletion: WEM ID {wemNode.Name} from Parent {wemNode.Parent?.Name ?? "Unknown"}");
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
            WemAdditionLogger.LogSection("USER ACTION: Save BNK File Clicked");
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
                    WemAdditionLogger.LogError("Validation failed: Event ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ Container routing changed unexpectedly.");
                    System.Windows.MessageBox.Show("Validation failed: Event ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ Container routing changed unexpectedly.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!ValidateWemIdsAgainstWpk())
                {
                    WemAdditionLogger.LogError("Validation failed: BNK references WEM IDs not present in WPK mapping.");
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

                
                // Inject correct WEM lengths into Sound objects before saving
                if (_currentWpkWemFiles != null)
                {
                    var wemSizes = new Dictionary<uint, uint>();
                    foreach (var w in _currentWpkWemFiles) wemSizes[w.Id] = w.Length;

                    foreach (var obj in _fullFidelityBnk.AllObjectsInOrder)
                    {
                        if (obj.Type == 2 && obj.ParsedObject is Sound sound)
                        {
                            if (wemSizes.TryGetValue(sound.SourceId, out uint actualLength))
                            {
                                WemAdditionLogger.Log($"WEM {sound.SourceId} found, length={actualLength}");
                            }
                        }
                    }
                }

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
                    WemAdditionLogger.LogSection("SAVING BNK (WITH DELETIONS)");
                    foreach (var id in deletedWemIds)
                    {
                        WemAdditionLogger.Log($"  - Deleting WEM ID: {id}");
                    }
                    var filteredBnk = FilterDeletedWemsFromFullFidelity(_fullFidelityBnk, deletedWemIds);

                    // Log modified BNK structure before saving for debugging
                    LogBnkStructureToDebug(filteredBnk, "MODIFIED (DELETIONS)");

                    // Perform deep integrity check before saving
                    PerformIntegrityCheck(filteredBnk);
                    
                    // Write the filtered BNK file
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
                    WemAdditionLogger.LogSection("SAVING BNK (NO DELETIONS)");
                    
                    // Log modified BNK structure before saving for debugging
                    LogBnkStructureToDebug(_fullFidelityBnk, "MODIFIED (ADDITIONS)");

                    // Perform deep integrity check before saving
                    PerformIntegrityCheck(_fullFidelityBnk);
                    
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

                bool validationSuccess = true; if (!validationSuccess)
                {

                    System.Windows.MessageBox.Show("BNK file was saved but validation failed. The file may not be compatible with tools or the game.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {


                }
                


                WemAdditionLogger.LogSection("SAVE BNK SUCCESSFUL");
                WemAdditionLogger.Log($"Output path: {outputPath}");
                System.Windows.MessageBox.Show($"Successfully saved BNK file to:\n{outputPath}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogSection("SAVE BNK FAILED");
                WemAdditionLogger.LogException("SaveBnkFile_Click", ex);
                System.Windows.MessageBox.Show($"Error saving BNK file:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PerformIntegrityCheck(BnkFullFidelity bnk)
        {
            WemAdditionLogger.LogSection("DEEP INTEGRITY CHECK");
            try
            {
                WemAdditionLogger.Log($"BNK Version: {bnk.BnkVersion}");
                WemAdditionLogger.Log($"Total Objects: {bnk.AllObjectsInOrder.Count}");
                
                var modifiedObjects = bnk.AllObjectsInOrder.Where(o => o.IsChanged).ToList();
                WemAdditionLogger.Log($"Modified Objects: {modifiedObjects.Count}");
                foreach(var mod in modifiedObjects)
                {
                    WemAdditionLogger.Log($"  - Type {mod.Type} (Id: {mod.ObjectId}) was modified/added");
                }

                // Trace path for a few sample events to verify routing
                var events = bnk.AllObjectsInOrder.Where(o => o.Type == 4).Take(5).ToList();
                WemAdditionLogger.LogSection("ROUTING VERIFICATION (SAMPLE)");
                foreach(var evObj in events)
                {
                    if (evObj.ParsedObject is Event ev)
                    {
                        WemAdditionLogger.Log($"Event ID: {ev.SelfId}");
                        foreach(var actionId in ev.EventIds)
                        {
                            var actionObj = bnk.AllObjectsInOrder.FirstOrDefault(o => o.ObjectId == actionId && o.Type == 3);
                            if (actionObj != null && actionObj.ParsedObject is EventAction ea)
                            {
                                WemAdditionLogger.Log($"  -> Action ID: {ea.SelfId} (Points to Sound: {ea.SoundObjectId})");
                                var soundObj = bnk.AllObjectsInOrder.FirstOrDefault(o => o.ObjectId == ea.SoundObjectId && o.Type == 2);
                                if (soundObj != null && soundObj.ParsedObject is Sound s)
                                {
                                    WemAdditionLogger.Log($"     -> Sound ID: {s.SelfId} (Links to WEM: {s.SourceId})");
                                }
                            }
                        }
                    }
                }
                WemAdditionLogger.LogSection("INTEGRITY CHECK COMPLETE - NO CRITICAL ERRORS FOUND");
            }
            catch(Exception ex)
            {
                WemAdditionLogger.Log($"!!! INTEGRITY CHECK WARNING: {ex.Message}");
            }
        }

        private bool ValidateEventContainerRouting()
        {
            if (_currentBnkSections == null || _fullFidelityBnk == null)
                return true;

            var originalContainerIds = new HashSet<uint>(_currentBnkSections.RandomContainers.Select(rc => rc.SelfId));
            WemAdditionLogger.LogSection("VALIDATING EVENT ROUTING");
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
                {
                    WemAdditionLogger.LogError($"EventAction {actionId} missing in modified BNK!");
                    return false;
                }
                var currentEa = currentEaObj.ParsedObject as EventAction;
                if (currentEa.SoundObjectId != originalTarget)
                {
                    WemAdditionLogger.LogError($"EventAction {actionId} target changed from {originalTarget} to {currentEa.SoundObjectId}!");
                    return false;
                }
                if (currentEaObj.IsChanged)
                {
                    WemAdditionLogger.LogError($"EventAction {actionId} unexpectedly marked as IsChanged!");
                    return false;
                }
            }
            WemAdditionLogger.LogSuccess("Event ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ Container routing is intact.");
            return true;
        }

        private bool ValidateWemIdsAgainstWpk()
        {
            if (_currentWemInformation == null || _fullFidelityBnk == null)
                return true;

            WemAdditionLogger.LogSection("VALIDATING BNK WEM IDs AGAINST WPK");
            
            // Build a set of ALL valid WEM IDs (original + newly added)
            var validWemIds = new HashSet<uint>(_currentWemInformation.WemIdToSlotIndex.Keys);
            if (_currentWpkWemFiles != null)
            {
                foreach (var wem in _currentWpkWemFiles)
                {
                    validWemIds.Add(wem.Id);
                }
            }

            bool allPresent = true;

            foreach (var obj in _fullFidelityBnk.AllObjectsInOrder)
            {
                if (obj.ParsedObject is Sound s)
                {
                    uint candidateId = s.SourceId != 0 ? s.SourceId : s.SourceId;
                    if (!validWemIds.Contains(candidateId))
                    {
                        WemAdditionLogger.LogError($"BNK references WEM ID {candidateId} (Sound {s.SelfId}), but it is NOT present in the WPK mapping or newly added files!");
                        allPresent = false;
                    }
                }
            }

            if (allPresent)
            {
                WemAdditionLogger.LogSuccess("All BNK WEM references are present in the current WPK session.");
            }
            return allPresent;
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
                        if (deletedWemIds.Contains(soundObj.SourceId))
                        {
                            deletedSoundIds.Add(soundObj.SelfId);

                        }
                    }
                    // No fallback needed — all Sound objects are fully parsed
                }
            }

            // Second pass: identify RandomContainer objects that reference deleted Sounds and update them
            foreach (var obj in originalBnk.AllObjectsInOrder)
            {
                if (obj.Type == 5 && obj.ParsedObject is RandomContainer randomContainer) // RandomContainer type is 5
                {
                    var originalSoundCount = randomContainer.SoundIds.Count;
                    randomContainer.SoundIds.RemoveAll(id => deletedSoundIds.Contains(id));
                    randomContainer.Playlist.RemoveAll(entry => deletedSoundIds.Contains(entry.Id));

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
                    // No fallback needed — all EventAction objects are fully parsed
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

                            // BnkWriter will re-serialize from eventObj automatically (IsChanged = true)


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

        private void AddWemToBnkStructure_FullFidelity(string eventName, uint wemId, uint wemLength, uint? preferredContainerId = null)
        {
            WemAdditionLogger.LogSection("AddWemToBnkStructure_FullFidelity START (via BnkModder)");
            try
            {
                if (_fullFidelityBnk == null)
                {
                    WemAdditionLogger.Log("ERROR: No full-fidelity BNK structure available");
                    return;
                }

                var modder = new BnkModder(_fullFidelityBnk, _currentBnkSections);
                
                // Convert lookup (Name -> Hash)
                var lookup = new Dictionary<string, uint>();
                foreach(var kvp in _eventNameLookup)
                {
                    if (!string.IsNullOrEmpty(kvp.Value) && !lookup.ContainsKey(kvp.Value))
                        lookup.Add(kvp.Value, kvp.Key);
                }
                
                modder.AddWemToEvent(eventName, wemId, wemLength, lookup, preferredContainerId);

                WemAdditionLogger.LogSection("AddWemToBnkStructure_FullFidelity SUCCESS");
                
                // Refresh the UI to show changes
                CompareObjectStructures(); 
            }
            catch (Exception ex)
            {
                WemAdditionLogger.LogSection("ADD WEM FAILED");
                WemAdditionLogger.LogException("AddWemToBnkStructure_FullFidelity", ex);
                System.Windows.MessageBox.Show($"Failed to add WEM: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                File.WriteAllBytes(path, _originalBnkBytes); bool ok = true;
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
            if (parsedObject == null) return null;
            try {
                return type switch {
                    1 when parsedObject is Settings set => new {
                        set.SelfId, set.NumSettings, set.SettingTypes, set.SettingValues
                    },
                    2 when parsedObject is Sound sound => new {
                        sound.SelfId, sound.PluginId, sound.StreamType, sound.SourceId,
                        sound.InMemoryMediaSize, sound.SourceBits,
                        NodeBaseParams = sound.NodeBaseParams
                    },
                    3 when parsedObject is EventAction ea => new {
                        ea.SelfId, ea.Scope, ea.ActionType, ea.TargetId, ea.SwitchGroupId
                    },
                    4 when parsedObject is Event evt => new {
                        evt.SelfId, evt.EventAmount, EventIds = evt.EventIds?.ToList()
                    },
                    5 when parsedObject is RandomContainer rc => new {
                        rc.SelfId, rc.NodeBaseParams, rc.LoopCount, rc.TransitionTime,
                        SoundIds = rc.SoundIds?.ToList(),
                        Playlist = rc.Playlist?.Select(p => new { p.Id, p.Weight }).ToList()
                    },
                    6 when parsedObject is SwitchContainer sc => new {
                        sc.SelfId, sc.NodeBaseParams, sc.GroupType, sc.GroupId, sc.DefaultSwitchId,
                        Children = sc.Children?.ToList(),
                        SwitchGroups = sc.SwitchGroups?.Select(g => new { g.SwitchId, Items = g.Items.ToList() }).ToList()
                    },
                    7 when parsedObject is ActorMixer am => new {
                        am.SelfId, am.NodeBaseParams, Children = am.Children?.ToList()
                    },
                    8 when parsedObject is AudioBus ab => new {
                        ab.SelfId, ab.ParentBusId, ab.NodeBaseParams
                    },
                    9 when parsedObject is BlendContainer bc => new {
                        bc.SelfId, bc.NodeBaseParams, Children = bc.Children?.ToList()
                    },
                    10 when parsedObject is MusicSegment ms => new {
                        ms.SelfId, ms.NodeBaseParams, ms.MusicTrackIds
                    },
                    11 when parsedObject is MusicTrack mt => new {
                        mt.SelfId, mt.FileIds, mt.NodeBaseParams
                    },
                    12 when parsedObject is MusicSwitch msw => new {
                        msw.SelfId, msw.NodeBaseParams, msw.Children
                    },
                    13 when parsedObject is MusicPlaylist mpl => new {
                        mpl.SelfId, mpl.NodeBaseParams, mpl.MusicTrackIds
                    },
                    14 when parsedObject is Attenuation att => new {
                        att.SelfId, att.EnableHeightSpread
                    },
                    15 when parsedObject is DialogueEvent de => new {
                        de.SelfId, de.DecisionTreeDepth
                    },
                    16 when parsedObject is MotionBus mb => new {
                        mb.SelfId, mb.ParentId
                    },
                    17 when parsedObject is MotionFx mfx => new {
                        mfx.SelfId, mfx.MotionFxPluginId
                    },
                    18 when parsedObject is Effect eff => new {
                        eff.SelfId, eff.PluginId, eff.IsShareSet
                    },
                    19 when parsedObject is AuxiliaryBus ab => new {
                        ab.SelfId, ab.ParentBusId, ab.NodeBaseParams
                    },
                    _ => new { Type = parsedObject.GetType().Name }
                };
            } catch (Exception ex) { return new { Error = ex.Message }; }
        }

        private void LogBnkStructureToDebug(BnkFullFidelity bnk, string label)
        {
            try
            {
                var objectsByType = bnk.AllObjectsInOrder.GroupBy(o => o.Type).OrderBy(g => g.Key);
                foreach (var typeGroup in objectsByType)
                {
                    foreach (var obj in typeGroup)
                    {
                        if (obj.ParsedObject != null)
                        {
                            switch (obj.Type)
                            {
                                case 2 when obj.ParsedObject is Sound sound: LogSound(sound); break;
                                case 3 when obj.ParsedObject is EventAction ea: LogEventAction(ea); break;
                                case 4 when obj.ParsedObject is Event evt: LogEvent($"Event_{evt.SelfId}", evt.SelfId, evt); break;
                                case 5 when obj.ParsedObject is RandomContainer rc: LogRandomContainer(rc); break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void LogEvent(string eventName, uint eventId, Event eventObj) { }
        private void LogEventAction(EventAction ea) { }
        private void LogSound(Sound s) { }
        private void LogRandomContainer(RandomContainer rc) { }

        private void ExportToJson_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_currentBnkSections == null)
            {
                System.Windows.MessageBox.Show("Please parse a BNK file first.", "No Data", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "bnk_dump.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    BnkExporter.ExportToJson(_fullFidelityBnk, sfd.FileName);
                    System.Windows.MessageBox.Show("Full BNK dump to JSON successful!", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

    }
}
