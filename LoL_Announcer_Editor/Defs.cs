using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LoL_Announcer_Editor
{
    public static class Defs
    {
        public static int VERBOSE = 0;
        public static TextWriter ConsolelessStderr = Console.Error;

        public static void EPrintf(string format, params object[] args)
        {
            string msg = string.Format(format, args);
            Logger.LogError(msg.TrimEnd('\n', '\r'));
        }

#if DEBUG
        public static void DPrintf(string format, params object[] args)
        {
            string msg = string.Format(format, args);
            Logger.Log("[DEBUG] " + msg.TrimEnd('\n', '\r'));
        }
#else
        public static void DPrintf(string format, params object[] args) { }
#endif

        public static void VPrintf(int level, string format, params object[] args)
        {
            if (VERBOSE >= level)
            {
                string msg = string.Format(format, args);
                Logger.Log("[VERBOSE] " + msg.TrimEnd('\n', '\r'));
            }
        }
    }

    public class BinaryData
    {
        public ulong Length { get; set; }
        public required byte[] Data { get; set; }
    }

    public class AudioData
    {
        public uint Id { get; set; }
        public uint Length { get; set; }
        public required byte[] Data { get; set; }
    }

    public class StringWithChildren
    {
        public required string String { get; set; }
        public AudioData? WemData { get; set; }
        public List<AudioData> Children { get; set; } = new List<AudioData>();
    }

    public class WemInformation
    {
        public List<StringWithChildren> GroupedWems { get; set; } = new List<StringWithChildren>();
        public List<AudioData> SortedWemDataList { get; set; } = new List<AudioData>();

        // Original WPK structure preservation
        public int OriginalSlotCount { get; set; } = 0;  // Total slots in original WPK (including zeros)
        public Dictionary<uint, int> WemIdToSlotIndex { get; set; } = new Dictionary<uint, int>();  // Maps WEM ID to original slot index

        // Exact layout preservation for WPK
        public List<uint> OriginalOffsets { get; set; } = new List<uint>();
        public Dictionary<int, WpkFileEntry> SlotIndexToEntry { get; set; } = new Dictionary<int, WpkFileEntry>();
    }

    public enum NodeType
    {
        Root,
        Event,
        Container,  // For SequenceContainer/RandomContainer/SwitchContainer IDs
        WemFile
    }

    public class TreeNode : INotifyPropertyChanged
    {
        public required string Name { get; set; }
        public ObservableCollection<TreeNode> Children { get; set; } = new ObservableCollection<TreeNode>();
        public AudioData? WemData { get; set; } // Only set for WEM file nodes
        public NodeType NodeType { get; set; } = NodeType.Root; // Default to Root
        public bool IsSelected { get; set; } = false; // For legacy selection
        public bool ShowCheckbox { get; set; } = false; // Controls checkbox visibility
        public TreeNode? Parent { get; set; }

        private bool? _isChecked = false;
        private bool _isInternalChange = false;
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                    if (!_isInternalChange)
                    {
                        // Downward propagation
                        SetChildrenChecked(value);
                        // Upward propagation
                        UpdateParentChecked();
                    }
                }
            }
        }

        private string? _replacementFileName;
        public string? ReplacementFileName
        {
            get => _replacementFileName;
            set
            {
                if (_replacementFileName != value)
                {
                    _replacementFileName = value;
                    OnPropertyChanged(nameof(ReplacementFileName));
                }
            }
        }



        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        private void SetChildrenChecked(bool? value)
        {
            foreach (var child in Children)
            {
                child._isInternalChange = true;
                child.IsChecked = value;
                child._isInternalChange = false;
                child.SetChildrenChecked(value);
            }
        }

        private void UpdateParentChecked()
        {
            if (Parent == null) return;
            bool allChecked = Parent.Children.All(c => c.IsChecked == true);
            bool allUnchecked = Parent.Children.All(c => c.IsChecked == false);
            bool? parentState = null;
            if (allChecked)
                parentState = true;
            else if (allUnchecked)
                parentState = false;
            // else parentState remains null (indeterminate)
            Parent._isInternalChange = true;
            Parent.IsChecked = parentState;
            Parent._isInternalChange = false;
            Parent.UpdateParentChecked();
        }

        public void ExpandAll()
        {
            IsExpanded = true;
            foreach (var child in Children)
                child.ExpandAll();
        }

        public void CollapseAll()
        {
            IsExpanded = false;
            foreach (var child in Children)
                child.CollapseAll();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 
