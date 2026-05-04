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
                Logger.StartNewSession(); 
                Logger.LogSection("SOFTWARE STARTED");
                
                
                // Load settings on startup
                SettingsManager.LoadSettings();

                // Initialize Theme from saved settings
                ThemeManager.ApplySavedTheme();

                UpdateControlStates();
            }
            catch (Exception ex)
            {
                Logger.LogException("MainWindow Initialization", ex);
                Logger.LogInitError(ex.Message);
                throw; // Re-throw to let the global handler catch it
            }
        }


        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            Logger.LogSection("SOFTWARE CLOSED");
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
                UpdateControlStates();
            }
        }

        private void OpenWpkFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "WPK files (*.wpk)|*.wpk|All files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                WpkFilePath.Text = dialog.FileName;
                UpdateControlStates();
            }
        }

        private void OpenBnkFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "BNK files (*.bnk)|*.bnk|All files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                BnkFilePath.Text = dialog.FileName;
                UpdateControlStates();
            }
        }

        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow();
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
                Logger.LogSection("STARTING WWISE PROJECT RECONSTRUCTION");
                Logger.LogExportStart(projectFolder);

                if (!WwiseProjectExporter.ValidateWwiseProject(projectFolder, out string error))
                {
                    Logger.LogInvalidWwiseProject(error);
                    return;
                }

                var exporter = new WwiseProjectExporter();
                exporter.Export(
                    _currentBnkSections,
                    _eventNameLookup,
                    projectFolder
                );

                Logger.LogSection("RECONSTRUCTION COMPLETE");
                System.Windows.MessageBox.Show(
                    "Wwise Project generated successfully!\n\nYou can now open the project in Wwise.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.LogException("Wwise Export", ex);
                Logger.LogExportError(ex.Message);
            }
        }

        private void ThemeWin11_Click(object sender, RoutedEventArgs e) => ThemeManager.SetTheme(AppTheme.Windows11);
        private void ThemeWin10_Click(object sender, RoutedEventArgs e) => ThemeManager.SetTheme(AppTheme.Windows10);
        private void ThemeHextech_Click(object sender, RoutedEventArgs e) => ThemeManager.SetTheme(AppTheme.Hextech);
        private void ThemeLight_Click(object sender, RoutedEventArgs e) => ThemeManager.SetTheme(AppTheme.Light);
        private void ResetAppearance_Click(object sender, RoutedEventArgs e) => ThemeManager.ResetAppearance();

        private void CreditsMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CreditsWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        private void PickAccentColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = System.Windows.Media.Color.FromArgb(dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
                ThemeManager.SetAccentColor(color);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => this.Close();

        private async void ParseFilesButton_Click(object sender, RoutedEventArgs e)
        {
            // Logger.Clear();
            Logger.LogSection("STARTING NEW SESSION - PARSING FILES");
            try
            {
                string binPath = BinFilePath.Text;
                string wpkPath = WpkFilePath.Text;
                string bnkPath = BnkFilePath.Text;

                Logger.LogPaths(binPath, wpkPath, bnkPath);





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

                Logger.LogBnkLoadStart(bnkPath);
                try
                {
                    _originalBnkBytes = File.ReadAllBytes(bnkPath);
                    Logger.LogBnkLoaded(_originalBnkBytes.Length);
                }
                catch (Exception ex)
                {
                    Logger.LogException("Loading original BNK bytes", ex);
                    Logger.LogBnkLoadError(ex.Message);
                    return;
                }

                // Parse BIN for event name mapping
                Logger.LogBinParseStart();
                var stringHashes = BinParser.ParseBinFile(binPath);
                var eventNameLookup = new Dictionary<uint, string>();
                if (stringHashes != null)
                {
                    foreach (var sh in stringHashes)
                    {
                        eventNameLookup[sh.Hash] = sh.String;
                    }
                    Logger.LogBinParsed(stringHashes.Count);
                }
                else
                {
                    Logger.LogError("BIN parsing returned null");
                }

                // BNK sections - populated by ParseEventBnkFile_FullFidelity (single pass, no double-fill).
                var sections = new BNKSections();
                sections.SourceFileName = Path.GetFileNameWithoutExtension(bnkPath);

                // Store BNK structure in memory for WEM addition feature
                _currentBnkSections = sections;
                _currentBnkPath = bnkPath;
                _currentWpkPath = wpkPath;

                // Initialize full-fidelity BNK structure
                Logger.LogBnkParseStart();
                _fullFidelityBnk = new BnkFullFidelity();
                bool fullFidelityOk = BnkExtractor.ParseEventBnkFile_FullFidelity(bnkPath, _fullFidelityBnk, sections);
                if (!fullFidelityOk)
                {
                    Logger.LogError("Failed to parse BNK file in Full Fidelity mode");
                }
                else
                {
                    Logger.LogBnkParsed(_fullFidelityBnk.AllObjectsInOrder.Count);

                }

                // Build a set of BNK event IDs
                var bnkEventIds = new HashSet<uint>(sections.Events.Select(e => e.SelfId));


                // Parse WPK with stringHashes and bnkEventIds

                var wemInfo = WpkExtractor.ParseWpkFile(wpkPath, stringHashes ?? new List<StringHash>(), bnkEventIds);
                _currentWemInformation = wemInfo;
                
                // If parsing is successful, enable operations
                UpdateControlStates(isParsed: true);
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
                        Logger.LogEventResolve(bnkEvent.SelfId, eventId, eventAction.SoundObjectId);
                        var fileIds = ResolveObjectToFileIds(eventAction.SoundObjectId, sections);
                        Logger.LogEventWemsResolved(bnkEvent.SelfId, fileIds.Count);
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

                



                // Store event name lookup
                _eventNameLookup = eventNameLookup;
                Logger.LogSection("FILES LOADED SUCCESSFULLY");
                Logger.LogFilesLoaded(binPath, wpkPath, bnkPath);
            }
            catch (Exception ex)
            {
                Logger.LogSection("FILE LOADING FAILED");
                Logger.LogException("ParseFilesButton_Click", ex);
                Logger.LogFileParsingError(ex.Message);
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
                Logger.LogResolveSound(objectId, sound.SourceId);
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
                    
                Logger.LogResolveContainer(objectId, childIds.Count);
                foreach (var childId in childIds)
                {
                    Logger.LogResolveChild(childId);
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
                _rootNodes.Clear();
                TreeView.ItemsSource = null;

                // Reset internal data
                _currentWemInformation = null;
                _currentBnkSections = null;
                _originalBnkBytes = null;
                _currentBnkPath = null;
                _currentWpkPath = null;
                _eventNameLookup.Clear();

                // Clear UI paths
                BinFilePath.Text = string.Empty;
                WpkFilePath.Text = string.Empty;
                BnkFilePath.Text = string.Empty;

                // Reset progress
                ExtractionProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;

                // Update UI states to "Startup" mode
                UpdateControlStates(isParsed: false);
                
                Logger.Log("Treeview cleared and session reset.");
            }
            catch (Exception ex)
            {
                Logger.LogException("DeleteTreeView_Click", ex);
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

                string successMsg = $"Successfully extracted {extractedCount} out of {totalFiles} files to:\n{extractDir}";
                Logger.Log(successMsg);
                System.Windows.MessageBox.Show(successMsg, "Extraction Complete", MessageBoxButton.OK, MessageBoxImage.Information);


            }
            catch (Exception ex)
            {
                ExtractionProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;

                Logger.LogExtractionError(ex.Message);
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
            catch (Exception)
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
            UpdateControlStates(isParsed: _currentWemInformation != null);
            
            var selectedNode = TreeView.SelectedItem as TreeNode;
            if (selectedNode != null && selectedNode.NodeType == NodeType.WemFile && selectedNode.WemData != null)
            {
                // Only auto-play if the setting is enabled
                if (SettingsManager.PlayOnClick)
                {
                    Logger.LogAudioPlay("AUTO-PLAY AUDIO", selectedNode.Name, selectedNode.Parent?.Name ?? "Unknown", selectedNode.WemData?.Data.Length ?? 0);
                    PlayWem(selectedNode.WemData!);
                }
            }
        }

        private void PlaySoundButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedNode = TreeView.SelectedItem as TreeNode;
            if (selectedNode != null && selectedNode.NodeType == NodeType.WemFile && selectedNode.WemData != null)
            {
                Logger.LogAudioPlay("MANUAL-PLAY AUDIO", selectedNode.Name, selectedNode.Parent?.Name ?? "Unknown", selectedNode.WemData?.Data.Length ?? 0);
                
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
                            Dispatcher.Invoke(() => UpdateControlStates(isParsed: _currentWemInformation != null));
                        };
                        _outputDevice.Play();
                        Dispatcher.Invoke(() => UpdateControlStates(isParsed: _currentWemInformation != null));
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
                    string errorMsg = $"Failed to play sound: {ex.Message}";
                    Logger.LogError(errorMsg);
                    System.Windows.MessageBox.Show(errorMsg);
                    CleanupPlayback();
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Unexpected error playing sound: {ex.Message}";
                Logger.LogError(errorMsg);
                System.Windows.MessageBox.Show(errorMsg);
            }
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
                    Logger.LogWemReplaced(selectedWemNodes.Count, System.IO.Path.GetFileName(wemFiles[0]));
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
                    Logger.LogWemReplacementSummary(replaced, notMatched);
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

                Logger.LogWemReplacementError(ex.Message);
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
                            }

                            // Verify data consistency
                            if (wemNode.WemData.Data.Length != wemNode.WemData.Length)
                            {
                                Logger.LogIntegrityMismatch(wemNode.Name, wemNode.WemData.Data.Length, wemNode.WemData.Length);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("VerifyWemDataIntegrity", ex);
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
                                if (child.WemData != null)
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



                    // Convert to list for legacy compatibility (but we'll use slot-preserving method)
                    wemDataList = wemDataById.Values.ToList();




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


                        WpkWriter.WriteWpkFilePreservingSlots(
                            wemDataByIdForWrite,
                            _currentWemInformation.OriginalSlotCount,
                            _currentWemInformation.WemIdToSlotIndex,
                            new HashSet<uint>(), // No deletions
                            newWemFiles,
                            outputPath,
                            _currentWemInformation.OriginalOffsets,
                            _currentWemInformation.SlotIndexToEntry);
                    }
                    else
                    {
                        // Fallback to legacy method if no slot info available

                        WpkWriter.WriteWpkFile(wemDataList, outputPath);
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
                
                Logger.LogSection("SAVE WPK SUCCESSFUL");
                Logger.LogWpkSaveSuccess(outputPath, wemDataList.Count);
            }
            catch (Exception ex)
            {
                Logger.LogSection("SAVE WPK FAILED");
                Logger.LogException("SaveAsBnkWpk_Click", ex);
                Logger.LogWpkSaveError(ex.Message);
            }
        }

















        private void PackageWemsToWpk_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Folder containing .wem files"
            };

            if (folderDialog.ShowDialog() != true) return;

            string inputFolder = folderDialog.FolderName;

            string defaultFileName = !string.IsNullOrEmpty(WpkFilePath.Text) 
                ? System.IO.Path.GetFileName(WpkFilePath.Text) 
                : System.IO.Path.GetFileName(inputFolder) + ".wpk";

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save WPK File",
                Filter = "WPK files (*.wpk)|*.wpk|All files (*.*)|*.*",
                FileName = defaultFileName
            };

            if (saveDialog.ShowDialog() != true) return;

            string outputPath = saveDialog.FileName;

            try
            {
                // Logger.Clear();
                WpkPackager.PackageWems(inputFolder, outputPath);
                System.Windows.MessageBox.Show("WPK Packaging Complete!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.LogException("WPK Packaging", ex);
                Logger.LogPackagingError(ex.Message);
            }
        }


        private void ExpandSelected_Click(object sender, RoutedEventArgs e)
        {
            var checkedNodes = GetAllNodes().Where(n => n.IsChecked == true).ToList();
            if (checkedNodes.Count > 0)
            {
                foreach (var node in checkedNodes)
                    node.ExpandAll();
            }
            else if (TreeView.SelectedItem is TreeNode selected)
            {
                selected.ExpandAll();
            }
        }

        private void CollapseSelected_Click(object sender, RoutedEventArgs e)
        {
            var checkedNodes = GetAllNodes().Where(n => n.IsChecked == true).ToList();
            if (checkedNodes.Count > 0)
            {
                foreach (var node in checkedNodes)
                    node.CollapseAll();
            }
            else if (TreeView.SelectedItem is TreeNode selected)
            {
                selected.CollapseAll();
            }
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var root in _rootNodes)
                root.ExpandAll();
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var root in _rootNodes)
                root.CollapseAll();
        }

        private void UpdateControlStates(bool isParsed = false)
        {
            bool hasBin = !string.IsNullOrWhiteSpace(BinFilePath.Text) && File.Exists(BinFilePath.Text);
            bool hasWpk = !string.IsNullOrWhiteSpace(WpkFilePath.Text) && File.Exists(WpkFilePath.Text);
            bool hasBnk = !string.IsNullOrWhiteSpace(BnkFilePath.Text) && File.Exists(BnkFilePath.Text);

            // Parse button is active only when all 3 files are selected
            ParseFilesButton.IsEnabled = hasBin && hasWpk && hasBnk;

            var selectedNode = TreeView.SelectedItem as TreeNode;
            bool isWemSelected = selectedNode != null && selectedNode.NodeType == NodeType.WemFile;
            bool isRootSelected = selectedNode != null && selectedNode.NodeType == NodeType.Root;
            
            bool isPlaying = false;
            lock (_playLock)
            {
                isPlaying = _outputDevice != null && _outputDevice.PlaybackState == PlaybackState.Playing;
            }

            // All other operational buttons are active only AFTER a successful parse
            DeleteTreeView.IsEnabled = isParsed;
            ExtractSelection.IsEnabled = isParsed;
            ExportWwiseProject.IsEnabled = isParsed;
            PackageWemsToWpk.IsEnabled = isParsed;
            
            // Selection-specific buttons
            SaveAsBnkWpkButton.IsEnabled = isParsed && isRootSelected;
            ReplaceWemDataButton.IsEnabled = isParsed && isWemSelected;
            PlayAudioButton.IsEnabled = isParsed && isWemSelected;
            
            // Playback-specific button
            StopAllSoundsButton.IsEnabled = isPlaying;
        }

        private void StopAllSoundsButton_Click(object sender, RoutedEventArgs e)
        {
            CleanupPlayback();
            UpdateControlStates(isParsed: _currentWemInformation != null);
        }
    }
}
