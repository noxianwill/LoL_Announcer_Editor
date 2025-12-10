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
                Defs.LogApplicationStart();
                
                // Load settings on startup
                SettingsManager.LoadSettings();
                
                // Validate critical dependencies
                ValidateDependencies();
                
                Defs.LogAction("MainWindow initialized successfully");
            }
            catch (Exception ex)
            {
                Defs.LogError($"MainWindow initialization failed: {ex.Message}");
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
                    Defs.LogWarning($"vgmstream-cli.exe not found at {vgmstreamExe}");
                }
                else
                {
                    Defs.LogAction("vgmstream-cli.exe found successfully");
                }

                // Check if temp directory can be created
                string tempDir = GetCustomTempDir();
                Defs.LogAction($"Temp directory validated: {tempDir}");

                // Test NAudio initialization
                try
                {
                    var testDevice = new WaveOutEvent();
                    testDevice.Dispose();
                    Defs.LogAction("NAudio initialization successful");
                }
                catch (Exception ex)
                {
                    Defs.LogError($"NAudio initialization failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Defs.LogError($"Dependency validation failed: {ex.Message}");
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

        private void ParseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string binPath = BinFilePath.Text;
                string wpkPath = WpkFilePath.Text;
                string bnkPath = BnkFilePath.Text;

                Defs.LogAction($"ParseFilesButton_Click: bin={binPath}, wpk={wpkPath}, bnk={bnkPath}");
                ScenarioLogger.StartScenario("Announcer Editing Scenario");
                ScenarioLogger.LogSection("Parse Original Files");

                // Validate file paths
                if (string.IsNullOrEmpty(binPath) || !System.IO.File.Exists(binPath))
                {
                    Defs.LogAction("ParseFilesButton_Click: BIN file path is invalid or file doesn't exist");
                    System.Windows.MessageBox.Show("Please select a valid BIN file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(wpkPath) || !System.IO.File.Exists(wpkPath))
                {
                    Defs.LogAction("ParseFilesButton_Click: WPK file path is invalid or file doesn't exist");
                    System.Windows.MessageBox.Show("Please select a valid WPK file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(bnkPath) || !System.IO.File.Exists(bnkPath))
                {
                    Defs.LogAction("ParseFilesButton_Click: BNK file path is invalid or file doesn't exist");
                    System.Windows.MessageBox.Show("Please select a valid BNK file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // CRITICAL FIX: Load the original BNK bytes first for full-fidelity saving
                Defs.LogAction("ParseFilesButton_Click: Loading original BNK bytes for full-fidelity saving");
                try
                {
                    _originalBnkBytes = File.ReadAllBytes(bnkPath);
                    Defs.LogAction($"ParseFilesButton_Click: Successfully loaded {_originalBnkBytes.Length} bytes from BNK file");
                }
                catch (Exception ex)
                {
                    Defs.LogAction($"ParseFilesButton_Click: Failed to load original BNK bytes: {ex.Message}");
                    System.Windows.MessageBox.Show($"Failed to load BNK file: {ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Parse BIN for event name mapping
                Defs.LogAction("ParseFilesButton_Click: Starting BIN file parsing");
                var stringHashes = BinUtils.ParseBinFile(binPath);
                var eventNameLookup = new Dictionary<uint, string>();
                if (stringHashes != null)
                {
                    foreach (var sh in stringHashes)
                    {
                        eventNameLookup[sh.Hash] = sh.String;
                    }
                    Defs.LogAction($"ParseFilesButton_Click: Parsed {stringHashes.Count} string hashes from BIN file");
                }
                else
                {
                    Defs.LogAction("ParseFilesButton_Click: Failed to parse BIN file or no string hashes found");
                }

                // Parse BNK using BNKSections and ParseEventBnkFile
                Defs.LogAction("ParseFilesButton_Click: Starting BNK file parsing");
                var sections = new BNKSections();
                if (!BnkExtractor.ParseEventBnkFile(bnkPath, sections))
                {
                    Defs.LogAction("ParseFilesButton_Click: Failed to parse BNK file.");
                    System.Windows.MessageBox.Show("Failed to parse BNK file. Please check if the file is valid.", "Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                Defs.LogAction($"ParseFilesButton_Click: Successfully parsed BNK file with {sections.Events.Count} events");
                ScenarioLogger.LogInfo($"Original BNK objects: Events={sections.Events.Count}, Sounds={sections.Sounds.Count}, EventActions={sections.EventActions.Count}");
                ScenarioLogger.LogFileSize("Original BNK", bnkPath);
                
                // Store BNK structure in memory for WEM addition feature
                _currentBnkSections = sections;
                _currentBnkPath = bnkPath;
                _currentWpkPath = wpkPath;

                // Initialize full-fidelity BNK structure
                Defs.LogAction("ParseFilesButton_Click: Initializing full-fidelity BNK structure");
                _fullFidelityBnk = new BnkFullFidelity();
                bool fullFidelityOk = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, _fullFidelityBnk, sections);
                if (!fullFidelityOk)
                {
                    Defs.LogAction("ParseFilesButton_Click: Warning - Failed to parse BNK file with full fidelity, but continuing with basic parsing");
                }
                else
                {
                    Defs.LogAction($"ParseFilesButton_Click: Successfully parsed BNK file with full fidelity, found {_fullFidelityBnk.AllObjectsInOrder.Count} objects");

                    // Log original BNK structure for debugging
                    BnkDebugLogger.StartNewSession();
                    BnkDebugLogger.LogSection("ORIGINAL BNK STRUCTURE (After Parse)");
                    LogBnkStructureToDebug(_fullFidelityBnk, "ORIGINAL");
                }

                // Build a set of BNK event IDs
                var bnkEventIds = new HashSet<uint>(sections.Events.Select(e => e.SelfId));
                Defs.LogAction($"ParseFilesButton_Click: Found {bnkEventIds.Count} unique BNK event IDs");

                // Parse WPK with stringHashes and bnkEventIds
                Defs.LogAction("ParseFilesButton_Click: Starting WPK file parsing");
                var wemInfo = WpkExtractor.ParseWpkFile(wpkPath, stringHashes ?? new List<StringHash>(), bnkEventIds);
#pragma warning disable CS8602 // Dereference of a possibly null reference
                Defs.LogAction($"ParseFilesButton_Click: Successfully parsed WPK file with {wemInfo.SortedWemDataList.Count} WEM files");
#pragma warning restore CS8602
                ScenarioLogger.LogInfo($"Original WPK slots={wemInfo.OriginalSlotCount}, files={wemInfo.SortedWemDataList.Count}");
                ScenarioLogger.LogFileSize("Original WPK", wpkPath);
                
                // Store WPK WEM files and slot information in memory for WEM addition feature
                _currentWpkWemFiles = wemInfo.SortedWemDataList;
                _currentWemInformation = wemInfo;  // Store full WemInformation for slot preservation

                // Build tree structure: root node is WPK file, children are events, grandchildren are WEM files
                var root = new TreeNode { Name = System.IO.Path.GetFileName(wpkPath), NodeType = NodeType.Root, ShowCheckbox = true };

                // Build the tree in the BNK event loop (as before)
                Defs.LogAction("ParseFilesButton_Click: Building tree structure");
                foreach (var bnkEvent in sections.Events)
                {
                    string eventName = eventNameLookup.TryGetValue(bnkEvent.SelfId, out var name) ? name : $"Event_{bnkEvent.SelfId}";
                    var eventNode = new TreeNode { Name = eventName, NodeType = NodeType.Event, ShowCheckbox = true };

                    Defs.LogAction($"[DEBUG] Building tree for event: {eventName} (SelfId={bnkEvent.SelfId}) with EventIds=[{string.Join(", ", bnkEvent.EventIds)}]");

                    foreach (var eventId in bnkEvent.EventIds)
                    {
                        var eventAction = sections.EventActions.Find(ea => ea.SelfId == eventId);
                        if (eventAction == null)
                        {
                            Defs.LogAction($"[DEBUG]   Skipping EventAction {eventId} (not found)");
                            continue;
                        }
                        Defs.LogAction($"[DEBUG]   Using EventAction {eventAction.SelfId} Type={eventAction.Type} Target={eventAction.SoundObjectId}");

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
                            Defs.LogAction($"[DEBUG]   Added Container node: {containerId} to event {eventName}");
                        }

                        // Now add the WEM files
                        var fileIds = ResolveObjectToFileIds(eventAction.SoundObjectId, sections);
                        foreach (var fileId in fileIds)
                        {
                            var wemFile = wemInfo.SortedWemDataList.Find(w => w.Id == fileId);
                            if (wemFile == null)
                            {
                                Defs.LogAction($"[DEBUG]     WEM fileId {fileId} not found in WPK");
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
                            Defs.LogAction($"[DEBUG]     Added WEM node: {fileId} to {(parentForWems.NodeType == NodeType.Container ? "container " + parentForWems.Name : "event " + eventName)}");
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
                    Defs.LogAction($"[DEBUG] Added Unreferenced WEMs: {unref.Children.Count}");
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
                        Defs.LogAction("--------------------------------------------------");
                        // Log all event parameters
                        Defs.LogAction($"EVENT:");
                        Defs.LogAction($"  Name: {eventNode.Name}");
                        Defs.LogAction($"  ID: {bnkEvent.SelfId}");
                        Defs.LogAction($"  EventAmount: {bnkEvent.EventAmount}");
                        Defs.LogAction($"  EventIds: [{string.Join(", ", bnkEvent.EventIds)}]");
                        Defs.LogAction($"  Related WEM(s):");
                        foreach (var wemNode in eventNode.Children)
                        {
                            var wem = wemNode.WemData;
                            if (wem != null)
                            {
                                Defs.LogAction($"    - Name: {wemNode.Name}");
                                Defs.LogAction($"      ID: {wem.Id}");
                                Defs.LogAction($"      Length: {wem.Length}");
                            }
                        }
                    }
                }

                // Log totals at the end
                Defs.LogAction("");
                Defs.LogAction($"Total events found: {root.Children.Count}");
                Defs.LogAction($"Total wem files found: {wemInfo.SortedWemDataList.Count}");

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
                Defs.LogAction($"Total wem used: {wemUsageTotal}");
                
                // Enable the Save BNK file button since we now have BNK structure in memory
                SaveBnkFileButton.IsEnabled = true;
                Defs.LogAction($"ParseFilesButton_Click: Enabled Save BNK button. _originalBnkBytes: {(_originalBnkBytes == null ? "null" : _originalBnkBytes.Length + " bytes")}");

                // Store event name lookup
                _eventNameLookup = eventNameLookup;
            }
            catch (Exception ex)
            {
                Defs.LogAction($"ParseFilesButton_Click: Unexpected exception: {ex.Message}");
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
                fileIds.Add(sound.FileId);
                return fileIds;
            }
            
            // Check if it's a RandomContainer
            var randomContainer = sections.RandomContainers.FirstOrDefault(r => r.SelfId == objectId);
            if (randomContainer != null)
            {
                foreach (var childId in randomContainer.SoundIds)
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
                Defs.LogAction("DeleteTreeView_Click: Invoked");
                if (TreeView.ItemsSource is ObservableCollection<TreeNode> roots)
                {
                    Defs.LogAction($"DeleteTreeView_Click: Root nodes count before clear: {roots.Count}");
                    foreach (var root in roots)
                    {
                        Defs.LogAction($"DeleteTreeView_Click: Root node '{root.Name}' has {root.Children.Count} children");
                    }
                }
                else
                {
                    Defs.LogAction("DeleteTreeView_Click: TreeView.ItemsSource is not ObservableCollection<TreeNode>");
                }
                _rootNodes.Clear();
                TreeView.ItemsSource = null;
                Defs.LogAction("DeleteTreeView_Click: Cleared _rootNodes and reset TreeView.ItemsSource");
                // Unload all file paths
                BinFilePath.Text = string.Empty;
                WpkFilePath.Text = string.Empty;
                BnkFilePath.Text = string.Empty;
                Defs.LogAction("DeleteTreeView_Click: Cleared file path TextBoxes (Bin, Wpk, Bnk)");
            }
            catch (Exception ex)
            {
                Defs.LogAction($"DeleteTreeView_Click: Exception: {ex.Message}\n{ex.StackTrace}");
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
                Defs.LogAction($"Extracting {selectedItems.Count} items to: {extractDir}");

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

                Defs.LogAction($"Extraction completed: {extractedCount}/{totalFiles} files extracted");
            }
            catch (Exception ex)
            {
                ExtractionProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;
                Defs.LogAction($"Extraction failed: {ex.Message}");
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
                        Defs.LogAction($"Extracted WEM file: {wemPath} - Data length: {item.WemData.Data.Length} bytes, ReplacementFileName: {item.ReplacementFileName ?? "None"}");
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
                Defs.LogAction($"Extracting item: {item.Name} (Type: {item.NodeType})");
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
                        Defs.LogAction($"Unknown node type for extraction: {item.NodeType}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Defs.LogAction($"Failed to extract item {item.Name}: {ex.Message}");
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
                
                Defs.LogAction($"Extracted {extractedCount} WEM files from event: {eventNode.Name}");
                return extractedCount > 0;
            }
            catch (Exception ex)
            {
                Defs.LogAction($"Failed to extract event node {eventNode.Name}: {ex.Message}");
                return false;
            }
        }

        private bool ExtractWemFile(TreeNode wemNode, string extractDir)
        {
            try
            {
                if (wemNode.WemData == null)
                {
                    Defs.LogAction($"WEM node {wemNode.Name} has no audio data");
                    return false;
                }

                // Always extract as WEM file
                string wemFileName2 = !string.IsNullOrEmpty(wemNode.ReplacementFileName)
                    ? System.IO.Path.GetFileNameWithoutExtension(wemNode.ReplacementFileName)
                    : wemNode.Name;
                string wemPath = System.IO.Path.Combine(extractDir, SanitizeFileName(wemFileName2) + ".wem");
                File.WriteAllBytes(wemPath, wemNode.WemData.Data);
                Defs.LogAction($"Extracted WEM file: {wemPath}");

                // Optionally extract as OGG file
                if (SettingsManager.ExtractOgg)
                {
                    string oggPath = System.IO.Path.Combine(extractDir, SanitizeFileName(wemNode.Name) + ".ogg");
                    if (ConvertWemToOgg(wemNode.WemData.Data, oggPath))
                    {
                        Defs.LogAction($"Extracted OGG file: {oggPath}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Defs.LogAction($"Failed to extract WEM file {wemNode.Name}: {ex.Message}");
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
                    Defs.LogAction("vgmstream-cli.exe not found, cannot convert to OGG");
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
                        Defs.LogAction($"vgmstream conversion failed with exit code: {process.ExitCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Defs.LogAction($"Failed to convert WEM to OGG: {ex.Message}");
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
                    Defs.LogAction($"Auto-playing audio for {selectedNode.Name} (PlayOnClick enabled)");
#pragma warning disable CS8602 // Dereference of a possibly null reference
                    PlayWem(selectedNode.WemData!);
#pragma warning restore CS8602
                }
                else
                {
                    Defs.LogAction($"Auto-play disabled for {selectedNode.Name} (PlayOnClick disabled)");
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
                    Defs.LogAction("PlayWem: wemData is null or empty");
                    return;
                }

                string tempDir = GetCustomTempDir();
                string wavFilePath;
                uint wemId = wemData.Id;
                if (_wemWavCache.TryGetValue(wemId, out wavFilePath) && System.IO.File.Exists(wavFilePath))
                {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
                    // Play from cache
                    Defs.LogAction($"PlayWem: Playing from cache for WEM ID {wemId}");
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
                    Defs.LogAction($"PlayWem: vgmstream-cli.exe not found at {vgmstreamExe}");
                    System.Windows.MessageBox.Show("vgmstream-cli.exe not found. Please ensure it's in the soundlibs/vgmstream directory.");
                    return;
                }

                string wemFileName = Guid.NewGuid().ToString() + ".wem";
                string wavFileName = Guid.NewGuid().ToString() + ".wav";
                _currentTempWem = System.IO.Path.Combine(tempDir, wemFileName);
                _currentTempWav = System.IO.Path.Combine(tempDir, wavFileName);
                
                Defs.LogAction($"PlayWem: Converting WEM ID {wemId} to {_currentTempWav}");
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
                        Defs.LogAction("PlayWem: Conversion cancelled");
                        CleanupPlayback();
                        return;
                    }

                    if (!System.IO.File.Exists(_currentTempWav))
                    {
                        Defs.LogAction($"PlayWem: Failed to convert WEM to WAV. Process exit code: {process.ExitCode}");
                        System.Windows.MessageBox.Show("Failed to convert WEM to WAV.");
                        // Do not delete temp files here
                        return;
                    }

                    // Add to cache
                    _wemWavCache[wemId] = _currentTempWav;
                    Defs.LogAction($"PlayWem: Successfully converted and cached WEM ID {wemId}");

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
                    Defs.LogAction("PlayWem: Operation cancelled");
                    CleanupPlayback();
                }
                catch (Exception ex)
                {
                    Defs.LogAction($"PlayWem: Exception during conversion: {ex.Message}");
                    System.Windows.MessageBox.Show($"Failed to play sound: {ex.Message}");
                    CleanupPlayback();
                }
            }
            catch (Exception ex)
            {
                Defs.LogAction($"PlayWem: Unexpected exception: {ex.Message}");
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
            Defs.LogAction("ReplaceWemData_Click: Invoked");
            // Use selected WEM node(s) instead of checked nodes
            var selectedWemNodes = GetSelectedWemNodesFromSelection();
            Defs.LogAction($"ReplaceWemData_Click: Selected WEM nodes count: {selectedWemNodes.Count}");
            if (selectedWemNodes.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one WEM file node to replace.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                Defs.LogAction("ReplaceWemData_Click: No WEM nodes selected");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "WEM audio files (*.wem)|*.wem|All files (*.*)|*.*";
            dialog.Multiselect = true;
            var dialogResult = dialog.ShowDialog();
            Defs.LogAction($"ReplaceWemData_Click: File dialog result: {dialogResult}");
            if (dialogResult != true)
            {
                Defs.LogAction("ReplaceWemData_Click: File dialog cancelled");
                return;
            }

            var wemFiles = dialog.FileNames;
            Defs.LogAction($"ReplaceWemData_Click: wemFiles.Length = {wemFiles.Length}");
            if (wemFiles.Length == 0)
            {
                Defs.LogAction("ReplaceWemData_Click: No files selected in dialog");
                return;
            }

            try
            {
                var allWemNodes = GetAllNodes().Where(n => n.NodeType == NodeType.WemFile).ToList();
                if (wemFiles.Length == 1)
                {
                    // Single file: use for all selected nodes (and all nodes with the same WEM ID)
                    var wemBytes = File.ReadAllBytes(wemFiles[0]);
                    Defs.LogAction($"ReplaceWemData_Click: Using single file {wemFiles[0]} for all selected nodes (file size: {wemBytes.Length} bytes)");
                    foreach (var selectedNode in selectedWemNodes)
                    {
                        // Find all nodes with the same WEM ID
                        foreach (var node in allWemNodes.Where(n => n.Name == selectedNode.Name))
                        {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                            Defs.LogAction($"ReplaceWemData_Click: Before replacement - Node {node.Name}: Data length = {node.WemData.Data.Length}, Length = {node.WemData.Length}");
#pragma warning restore CS8602
                            var newData = new byte[wemBytes.Length];
                            Array.Copy(wemBytes, newData, wemBytes.Length);
                            node.WemData.Data = newData;
                            node.WemData.Length = (uint)wemBytes.Length;
                            node.ReplacementFileName = System.IO.Path.GetFileName(wemFiles[0]);
                            Defs.LogAction($"Set ReplacementFileName for node {node.Name} to {node.ReplacementFileName}");
                            Defs.LogAction($"Replaced WEM node {node.Name} (ID: {node.WemData.Id}) with {wemFiles[0]} - New data length: {node.WemData.Data.Length} bytes");
                            Defs.LogAction($"ReplaceWemData_Click: After replacement - Node {node.Name}: Data length = {node.WemData.Data.Length}, Length = {node.WemData.Length}");
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
                    Defs.LogAction($"ReplaceWemData_Click: wemFileMap keys: {string.Join(", ", wemFileMap.Keys)}");
                    foreach (var selectedNode in selectedWemNodes)
                    {
                        if (wemFileMap.TryGetValue(selectedNode.Name, out var filePath))
                        {
                            var wemBytes = File.ReadAllBytes(filePath);
                            foreach (var node in allWemNodes.Where(n => n.Name == selectedNode.Name))
                            {
#pragma warning disable CS8602 // Dereference of a possibly null reference
                                Defs.LogAction($"ReplaceWemData_Click: Before replacement - Node {node.Name}: Data length = {node.WemData.Data.Length}, Length = {node.WemData.Length}");
#pragma warning restore CS8602
                                var newData = new byte[wemBytes.Length];
                                Array.Copy(wemBytes, newData, wemBytes.Length);
                                node.WemData.Data = newData;
                                node.WemData.Length = (uint)wemBytes.Length;
                                node.ReplacementFileName = System.IO.Path.GetFileName(filePath);
                                Defs.LogAction($"Set ReplacementFileName for node {node.Name} to {node.ReplacementFileName}");
#pragma warning disable CS8602 // Dereference of a possibly null reference
                                Defs.LogAction($"ReplaceWemData_Click: After replacement - Node {node.Name}: Data length = {node.WemData.Data.Length}, Length = {node.WemData.Length}");
#pragma warning restore CS8602
                                RefreshTreeNodeUI(node);
                            }
                            replaced++;
                        }
                        else
                        {
                            notMatched++;
#pragma warning disable CS8602 // Dereference of a possibly null reference
                            Defs.LogAction($"No matching file for WEM node {selectedNode.Name} (ID: {selectedNode.WemData.Id})");
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
                            Defs.LogAction($"ReplaceWemData_Click: Cleared cache for WEM ID {wemId}");
                        }
                    }
                }

                // After all replacements, force full TreeView UI refresh
                // No longer needed: TreeView.ItemsSource = null; TreeView.ItemsSource = currentItems;
                
                // Verify data integrity after replacement
                Defs.LogAction("ReplaceWemData_Click: Verifying data integrity after replacement...");
                VerifyWemDataIntegrity();
            }
            catch (Exception ex)
            {
                Defs.LogAction($"ReplaceWemData_Click: Exception: {ex.Message}");
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
                Defs.LogAction("VerifyWemDataIntegrity: Starting verification...");
                if (_rootNodes.Count == 0 || _rootNodes[0]?.Children.Count == 0)
                {
                    Defs.LogAction("VerifyWemDataIntegrity: No data to verify");
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
                                Defs.LogAction($"VerifyWemDataIntegrity: WARNING - Node {wemNode.Name} has inconsistent lengths: Data.Length={wemNode.WemData.Data.Length}, Length={wemNode.WemData.Length}");
                            }
                        }
                    }
                }

                Defs.LogAction($"VerifyWemDataIntegrity: Summary - Total WEM nodes: {totalWemNodes}, With data: {nodesWithData}, With replacements: {nodesWithReplacements}");
                
                if (replacementDetails.Count > 0)
                {
                    Defs.LogAction("VerifyWemDataIntegrity: Replacement details:");
                    foreach (var detail in replacementDetails)
                    {
                        Defs.LogAction($"  {detail}");
                    }
                }
            }
            catch (Exception ex)
            {
                Defs.LogAction($"VerifyWemDataIntegrity: Exception: {ex.Message}");
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
                Defs.LogAction("SaveAsBnkWpk_Click: Invoked");
                
                // Check if root node is selected
                if (!IsRootNodeSelected())
                {
                    System.Windows.MessageBox.Show("Please select the root file item (WPK file) to save.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    Defs.LogAction("SaveAsBnkWpk_Click: Root node not selected");
                    return;
                }
                
                // Verify data integrity before saving
                VerifyWemDataIntegrity();
                
#pragma warning disable CS8602 // Dereference of a possibly null reference
                if (_rootNodes.Count == 0 || _rootNodes[0].Children.Count == 0)
#pragma warning restore CS8602
                {
                    System.Windows.MessageBox.Show("No data to save. Please parse files first.", "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Information);
                    Defs.LogAction("SaveAsBnkWpk_Click: No data to save");
                    return;
                }
                
                // Enhanced logging for debugging
                Defs.LogAction($"SaveAsBnkWpk_Click: Root nodes count: {_rootNodes.Count}");
#pragma warning disable CS8602 // Dereference of a possibly null reference
                Defs.LogAction($"SaveAsBnkWpk_Click: First root children count: {_rootNodes[0].Children.Count}");
#pragma warning restore CS8602
                
                // Start with ALL WEM files from the original WPK to preserve unreferenced files
                List<AudioData> wemDataList;

                // Declare these at method scope so they're accessible for slot-preserving write
                var wemNodes = new List<TreeNode>();
                var deletedWemIds = new HashSet<uint>();
                var newWemFiles = new List<AudioData>();

                if (_currentWpkWemFiles != null && _currentWpkWemFiles.Count > 0)
                {
                    Defs.LogAction($"SaveAsBnkWpk_Click: Starting with {_currentWpkWemFiles.Count} WEM files from original WPK");

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
                                    Defs.LogAction($"SaveAsBnkWpk_Click: WEM {child.Name} (ID: {child.WemData.Id}) marked for deletion");
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
                                        Defs.LogAction($"SaveAsBnkWpk_Click: WEM {child.Name} (ID: {child.WemData.Id}) has replacement: {child.ReplacementFileName}");
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
                            Defs.LogAction($"SaveAsBnkWpk_Click: Found new WEM {wemNode.Name} (ID: {wemNode.WemData.Id})");
                        }
                    }

                    // DON'T remove deleted WEMs from wemDataById - the slot-preserving method handles them
                    // Just log what will be deleted
                    foreach (var deletedId in deletedWemIds)
                    {
                        Defs.LogAction($"SaveAsBnkWpk_Click: WEM ID {deletedId} will become empty slot");
                    }

                    // Convert to list for legacy compatibility (but we'll use slot-preserving method)
                    wemDataList = wemDataById.Values.Where(w => !deletedWemIds.Contains(w.Id)).ToList();

                Defs.LogAction($"SaveAsBnkWpk_Click: Final counts - Active: {wemDataList.Count}, Original slots: {_currentWemInformation!.OriginalSlotCount}, Deleted: {deletedWemIds.Count}, New: {newWemFiles.Count}");
                ScenarioLogger.LogSection("Save WPK");
                ScenarioLogger.LogInfo($"Active={wemDataList.Count}, Deleted={deletedWemIds.Count}, New={newWemFiles.Count}, OriginalSlots={_currentWemInformation!.OriginalSlotCount}");
                }
                else
                {
                    // Fallback to old behavior if _currentWpkWemFiles is not available
                    Defs.LogAction("SaveAsBnkWpk_Click: WARNING - _currentWpkWemFiles is null, falling back to tree-only collection");
                    System.Windows.MessageBox.Show("No original WPK data available. Cannot save WPK file.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Log details about the data being saved
                for (int i = 0; i < Math.Min(10, wemDataList.Count); i++)
                {
                    var wemData = wemDataList[i];
                    Defs.LogAction($"SaveAsBnkWpk_Click: Data[{i}]: ID={wemData.Id}, Length={wemData.Length}");
                }

                if (wemDataList.Count > 10)
                {
                    Defs.LogAction($"SaveAsBnkWpk_Click: ... and {wemDataList.Count - 10} more items");
                }
                
                // Prompt for file name
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.Filter = "WPK files (*.wpk)|*.wpk|All files (*.*)|*.*";
                dialog.Title = "Save as WPK";
                if (dialog.ShowDialog() != true)
                {
                    Defs.LogAction("SaveAsBnkWpk_Click: Save dialog cancelled");
                    return;
                }
                string outputPath = dialog.FileName;
                Defs.LogAction($"SaveAsBnkWpk_Click: Saving to {outputPath}");
                
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

                        Defs.LogAction($"SaveAsBnkWpk_Click: Using slot-preserving write method");
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
                        Defs.LogAction($"SaveAsBnkWpk_Click: Using legacy write method (no slot info)");
                        Utility.WriteWpkFile(wemDataList, outputPath);
                    }
                    Defs.LogAction($"SaveAsBnkWpk_Click: WriteWpkFile completed successfully");
                }
                catch (Exception writeEx)
                {
                    Defs.LogAction($"SaveAsBnkWpk_Click: WriteWpkFile failed: {writeEx.Message}");
                    throw;
                }

                // For WPK save, log size and optionally re-parse WPK for sanity
                try
                {
                    var info = new FileInfo(outputPath);
                    BnkWpkLogger.LogInfo($"SaveAsBnkWpk_Click: WPK saved, size: {info.Length} bytes");
                    ScenarioLogger.LogFileSize("Saved WPK", outputPath);
                    var parsed = WpkExtractor.ParseWpkFile(outputPath, new List<StringHash>(), null);
                    if (parsed == null)
                    {
                        BnkWpkLogger.LogWarning($"SaveAsBnkWpk_Click: Re-parse of saved WPK failed");
                        ScenarioLogger.LogWarning("WPK re-parse failed");
                    }
                    else
                    {
                        BnkWpkLogger.LogInfo($"SaveAsBnkWpk_Click: Re-parse OK: slots={parsed.OriginalSlotCount}, files={parsed.SortedWemDataList.Count}");
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
                            ScenarioLogger.LogInfo($"WPK offsets: original={originalCount}, saved={currentCount}, mismatches={mismatches}, zero_offsets_in_saved={zerosInCurrent}");
                            if (currentCount > originalCount)
                            {
                                var appendedOffset = parsed.OriginalOffsets[originalCount];
                                ScenarioLogger.LogInfo($"Appended slot offset: {appendedOffset}");
                            }
                        }
                    }
                }
                catch (Exception validateEx)
                {
                    BnkWpkLogger.LogWarning($"SaveAsBnkWpk_Click: WPK re-parse threw: {validateEx.Message}");
                    ScenarioLogger.LogWarning($"WPK re-parse threw: {validateEx.Message}");
                    ScenarioLogger.LogWarning(validateEx.StackTrace ?? "<no stack>");
                }

                // Verify the saved file
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    Defs.LogAction($"SaveAsBnkWpk_Click: Saved file exists, size: {fileInfo.Length} bytes");
                }
                else
                {
                    Defs.LogAction($"SaveAsBnkWpk_Click: WARNING - Saved file does not exist!");
                }
                
                System.Windows.MessageBox.Show($"Saved {wemDataList.Count} WEM(s) to:\n{outputPath}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                Defs.LogAction($"SaveAsBnkWpk_Click: Successfully saved {wemDataList.Count} WEM(s) to {outputPath}");
                ScenarioLogger.LogInfo($"Saved WPK to {outputPath}");
                ScenarioLogger.EndScenario(true);
            }
            catch (Exception ex)
            {
                Defs.LogAction($"SaveAsBnkWpk_Click: Exception: {ex.Message}\n{ex.StackTrace}");
                ScenarioLogger.LogError($"Save WPK failed: {ex.Message}");
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
            BnkWpkLogger.LogOperationStart("Add WEM File");
            try
            {
                BnkWpkLogger.LogInfo("AddWemFile_Click: Invoked");
                ScenarioLogger.LogSection("Add WEM");
                
                // Check if an event or container is selected
                var selectedNode = TreeView.SelectedItem as TreeNode;
                if (selectedNode == null || (selectedNode.NodeType != NodeType.Event && selectedNode.NodeType != NodeType.Container))
                {
                    System.Windows.MessageBox.Show("Please select an event or container to add WEM files to.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    BnkWpkLogger.LogWarning("AddWemFile_Click: No event or container selected");
                    BnkWpkLogger.LogOperationEnd("Add WEM File", false);
                    return;
                }

                // If a container is selected, we need to find its parent event
                TreeNode eventNode = selectedNode.NodeType == NodeType.Event ? selectedNode : selectedNode.Parent;
                if (eventNode == null || eventNode.NodeType != NodeType.Event)
                {
                    System.Windows.MessageBox.Show("Could not find parent event for selected container.", "Invalid Selection", MessageBoxButton.OK, MessageBoxImage.Error);
                    BnkWpkLogger.LogError("AddWemFile_Click: Selected container has no parent event");
                    BnkWpkLogger.LogOperationEnd("Add WEM File", false);
                    return;
                }
                
                // Check if we have BNK structure in memory
                if (_currentBnkSections == null)
                {
                    System.Windows.MessageBox.Show("No BNK structure loaded. Please parse files first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    BnkWpkLogger.LogWarning("AddWemFile_Click: No BNK structure in memory");
                    BnkWpkLogger.LogOperationEnd("Add WEM File", false);
                    return;
                }
                
                BnkWpkLogger.LogInfo($"AddWemFile_Click: Adding WEM to event: {eventNode.Name}");
                
                // Open file dialog to select WEM file
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "WEM files (*.wem)|*.wem|All files (*.*)|*.*";
                dialog.Title = "Select WEM file to add";
                if (dialog.ShowDialog() != true)
                {
                    BnkWpkLogger.LogWarning("AddWemFile_Click: File selection cancelled");
                    BnkWpkLogger.LogOperationEnd("Add WEM File", false);
                    return;
                }
                
                string wemFilePath = dialog.FileName;
                BnkWpkLogger.LogInfo($"AddWemFile_Click: Selected WEM file: {wemFilePath}");
                
                // Read the WEM file
                byte[] wemData = File.ReadAllBytes(wemFilePath);
                BnkWpkLogger.LogInfo($"AddWemFile_Click: Read WEM file, size: {wemData.Length} bytes");
                
                // Derive WEM ID from filename to keep BNK/WPK consistent with tools
                uint newWemId = 0;
                var fileName = Path.GetFileName(wemFilePath);
                var baseName = fileName.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - 4)
                    : fileName;
                if (!uint.TryParse(baseName, out newWemId))
                {
                    newWemId = GetNextAvailableWemId();
                    BnkWpkLogger.LogWarning($"AddWemFile_Click: Could not parse WEM ID from filename '{fileName}', falling back to next available ID {newWemId}");
                }
                else
                {
                    BnkWpkLogger.LogInfo($"AddWemFile_Click: Parsed WEM ID from filename: {newWemId}");
                }
                
                // Create AudioData for the new WEM
                var newWemAudioData = new AudioData
                {
                    Id = newWemId,
                    Length = (uint)wemData.Length,
                    Data = wemData
                };
                
                // Add to WPK using slot-preserving write for game compatibility
                if (_currentWpkPath != null && _currentWemInformation != null)
                {
                    try
                    {
                        // Build active WEM map from current WPK state
                        var wemDataByIdForWrite = new Dictionary<uint, AudioData>();
                        if (_currentWpkWemFiles != null)
                        {
                            foreach (var wem in _currentWpkWemFiles)
                            {
                                wemDataByIdForWrite[wem.Id] = wem;
                            }
                        }

                        // No deletions during append
                        var deletedWemIds = new HashSet<uint>();
                        var newWems = new List<AudioData> { newWemAudioData };

                        BnkWpkLogger.LogInfo("AddWemFile_Click: Writing WPK with slot preservation (append new slot)");
                        Utility.WriteWpkFilePreservingSlots(
                            wemDataByIdForWrite,
                            _currentWemInformation.OriginalSlotCount,
                            _currentWemInformation.WemIdToSlotIndex,
                            deletedWemIds,
                            newWems,
                            _currentWpkPath,
                            _currentWemInformation.OriginalOffsets,
                            _currentWemInformation.SlotIndexToEntry
                        );

                        // Update in-memory slot mapping to include the new WEM at the appended slot
                        int newSlotIndex = _currentWemInformation.OriginalSlotCount;
                        _currentWemInformation.WemIdToSlotIndex[newWemAudioData.Id] = newSlotIndex;
                        _currentWemInformation.OriginalSlotCount++;
                        BnkWpkLogger.LogInfo($"AddWemFile_Click: Updated slot mapping - WEM {newWemAudioData.Id} -> slot {newSlotIndex}");
                    }
                    catch (Exception wpkEx)
                    {
                        System.Windows.MessageBox.Show($"Failed to update WPK file: {wpkEx.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        BnkWpkLogger.LogError($"AddWemFile_Click: Failed to write WPK with slot preservation: {wpkEx.Message}");
                        BnkWpkLogger.LogOperationEnd("Add WEM File", false);
                        return;
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
                ScenarioLogger.LogInfo($"Target event: {eventNode.Name}, container: {(preferredContainerId.HasValue ? preferredContainerId.Value.ToString() : "none")}");
                ScenarioLogger.LogInfo($"WEM ID: {newWemId}, size: {wemData.Length} bytes");
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
                BnkWpkLogger.LogInfo($"AddWemFile_Click: Successfully added WEM {newWemId} to {targetDescription}");
                ScenarioLogger.LogInfo($"Added WEM {newWemId} to {targetDescription}");
                BnkWpkLogger.LogOperationEnd("Add WEM File", true);
                System.Windows.MessageBox.Show($"Successfully added WEM file (ID: {newWemId}) to {targetDescription}.\n\nRemember to save the BNK file to persist the changes.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                BnkWpkLogger.LogError($"AddWemFile_Click: Exception: {ex.Message}");
                ScenarioLogger.LogError($"Add WEM failed: {ex.Message}");
                BnkWpkLogger.LogOperationEnd("Add WEM File", false);
                System.Windows.MessageBox.Show($"Error adding WEM file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveWemButton_Click(object sender, RoutedEventArgs e)
        {
            BnkWpkLogger.LogOperationStart("Remove WEM File");
            try
            {
                BnkWpkLogger.LogInfo("RemoveWemButton_Click: Invoked");

                // Get selected items (checkboxes)
                var selectedItems = GetSelectedItems();
                if (selectedItems.Count == 0)
                {
                    System.Windows.MessageBox.Show("Please select at least one WEM file to remove using the checkboxes.\n\nTip: Check the boxes next to the WEM files (or entire events) you want to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    BnkWpkLogger.LogWarning("RemoveWemButton_Click: No WEM files selected");
                    BnkWpkLogger.LogOperationEnd("Remove WEM File", false);
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
                    BnkWpkLogger.LogWarning("RemoveWemButton_Click: No WEM files in selection");
                    BnkWpkLogger.LogOperationEnd("Remove WEM File", false);
                    return;
                }

                BnkWpkLogger.LogInfo($"RemoveWemButton_Click: {wemNodesToRemove.Count} WEM file(s) selected for removal");

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
                    BnkWpkLogger.LogInfo("RemoveWemButton_Click: User cancelled removal");
                    BnkWpkLogger.LogOperationEnd("Remove WEM File", false);
                    return;
                }

                // Mark WEM files for deletion
                int markedCount = 0;
                foreach (var wemNode in wemNodesToRemove)
                {
                    wemNode.IsMarkedForDeletion = true;
                    wemNode.IsChecked = false; // Uncheck the checkbox
                    BnkWpkLogger.LogInfo($"RemoveWemButton_Click: Marked WEM {wemNode.Name} (ID: {wemNode.WemData?.Id}) for deletion");
                    markedCount++;
                }

                BnkWpkLogger.LogInfo($"RemoveWemButton_Click: Successfully marked {markedCount} WEM file(s) for deletion");
                BnkWpkLogger.LogOperationEnd("Remove WEM File", true);

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
                BnkWpkLogger.LogError($"RemoveWemButton_Click: Exception: {ex.Message}");
                BnkWpkLogger.LogOperationEnd("Remove WEM File", false);
                System.Windows.MessageBox.Show($"Error removing WEM file(s):\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveBnkFile_Click(object sender, RoutedEventArgs e)
        {
            BnkWpkLogger.LogOperationStart("Save BNK File");
            try
            {
                BnkWpkLogger.LogInfo("SaveBnkFile_Click: Invoked");
                ScenarioLogger.LogSection("Save BNK");
                
                // Check if we have BNK structure in memory
                if (_currentBnkSections == null)
                {
                    System.Windows.MessageBox.Show("No BNK structure loaded. Please parse files first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    BnkWpkLogger.LogWarning("SaveBnkFile_Click: No BNK structure in memory");
                    BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                    return;
                }
                
                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: BNK structure loaded - Events: {_currentBnkSections.Events.Count}, Sounds: {_currentBnkSections.Sounds.Count}, EventActions: {_currentBnkSections.EventActions.Count}");
                
                if (!ValidateEventContainerRouting())
                {
                    System.Windows.MessageBox.Show("Validation failed: Event → Container routing changed unexpectedly.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                    return;
                }

                if (!ValidateWemIdsAgainstWpk())
                {
                    System.Windows.MessageBox.Show("Validation failed: BNK references WEM IDs not present in WPK.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                    return;
                }

                // Prompt for save location
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.Filter = "BNK files (*.bnk)|*.bnk|All files (*.*)|*.*";
                dialog.Title = "Save BNK file";
                if (dialog.ShowDialog() != true)
                {
                    BnkWpkLogger.LogWarning("SaveBnkFile_Click: Save dialog cancelled");
                    BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                    return;
                }
                
                string outputPath = dialog.FileName;
                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: Saving BNK to: {outputPath}");
                
                // Validate _fullFidelityBnk
                if (_fullFidelityBnk == null || _fullFidelityBnk.AllObjectsInOrder == null || _fullFidelityBnk.AllObjectsInOrder.Count == 0)
                {
                    BnkWpkLogger.LogError("SaveBnkFile_Click: _fullFidelityBnk is null or empty - cannot serialize");
                    System.Windows.MessageBox.Show("Full-fidelity BNK data not loaded. Please parse files first.", "Missing Data", MessageBoxButton.OK, MessageBoxImage.Error);
                    BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                    return;
                }
                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: _fullFidelityBnk has {_fullFidelityBnk.AllObjectsInOrder.Count} objects");

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
                                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: WEM ID {wemNode.WemData.Id} marked for deletion");
                            }
                        }
                    }
                }
                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: Found {deletedWemIds.Count} WEM(s) marked for deletion");

                // Filter out objects related to deleted WEMs from full-fidelity structure
                if (deletedWemIds.Count > 0)
                {
                    var filteredBnk = FilterDeletedWemsFromFullFidelity(_fullFidelityBnk, deletedWemIds);
                    BnkWpkLogger.LogInfo($"SaveBnkFile_Click: After filtering, {filteredBnk.AllObjectsInOrder.Count} objects remain (removed {_fullFidelityBnk.AllObjectsInOrder.Count - filteredBnk.AllObjectsInOrder.Count} objects)");

                    // Log modified BNK structure before saving for debugging
                    BnkDebugLogger.LogSection("MODIFIED BNK STRUCTURE (Before Save)");
                    LogBnkStructureToDebug(filteredBnk, "MODIFIED");

                    // Write the filtered BNK file from memory using full-fidelity serialization
                    bool writeSuccess = BnkWriter.WriteBnkFile_FullFidelity(filteredBnk, outputPath);
                    if (!writeSuccess)
                    {
                        BnkWpkLogger.LogError("SaveBnkFile_Click: WriteBnkFile_FullFidelity returned false");
                        System.Windows.MessageBox.Show("Failed to write BNK file. Check the log for details.", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                        return;
                    }
                }
                else
                {
                    // No deletions, write as-is
                    // Log modified BNK structure before saving for debugging
                    BnkDebugLogger.LogSection("MODIFIED BNK STRUCTURE (Before Save - No Deletions)");
                    LogBnkStructureToDebug(_fullFidelityBnk, "MODIFIED");

                    bool writeSuccess = BnkWriter.WriteBnkFile_FullFidelity(_fullFidelityBnk, outputPath);
                    if (!writeSuccess)
                    {
                        BnkWpkLogger.LogError("SaveBnkFile_Click: WriteBnkFile_FullFidelity returned false");
                        System.Windows.MessageBox.Show("Failed to write BNK file. Check the log for details.", "Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                        return;
                    }
                }

                // Verify the saved file
                if (File.Exists(outputPath))
                {
                var fileInfo = new FileInfo(outputPath);
                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: Saved file exists, size: {fileInfo.Length} bytes");
                ScenarioLogger.LogFileSize("Saved BNK", outputPath);
                    
                    if (fileInfo.Length == 0)
                    {
                        BnkWpkLogger.LogError("SaveBnkFile_Click: Saved file is 0 bytes - this indicates a serious problem");
                        System.Windows.MessageBox.Show("Saved file is 0 bytes. This indicates a serious problem with the save process.", "File Size Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                        return;
                    }
                }
                else
                {
                    BnkWpkLogger.LogError("SaveBnkFile_Click: Saved file does not exist after write operation");
                    System.Windows.MessageBox.Show("Saved file does not exist after write operation.", "File Creation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                    return;
                }
                
                BnkWpkLogger.LogInfo($"SaveBnkFile_Click: Successfully saved BNK file to: {outputPath}");
                
                // Validate the written file
                BnkWpkLogger.LogInfo("SaveBnkFile_Click: Validating written BNK file...");
                bool validationSuccess = BnkWriter.ValidateWrittenBnkFile(outputPath);
                if (!validationSuccess)
                {
                    BnkWpkLogger.LogWarning("SaveBnkFile_Click: BNK file validation failed - the file may not be compatible with tools/game");
                    System.Windows.MessageBox.Show("BNK file was saved but validation failed. The file may not be compatible with tools or the game.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    BnkWpkLogger.LogInfo("SaveBnkFile_Click: BNK file validation successful");
                    ScenarioLogger.LogInfo("BNK validation successful");
                }
                
                BnkWpkLogger.LogOperationEnd("Save BNK File", true);
                ScenarioLogger.EndScenario(true);
                System.Windows.MessageBox.Show($"Successfully saved BNK file to:\n{outputPath}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                BnkWpkLogger.LogError($"SaveBnkFile_Click: Exception: {ex.Message}");
                BnkWpkLogger.LogError($"SaveBnkFile_Click: Stack trace: {ex.StackTrace}");
                BnkWpkLogger.LogOperationEnd("Save BNK File", false);
                ScenarioLogger.LogError($"Save BNK failed: {ex.Message}");
                ScenarioLogger.EndScenario(false);
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
                    if (!wemIds.Contains(s.FileId))
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
            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: Filtering {deletedWemIds.Count} deleted WEM IDs");

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
                            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: Sound ID {soundObj.SelfId} references deleted WEM {soundObj.FileId} - marking for removal");
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
                                    BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: Sound ID {selfId} references deleted WEM {fileId} - marking for removal (from raw data)");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            BnkWpkLogger.LogWarning($"FilterDeletedWemsFromFullFidelity: Failed to parse Sound object from raw data: {ex.Message}");
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
                        BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: RandomContainer ID {randomContainer.SelfId} had {originalSoundCount} sounds, now has {randomContainer.SoundIds.Count}");

                        // If RandomContainer is now empty, mark it for deletion
                        if (randomContainer.SoundIds.Count == 0)
                        {
                            deletedRandomContainerIds.Add(randomContainer.SelfId);
                            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: RandomContainer ID {randomContainer.SelfId} is now empty - marking for removal");
                        }
                        else
                        {
                            // Update the RandomContainer
                            randomContainer.SoundIdAmount = (uint)randomContainer.Playlist.Count;

                            // Mark the RandomContainer as changed - it will be serialized by BnkWriter.SerializeObject()
                            obj.IsChanged = true;

                            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: Updated RandomContainer ID {randomContainer.SelfId} - removed {originalSoundCount - randomContainer.SoundIds.Count} Sound reference(s), marked as changed for serialization");
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
                            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: EventAction ID {eventActionObj.SelfId} references deleted object {eventActionObj.SoundObjectId} - marking for removal");
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
                                    BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: EventAction ID {selfId} references deleted Sound {targetId} - marking for removal (from raw data)");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            BnkWpkLogger.LogWarning($"FilterDeletedWemsFromFullFidelity: Failed to parse EventAction object from raw data: {ex.Message}");
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

                            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: Updated Event ID {eventObj.SelfId} - removed {originalCount - eventObj.EventIds.Count} EventAction reference(s)");
                        }
                    }

                    filteredBnk.AllObjectsInOrder.Add(obj);
                }
                else
                {
                    removedCount++;
                }
            }

            BnkWpkLogger.LogInfo($"FilterDeletedWemsFromFullFidelity: Removed {removedCount} objects from BNK structure");
            return filteredBnk;
        }

        private void AddWemToBnkStructure_FullFidelity(string eventName, uint wemId, uint? preferredContainerId = null)
        {
            // Only new/modified objects are marked as IsChanged = true.
            // All other objects retain their original RawData and IsChanged = false.
            // This ensures the output BNK is byte-for-byte identical to the original except for the new WEM and its references.
            BnkLogger.LogInfo("[AddWemToBnkStructure_FullFidelity] Only new/modified objects are marked as changed. All others are preserved exactly.");
            try
            {
                BnkLogger.LogInfo($"AddWemToBnkStructure: Adding WEM {wemId} to event '{eventName}'");

                if (_fullFidelityBnk == null || _fullFidelityBnk.AllObjectsInOrder == null)
                {
                    BnkLogger.LogError("AddWemToBnkStructure: No full-fidelity BNK structure available");
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
                    BnkLogger.LogError($"AddWemToBnkStructure: Could not find event for name '{eventName}'");
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
                    BnkLogger.LogError($"AddWemToBnkStructure: Could not find event object with ID {targetEventId}");
                    return;
                }
                var eventObj = targetEvent.ParsedObject as Event;
                if (eventObj == null)
                {
                    BnkLogger.LogError("AddWemToBnkStructure: Target event object is null");
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
                    BnkLogger.LogError("AddWemToBnkStructure: Event has no EventAction references");
                    return;
                }

                // Find the EventAction to see what it points to
                var eventActionObj = _fullFidelityBnk.AllObjectsInOrder
                    .FirstOrDefault(o => o.Type == 3 && o.ParsedObject is EventAction ea && ea.SelfId == eventActionId);

                if (eventActionObj == null)
                {
                    BnkLogger.LogError($"AddWemToBnkStructure: Could not find EventAction {eventActionId}");
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
                    BnkLogger.LogInfo($"AddWemToBnkStructure: Event uses RandomContainer {targetObjectId} - adding Sound to container");

                    uint newSoundId = GenerateNewObjectId();

                    // Clone a template Sound
                    var templateSoundObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 2 && o.RawData != null);
                    if (templateSoundObj == null)
                    {
                        BnkLogger.LogError("AddWemToBnkStructure: No template Sound found");
                        return;
                    }
                    byte[] soundRaw = (byte[])templateSoundObj.RawData.Clone();

                    // Helper function to patch UInt32 values in binary data
                    void PatchUInt32(byte[] arr, uint oldValue, uint newValue)
                    {
                        var oldBytes = BitConverter.GetBytes(oldValue);
                        var newBytes = BitConverter.GetBytes(newValue);
                        for (int i = 0; i < arr.Length - 3; i++)
                        {
                            if (arr[i] == oldBytes[0] && arr[i + 1] == oldBytes[1] && arr[i + 2] == oldBytes[2] && arr[i + 3] == oldBytes[3])
                            {
                                arr[i] = newBytes[0];
                                arr[i + 1] = newBytes[1];
                                arr[i + 2] = newBytes[2];
                                arr[i + 3] = newBytes[3];
                            }
                        }
                    }

                    // Patch the Sound object
                    var templateSound = templateSoundObj.ParsedObject as Sound;
                    if (templateSound == null)
                    {
                        BnkLogger.LogError("AddWemToBnkStructure: Template Sound object is null");
                        return;
                    }

                    // Use the parsed StreamFileID from the template Sound object
                    // The hardcoded offset calculation was incorrect - use the already-parsed value instead
                    uint templateStreamFileID = templateSound.StreamFileID;

                    BnkLogger.LogInfo($"AddWemToBnkStructure: Template Sound - SelfId={templateSound.SelfId}, FileId={templateSound.FileId}, StreamFileID={templateStreamFileID}");
                    BnkLogger.LogInfo($"AddWemToBnkStructure: Patching to - newSoundId={newSoundId}, wemId={wemId}");
                    PatchUInt32(soundRaw, templateSound.SelfId, newSoundId);
                    PatchUInt32(soundRaw, templateSound.FileId, wemId);
                    if (templateStreamFileID != 0)
                    {
                        PatchUInt32(soundRaw, templateStreamFileID, wemId);  // Patch StreamFileID
                    }

                    // Parse the new Sound object
                    var newSound = new Sound
                    {
                        SelfId = newSoundId,
                        Unknown1 = templateSound.Unknown1,
                        IsStreamed = templateSound.IsStreamed,
                        Unknown2 = templateSound.Unknown2,
                        Unknown3 = templateSound.Unknown3,
                        FileId = wemId,
                        StreamFileID = wemId,  // CRITICAL: Set StreamFileID for announcer/voice sounds
                        Reserved0 = templateSound.Reserved0,
                        StreamSourceType = templateSound.StreamSourceType,
                        StreamSourceID = templateSound.StreamSourceID,
                        SoundType = templateSound.SoundType,
                        SoundStructure = templateSound.SoundStructure,
                        RawData = soundRaw
                    };

                    // Add the new Sound object
                    _fullFidelityBnk.AllObjectsInOrder.Add(new BnkObjectRecord
                    {
                        Type = 2,
                        Length = (uint)soundRaw.Length - 5, // Length doesn't include type byte and length field
                        RawData = soundRaw,
                        ParsedObject = newSound,
                        IsChanged = false,
                        ObjectId = newSoundId
                    });

                    // Update the RandomContainer to include the new Sound (preserve EventAction → Container)
                    var randomContainer = randomContainerObj.ParsedObject as RandomContainer;

                    // Use weight/rsGroupID from first existing entry for consistency
                    uint defaultWeight = randomContainer.Playlist.Count > 0 ? randomContainer.Playlist[0].Weight : 1448017921u;
                    uint defaultRsGroupID = randomContainer.Playlist.Count > 0 ? randomContainer.Playlist[0].RsGroupID : 3276806263u;
                    var newPlaylistEntry = new PlaylistEntry { PlayID = newSoundId, Weight = defaultWeight, RsGroupID = defaultRsGroupID };

                    randomContainer.Playlist.Add(newPlaylistEntry);
                    randomContainer.SoundIds.Add(newSoundId);
                    randomContainer.SoundIdAmount = (uint)randomContainer.Playlist.Count;
                    randomContainerObj.IsChanged = true;

                    BnkWpkLogger.LogInfo($"AddWemToBnkStructure: Added Sound {newSoundId} to RandomContainer {randomContainer.SelfId} (weight={defaultWeight}, rsGroupID={defaultRsGroupID})");
                }
                else
                {
                    // No RandomContainer - use the old direct EventAction → Sound approach
                    BnkLogger.LogInfo("AddWemToBnkStructure: Event uses direct Sound reference - creating new EventAction");

                    uint newSoundId = GenerateNewObjectId();
                    uint newEventActionId = GenerateNewObjectId();

                    var templateSoundObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 2 && o.RawData != null);
                    var templateEventActionObj = _fullFidelityBnk.AllObjectsInOrder.FirstOrDefault(o => o.Type == 3 && o.RawData != null);
                    if (templateSoundObj == null || templateEventActionObj == null)
                    {
                        BnkLogger.LogError("AddWemToBnkStructure: No template Sound or EventAction found");
                        return;
                    }

                    byte[] soundRaw = (byte[])templateSoundObj.RawData.Clone();
                    byte[] eventActionRaw = (byte[])templateEventActionObj.RawData.Clone();

                    void PatchUInt32(byte[] arr, uint oldValue, uint newValue)
                    {
                        var oldBytes = BitConverter.GetBytes(oldValue);
                        var newBytes = BitConverter.GetBytes(newValue);
                        for (int i = 0; i < arr.Length - 3; i++)
                        {
                            if (arr[i] == oldBytes[0] && arr[i + 1] == oldBytes[1] && arr[i + 2] == oldBytes[2] && arr[i + 3] == oldBytes[3])
                            {
                                arr[i] = newBytes[0];
                                arr[i + 1] = newBytes[1];
                                arr[i + 2] = newBytes[2];
                                arr[i + 3] = newBytes[3];
                            }
                        }
                    }

                    var templateSound = templateSoundObj.ParsedObject as Sound;
                    var templateEventAction = templateEventActionObj.ParsedObject as EventAction;
                    if (templateSound == null || templateEventAction == null)
                    {
                        BnkLogger.LogError("AddWemToBnkStructure: Template objects are null");
                        return;
                    }

                    // Use the parsed StreamFileID from the template Sound object
                    // The hardcoded offset calculation was incorrect - use the already-parsed value instead
                    uint templateStreamFileID = templateSound.StreamFileID;

                    BnkLogger.LogInfo($"AddWemToBnkStructure (Event path): Template Sound - SelfId={templateSound.SelfId}, FileId={templateSound.FileId}, StreamFileID={templateStreamFileID}");
                    BnkLogger.LogInfo($"AddWemToBnkStructure (Event path): Patching to - newSoundId={newSoundId}, wemId={wemId}");
                    PatchUInt32(soundRaw, templateSound.SelfId, newSoundId);
                    PatchUInt32(soundRaw, templateSound.FileId, wemId);
                    if (templateStreamFileID != 0)
                    {
                        PatchUInt32(soundRaw, templateStreamFileID, wemId);  // Patch StreamFileID
                    }
                    PatchUInt32(eventActionRaw, templateEventAction.SelfId, newEventActionId);
                    PatchUInt32(eventActionRaw, templateEventAction.SoundObjectId, newSoundId);

                    // Parse the new Sound object
                    var newSound = new Sound
                    {
                        SelfId = newSoundId,
                        Unknown1 = templateSound.Unknown1,
                        IsStreamed = templateSound.IsStreamed,
                        Unknown2 = templateSound.Unknown2,
                        Unknown3 = templateSound.Unknown3,
                        FileId = wemId,
                        StreamFileID = wemId,  // CRITICAL: Set StreamFileID for announcer/voice sounds
                        Reserved0 = templateSound.Reserved0,
                        StreamSourceType = templateSound.StreamSourceType,
                        StreamSourceID = templateSound.StreamSourceID,
                        SoundType = templateSound.SoundType,
                        SoundStructure = templateSound.SoundStructure,
                        RawData = soundRaw
                    };

                    // Parse the new EventAction object
                    var newEventAction = new EventAction
                    {
                        SelfId = newEventActionId,
                        Scope = templateEventAction.Scope,
                        Type = templateEventAction.Type,
                        SoundObjectId = newSoundId,
                        SwitchGroupId = templateEventAction.SwitchGroupId,
                        RawData = eventActionRaw
                    };

                    _fullFidelityBnk.AllObjectsInOrder.Add(new BnkObjectRecord
                    {
                        Type = 2,
                        Length = (uint)soundRaw.Length - 5, // Length doesn't include type byte and length field
                        RawData = soundRaw,
                        ParsedObject = newSound,
                        IsChanged = false,
                        ObjectId = newSoundId
                    });
                    _fullFidelityBnk.AllObjectsInOrder.Add(new BnkObjectRecord
                    {
                        Type = 3,
                        Length = (uint)eventActionRaw.Length - 5, // Length doesn't include type byte and length field
                        RawData = eventActionRaw,
                        ParsedObject = newEventAction,
                        IsChanged = false,
                        ObjectId = newEventActionId
                    });

                    // Update the existing event to reference the new EventAction
                    eventObj.EventIds.Add(newEventActionId);
                    eventObj.EventAmount = (byte)eventObj.EventIds.Count;
                    targetEvent.IsChanged = true;

                    // Update the event's RawData
                    using (var ms = new MemoryStream())
                    using (var writer = new BinaryWriter(ms))
                    {
                        writer.Write((byte)4);
                        uint eventLength = (uint)(4 + 1 + eventObj.EventIds.Count * 4);
                        writer.Write(eventLength);
                        writer.Write(eventObj.SelfId);
                        writer.Write(eventObj.EventAmount);
                        foreach (var eventId in eventObj.EventIds)
                        {
                            writer.Write(eventId);
                        }
                        targetEvent.RawData = ms.ToArray();
                        targetEvent.Length = eventLength;
                    }
                }

                BnkLogger.LogInfo($"AddWemToBnkStructure: SUCCESS - WEM {wemId} added to event '{eventName}'");
                CompareObjectStructures();
            }
            catch (Exception ex)
            {
                BnkLogger.LogError($"AddWemToBnkStructure: Exception occurred: {ex.Message}");
                BnkLogger.LogError($"AddWemToBnkStructure: Stack trace: {ex.StackTrace}");
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

            BnkLogger.LogInfo($"GenerateNewObjectId: Generated Wwise-style ID: {newId}");
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
                BnkLogger.LogInfo("[CompareObjectStructures] Starting object structure comparison");
                
                if (_fullFidelityBnk?.AllObjectsInOrder == null)
                {
                    BnkLogger.LogError("[CompareObjectStructures] No full-fidelity structure available");
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
                
                BnkLogger.LogInfo($"[CompareObjectStructures] Object counts:");
                BnkLogger.LogInfo($"[CompareObjectStructures]   Original Sounds: {originalSounds.Count}");
                BnkLogger.LogInfo($"[CompareObjectStructures]   New Sounds: {newSounds.Count}");
                BnkLogger.LogInfo($"[CompareObjectStructures]   Original EventActions: {originalEventActions.Count}");
                BnkLogger.LogInfo($"[CompareObjectStructures]   New EventActions: {newEventActions.Count}");
                
                if (originalSounds.Count > 0 && newSounds.Count > 0)
                {
                    var templateSound = originalSounds.First();
                    var newSound = newSounds.First();
                    
                    BnkLogger.LogInfo($"[CompareObjectStructures] Sound structure comparison:");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   Template: SelfId={templateSound.SelfId}, FileId={templateSound.FileId}, Unknown1={templateSound.Unknown1}, IsStreamed={templateSound.IsStreamed}");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   Template Unknown2: Length={templateSound.Unknown2?.Length}, Data=[{BitConverter.ToString(templateSound.Unknown2 ?? new byte[0])}]");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   Template Unknown3: Length={templateSound.Unknown3?.Length}, Data=[{BitConverter.ToString(templateSound.Unknown3 ?? new byte[0])}]");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   New: SelfId={newSound.SelfId}, FileId={newSound.FileId}, Unknown1={newSound.Unknown1}, IsStreamed={newSound.IsStreamed}");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   New Unknown2: Length={newSound.Unknown2?.Length}, Data=[{BitConverter.ToString(newSound.Unknown2 ?? new byte[0])}]");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   New Unknown3: Length={newSound.Unknown3?.Length}, Data=[{BitConverter.ToString(newSound.Unknown3 ?? new byte[0])}]");
                }
                
                if (originalEventActions.Count > 0 && newEventActions.Count > 0)
                {
                    var templateEventAction = originalEventActions.First();
                    var newEventAction = newEventActions.First();
                    
                    BnkLogger.LogInfo($"[CompareObjectStructures] EventAction structure comparison:");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   Template: SelfId={templateEventAction.SelfId}, Scope={templateEventAction.Scope}, Type={templateEventAction.Type}, SoundObjectId={templateEventAction.SoundObjectId}, SwitchGroupId={templateEventAction.SwitchGroupId}");
                    BnkLogger.LogInfo($"[CompareObjectStructures]   New: SelfId={newEventAction.SelfId}, Scope={newEventAction.Scope}, Type={newEventAction.Type}, SoundObjectId={newEventAction.SoundObjectId}, SwitchGroupId={newEventAction.SwitchGroupId}");
                }
                
                BnkLogger.LogInfo("[CompareObjectStructures] Object structure comparison completed");
            }
            catch (Exception ex)
            {
                BnkLogger.LogError($"[CompareObjectStructures] Exception: {ex.Message}");
                BnkLogger.LogError($"[CompareObjectStructures] Stack trace: {ex.StackTrace}");
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
                    BnkLogger.LogInfo($"Exporting BNK structure to JSON: {saveFileDialog.FileName}");
                    
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
                    
                    BnkLogger.LogInfo($"Successfully exported BNK structure to JSON: {saveFileDialog.FileName}");
                    System.Windows.MessageBox.Show($"BNK structure exported successfully to:\n{saveFileDialog.FileName}", 
                        "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                BnkLogger.LogError($"Export BNK to JSON failed: {ex.Message}");
                BnkLogger.LogError($"Stack trace: {ex.StackTrace}");
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
                BnkLogger.LogWarning($"Failed to serialize parsed object of type {type}: {ex.Message}");
                return new { Error = $"Serialization failed: {ex.Message}" };
            }
        }

        private void LogBnkStructureToDebug(BnkFullFidelity bnk, string label)
        {
            try
            {
                BnkDebugLogger.Log($"=== {label} BNK STRUCTURE ===");
                BnkDebugLogger.Log($"BNK Version: {bnk.BnkVersion:X}");
                BnkDebugLogger.Log($"Total Objects: {bnk.AllObjectsInOrder.Count}");
                BnkDebugLogger.Log("");

                // Group objects by type for easier analysis
                var objectsByType = bnk.AllObjectsInOrder.GroupBy(o => o.Type).OrderBy(g => g.Key);

                foreach (var typeGroup in objectsByType)
                {
                    BnkDebugLogger.LogSection($"{GetObjectTypeName(typeGroup.Key)} Objects (Type {typeGroup.Key}) - Count: {typeGroup.Count()}");

                    foreach (var obj in typeGroup)
                    {
                        // Log basic object info
                        BnkDebugLogger.Log($"  Object ID: {obj.ObjectId}, Length: {obj.Length}, Changed: {obj.IsChanged}");

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
                        BnkDebugLogger.Log("");
                    }
                }
            }
            catch (Exception ex)
            {
                BnkDebugLogger.Log($"ERROR logging BNK structure: {ex.Message}");
                BnkDebugLogger.Log($"Stack trace: {ex.StackTrace}");
            }
        }

        private void LogEvent(string eventName, uint eventId, Event eventObj)
        {
            BnkDebugLogger.Log($"    Event Name: {eventName}");
            BnkDebugLogger.Log($"    Event ID: {eventId}");
            BnkDebugLogger.Log($"    EventAmount: {eventObj.EventAmount}");
            BnkDebugLogger.Log($"    EventAction IDs: [{string.Join(", ", eventObj.EventIds)}]");
        }

        private void LogEventAction(EventAction ea)
        {
            BnkDebugLogger.Log($"    EventAction ID: {ea.SelfId}");
            BnkDebugLogger.Log($"    Scope: {ea.Scope}");
            BnkDebugLogger.Log($"    Type: {ea.Type}");
            BnkDebugLogger.Log($"    SoundObjectId: {ea.SoundObjectId}");
        }

        private void LogRandomContainer(RandomContainer rc)
        {
            BnkDebugLogger.Log($"    RandomContainer ID: {rc.SelfId}");
            BnkDebugLogger.Log($"    SwitchContainerId: {rc.SwitchContainerId}");
            BnkDebugLogger.Log($"    SoundIdAmount: {rc.SoundIdAmount}");
            BnkDebugLogger.Log($"    Sound IDs: [{string.Join(", ", rc.SoundIds)}]");
        }

        private void LogSound(Sound s)
        {
            BnkDebugLogger.Log($"    Sound ID: {s.SelfId}");
            BnkDebugLogger.Log($"    FileId (WEM): {s.FileId}");
            BnkDebugLogger.Log($"    IsStreamed: {s.IsStreamed}");
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
                        BnkWpkLogger.LogError("RegenerateRandomContainerRawData: Could not find SoundIdAmount position");
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

                        BnkWpkLogger.LogInfo($"RegenerateRandomContainerRawData: Updated RandomContainer {randomContainer.SelfId} - " +
                            $"Old size: {originalRawData.Length} bytes, New size: {newRawData.Length} bytes, " +
                            $"Sound count: {randomContainer.SoundIds.Count}");

                        return newRawData;
                    }
                }
            }
            catch (Exception ex)
            {
                BnkWpkLogger.LogError($"RegenerateRandomContainerRawData: Failed to regenerate RawData: {ex.Message}");
                return originalRawData; // Return unchanged on error
            }
        }
    }
}
