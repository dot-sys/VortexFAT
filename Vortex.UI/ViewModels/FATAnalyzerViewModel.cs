using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Drives.Models;

// ViewModels for application UI binding
namespace Vortex.UI.ViewModels
{
    // Manages FAT analyzer data and file tree
    public class FATAnalyzerViewModel : INotifyPropertyChanged
    {
        // Complete file list from drive
        private ObservableCollection<FileEntry> _allFiles;
        // Currently displayed file list
        private ObservableCollection<FileEntry> _filteredFiles;
        // Hierarchical folder structure tree
        private ObservableCollection<FileTreeNode> _fileTree;
        // Currently selected tree node
        private FileTreeNode _selectedNode;
        // Target drive letter path
        private string _selectedDrivePath;
        // Drive partition name
        private string _partitionName;
        // Drive volume label text
        private string _volumeLabel;
        // Prevents recursive update calls
        private bool _isUpdating;
        // Marks deleted files red
        private bool _markDeletedRed;
        // Marks unsigned files gold
        private bool _markUnsignedGold;
        // Marks hidden files blue
        private bool _markHiddenBlue;
        // Currently selected file entry
        private FileEntry _selectedFile;
        // Bottom grid detail rows
        private ObservableCollection<BottomGridRow> _bottomGridData;
        // Shows loading overlay visibility
        private bool _isLoadingOverlayVisible;
        // Loading overlay message text
        private string _loadingMessage;

        // Gets or sets all files
        public ObservableCollection<FileEntry> AllFiles
        {
            get => _allFiles;
            set
            {
                _allFiles = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets filtered files
        public ObservableCollection<FileEntry> FilteredFiles
        {
            get => _filteredFiles;
            set
            {
                _filteredFiles = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets file tree
        public ObservableCollection<FileTreeNode> FileTree
        {
            get => _fileTree;
            set
            {
                _fileTree = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets selected node
        public FileTreeNode SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_isUpdating || _selectedNode == value) return;

                _selectedNode = value;
                OnPropertyChanged();
                FilterFilesByNode();
            }
        }

        // Gets or sets selected drive path
        public string SelectedDrivePath
        {
            get => _selectedDrivePath;
            set
            {
                _selectedDrivePath = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets partition name
        public string PartitionName
        {
            get => _partitionName;
            set
            {
                _partitionName = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets volume label
        public string VolumeLabel
        {
            get => _volumeLabel;
            set
            {
                _volumeLabel = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets red marking
        public bool MarkDeletedRed
        {
            get => _markDeletedRed;
            set
            {
                _markDeletedRed = value;
                OnPropertyChanged();
                RefreshFilteredFiles();
            }
        }

        // Gets or sets gold marking
        public bool MarkUnsignedGold
        {
            get => _markUnsignedGold;
            set
            {
                _markUnsignedGold = value;
                OnPropertyChanged();
                RefreshFilteredFiles();
            }
        }

        // Gets or sets blue marking
        public bool MarkHiddenBlue
        {
            get => _markHiddenBlue;
            set
            {
                _markHiddenBlue = value;
                OnPropertyChanged();
                RefreshFilteredFiles();
            }
        }

        // Gets or sets selected file
        public FileEntry SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged();
                UpdateBottomGridData();
                ComputeHashForDeletedFile();
            }
        }

        // Gets or sets bottom grid data
        public ObservableCollection<BottomGridRow> BottomGridData
        {
            get => _bottomGridData;
            set
            {
                _bottomGridData = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets loading visibility
        public bool IsLoadingOverlayVisible
        {
            get => _isLoadingOverlayVisible;
            set
            {
                _isLoadingOverlayVisible = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets loading message
        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                _loadingMessage = value;
                OnPropertyChanged();
            }
        }

        // Initializes viewmodel with empty collections
        public FATAnalyzerViewModel()
        {
            AllFiles = new ObservableCollection<FileEntry>();
            FilteredFiles = new ObservableCollection<FileEntry>();
            FileTree = new ObservableCollection<FileTreeNode>();
            BottomGridData = new ObservableCollection<BottomGridRow>();
            _markDeletedRed = false;
            _markUnsignedGold = false;
            _markHiddenBlue = false;
        }

        // Loads files into viewmodel collections
        public void LoadFiles(System.Collections.Generic.List<FileEntry> files)
        {
            if (files == null) return;

            _isUpdating = true;
            try
            {
                AllFiles.Clear();
                foreach (var file in files)
                {
                    AllFiles.Add(file);
                }

                BuildFileTree();
            }
            finally
            {
                _isUpdating = false;
            }

            FilterFilesByNode();
        }

        // Builds hierarchical file tree structure
        private void BuildFileTree()
        {
            FileTree.Clear();

            var driveLetter = string.IsNullOrEmpty(SelectedDrivePath) ? "Drive" : SelectedDrivePath.TrimEnd(':', '\\');

            var driveDisplayName = !string.IsNullOrEmpty(VolumeLabel)
                ? $"{driveLetter}: ({VolumeLabel})"
                : $"{driveLetter}:";

            if (!string.IsNullOrEmpty(PartitionName))
            {
                driveDisplayName = !string.IsNullOrEmpty(VolumeLabel)
                    ? $"{driveLetter}: ({VolumeLabel}) - {PartitionName}"
                    : $"{driveLetter}: - {PartitionName}";
            }

            var driveNode = new FileTreeNode(
                driveDisplayName,
                driveLetter + ":",
                true,
                false)
            {
                NodeType = FileTreeNodeType.Drive
            };

            var rootPath = "\\";
            var rootNode = new FileTreeNode(
                "Root",
                rootPath,
                true,
                false)
            {
                NodeType = FileTreeNodeType.Root
            };

            driveNode.Children.Add(rootNode);

            var pathMap = new System.Collections.Generic.Dictionary<string, FileTreeNode>(System.StringComparer.OrdinalIgnoreCase)
            {
                [rootPath] = rootNode
            };

            foreach (var file in AllFiles.Where(f => f.IsDirectory && !f.IsDeleted).OrderBy(f => f.FullPath))
            {
                var path = NormalizePath(file.FullPath);
                if (string.IsNullOrEmpty(path) || path == "\\") continue;

                var parts = path.Split(new[] { '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var currentPath = rootPath;
                FileTreeNode parentNode = rootNode;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.IsNullOrEmpty(parts[i])) continue;

                    if (currentPath.EndsWith("\\"))
                        currentPath += parts[i];
                    else
                        currentPath += "\\" + parts[i];

                    if (!pathMap.ContainsKey(currentPath))
                    {
                        var newNode = new FileTreeNode(
                            parts[i],
                            currentPath,
                            true,
                            false)
                        {
                            NodeType = FileTreeNodeType.Folder
                        };

                        parentNode.Children.Add(newNode);
                        pathMap[currentPath] = newNode;
                    }

                    parentNode = pathMap[currentPath];
                }
            }

            foreach (var file in AllFiles.Where(f => !f.IsDirectory && !string.IsNullOrEmpty(f.FullPath)))
            {
                var path = NormalizePath(file.FullPath);
                var fileDir = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(fileDir) || fileDir == "\\") continue;

                var parts = fileDir.Split(new[] { '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var currentPath = rootPath;
                FileTreeNode parentNode = rootNode;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.IsNullOrEmpty(parts[i])) continue;

                    if (currentPath.EndsWith("\\"))
                        currentPath += parts[i];
                    else
                        currentPath = currentPath + "\\" + parts[i];

                    if (!pathMap.ContainsKey(currentPath))
                    {
                        var newNode = new FileTreeNode(
                            parts[i],
                            currentPath,
                            true,
                            false)
                        {
                            NodeType = FileTreeNodeType.Folder
                        };

                        parentNode.Children.Add(newNode);
                        pathMap[currentPath] = newNode;
                    }

                    parentNode = pathMap[currentPath];
                }
            }

            var deletedFilesText = System.Windows.Application.Current?.TryFindResource("DeletedFiles") as string ?? "[Deleted Files]";
            var unallocatedNode = new FileTreeNode(
                deletedFilesText,
                "[UNALLOCATED]",
                false,
                false)
            {
                NodeType = FileTreeNodeType.Unallocated
            };
            driveNode.Children.Add(unallocatedNode);

            var allFilesText = System.Windows.Application.Current?.TryFindResource("AllFiles") as string ?? "[All Files]";
            var allFilesNode = new FileTreeNode(
                allFilesText,
                "[ALLFILES]",
                false,
                false)
            {
                NodeType = FileTreeNodeType.AllFiles
            };
            driveNode.Children.Add(allFilesNode);

            FileTree.Add(driveNode);

            driveNode.IsExpanded = true;
        }

        // Filters files based on selected node
        private void FilterFilesByNode()
        {
            if (FilteredFiles == null || AllFiles == null) return;

            FilteredFiles.Clear();

            if (SelectedNode == null)
            {
                return;
            }

            if (SelectedNode.NodeType == FileTreeNodeType.Drive)
            {
                return;
            }
            else if (SelectedNode.NodeType == FileTreeNodeType.Root)
            {
                foreach (var file in AllFiles.Where(f => !string.IsNullOrEmpty(f.FullPath)))
                {
                    var filePath = NormalizePath(file.FullPath);
                    var fileDir = System.IO.Path.GetDirectoryName(filePath);
                    if (string.IsNullOrEmpty(fileDir) || fileDir == "\\")
                    {
                        FilteredFiles.Add(file);
                    }
                }
            }
            else if (SelectedNode.NodeType == FileTreeNodeType.Unallocated)
            {
                foreach (var file in AllFiles.Where(f => f.IsDeleted))
                {
                    FilteredFiles.Add(file);
                }
            }
            else if (SelectedNode.NodeType == FileTreeNodeType.AllFiles)
            {
                foreach (var file in AllFiles)
                {
                    FilteredFiles.Add(file);
                }
            }
            else
            {
                var selectedPath = SelectedNode.FullPath;
                if (string.IsNullOrEmpty(selectedPath))
                {
                    return;
                }

                var normalizedSelectedPath = selectedPath.TrimEnd('\\');

                foreach (var file in AllFiles)
                {
                    if (string.IsNullOrEmpty(file.FullPath)) continue;

                    var filePath = NormalizePath(file.FullPath);
                    var fileDir = System.IO.Path.GetDirectoryName(filePath);
                    if (string.IsNullOrEmpty(fileDir))
                    {
                        fileDir = "\\";
                    }

                    var normalizedFileDir = fileDir.TrimEnd('\\');

                    if (normalizedFileDir.Equals(normalizedSelectedPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        FilteredFiles.Add(file);
                    }
                }
            }
        }

        // Refreshes filtered files display
        private void RefreshFilteredFiles()
        {
            if (FilteredFiles != null && FilteredFiles.Count > 0)
            {
                var tempList = FilteredFiles.ToList();
                FilteredFiles.Clear();
                foreach (var item in tempList)
                {
                    FilteredFiles.Add(item);
                }
            }
        }

        // Updates bottom grid with file details
        private void UpdateBottomGridData()
        {
            BottomGridData.Clear();

            if (SelectedFile == null)
                return;

            var fullPath = SelectedFile.FullPath ?? string.Empty;
            if (!string.IsNullOrEmpty(SelectedDrivePath) && !fullPath.Contains(":"))
            {
                var driveLetter = SelectedDrivePath.TrimEnd(':', '\\');
                fullPath = driveLetter + ":" + fullPath;
            }

            BottomGridData.Add(new BottomGridRow
            {
                Column1 = "Full Path:",
                Column2 = fullPath,
                Column3 = ""
            });

            BottomGridData.Add(new BottomGridRow
            {
                Column1 = "Created Time:",
                Column2 = SelectedFile.CreationTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                Column3 = ""
            });

            BottomGridData.Add(new BottomGridRow
            {
                Column1 = "Modified Time:",
                Column2 = SelectedFile.ModifiedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                Column3 = ""
            });

            BottomGridData.Add(new BottomGridRow
            {
                Column1 = "Accessed Time:",
                Column2 = SelectedFile.AccessedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                Column3 = ""
            });

            BottomGridData.Add(new BottomGridRow
            {
                Column1 = "Attributes:",
                Column2 = SelectedFile.Attributes ?? "N/A",
                Column3 = ""
            });

            var hashValue = SelectedFile.Hash ?? "N/A";
            var hashForUrl = StripHashTypeSuffix(hashValue);

            var virusTotalLink = IsValidHashForUrl(hashForUrl)
                ? $"https://www.virustotal.com/gui/search/{hashForUrl}"
                : "";

            BottomGridData.Add(new BottomGridRow
            {
                Column1 = "Hash (MD5):",
                Column2 = hashForUrl,
                Column3 = virusTotalLink
            });
        }

        // Computes hash for deleted file asynchronously
        private async void ComputeHashForDeletedFile()
        {
            if (SelectedFile == null)
                return;

            if (!SelectedFile.IsDeleted && SelectedFile.Status != "Replaced")
                return;

            if (SelectedFile.IsDirectory)
                return;

            if (SelectedFile.Hash != "Deleted")
                return;

            var hashRowIndex = -1;
            for (int i = 0; i < BottomGridData.Count; i++)
            {
                if (BottomGridData[i].Column1 == "Hash (MD5):")
                {
                    hashRowIndex = i;
                    break;
                }
            }

            if (hashRowIndex == -1)
                return;

            var originalRow = BottomGridData[hashRowIndex];

            BottomGridData.RemoveAt(hashRowIndex);
            BottomGridData.Insert(hashRowIndex, new BottomGridRow
            {
                Column1 = "Hash (MD5):",
                Column2 = "Computing...",
                Column3 = ""
            });

            var fileToCompute = SelectedFile;
            var drivePath = SelectedDrivePath;

            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var driveInfo = Drives.Core.DriveDetector.GetAllDrives()
                        .FirstOrDefault(d => d.DriveLetter.Equals(drivePath, System.StringComparison.OrdinalIgnoreCase));

                    if (driveInfo == null)
                        return new { Success = true, Hash = "Drive Not Found" };

                    bool isExFAT = driveInfo.FileSystem.Equals("exFAT", System.StringComparison.OrdinalIgnoreCase);
                    bool isFAT16 = driveInfo.FileSystem.Equals("FAT", System.StringComparison.OrdinalIgnoreCase);

                    string computedHash = Drives.Util.HashCarver.ComputeDeletedFileHash(fileToCompute, drivePath, isExFAT, isFAT16);
                    return new { Success = true, Hash = computedHash };
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error computing hash: {ex.Message}");
                    return new { Success = false, Hash = "Error" };
                }
            });

            if (SelectedFile == fileToCompute && hashRowIndex >= 0 && hashRowIndex < BottomGridData.Count)
            {
                fileToCompute.Hash = result.Hash;

                var hashForDisplay = StripHashTypeSuffix(result.Hash);
                var virusTotalLink = IsValidHashForUrl(hashForDisplay)
                    ? $"https://www.virustotal.com/gui/search/{hashForDisplay}"
                    : "";

                BottomGridData.RemoveAt(hashRowIndex);
                BottomGridData.Insert(hashRowIndex, new BottomGridRow
                {
                    Column1 = "Hash (MD5):",
                    Column2 = hashForDisplay,
                    Column3 = virusTotalLink
                });
            }
        }

        // Property change notification event
        public event PropertyChangedEventHandler PropertyChanged;

        // Raises property changed event
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Normalizes file path format
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            path = path.Replace("/", "\\");

            if (path.Length >= 2 && path[1] == ':')
            {
                path = path.Substring(2);
            }

            return path;
        }

        // Removes hash type suffix from string
        private static string StripHashTypeSuffix(string hash)
        {
            if (string.IsNullOrEmpty(hash) || !hash.Contains(" ("))
                return hash;

            var indexOfType = hash.IndexOf(" (");
            return hash.Substring(0, indexOfType).Trim();
        }

        // Validates hash for URL usage
        private static bool IsValidHashForUrl(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return false;

            var invalidValues = new[] { "N/A", "Deleted", "Computing...", "Error",
                                                "Drive Not Found", "Access Denied", "Admin Required",
                                                "Unrecoverable", "Invalid Cluster", "Too Large", "Empty" };

            return !invalidValues.Contains(hash);
        }
    }

    // Holds bottom grid row data
    public class BottomGridRow : INotifyPropertyChanged
    {
        // First column text
        private string _column1;
        // Second column text
        private string _column2;
        // Third column text
        private string _column3;

        // Gets or sets column one
        public string Column1
        {
            get => _column1;
            set
            {
                if (_column1 == value) return;
                _column1 = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets column two
        public string Column2
        {
            get => _column2;
            set
            {
                if (_column2 == value) return;
                _column2 = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets column three
        public string Column3
        {
            get => _column3;
            set
            {
                if (_column3 == value) return;
                _column3 = value;
                OnPropertyChanged();
            }
        }

        // Property change notification event
        public event PropertyChangedEventHandler PropertyChanged;

        // Raises property changed event
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // File tree node type enumeration
    public enum FileTreeNodeType
    {
        Drive,
        Root,
        Folder,
        Unallocated,
        AllFiles
    }

    // Represents hierarchical tree node structure
    public class FileTreeNode : INotifyPropertyChanged
    {
        // Node display name text
        private string _displayName;
        // Full file path string
        private string _fullPath;
        // Directory flag indicator
        private bool _isDirectory;
        // Deleted flag indicator
        private bool _isDeleted;
        // Expanded state flag
        private bool _isExpanded;
        // Child nodes collection
        private ObservableCollection<FileTreeNode> _children;
        // Node type identifier
        private FileTreeNodeType _nodeType;

        // Gets or sets display name
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value) return;
                _displayName = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets full path
        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath == value) return;
                _fullPath = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets directory flag
        public bool IsDirectory
        {
            get => _isDirectory;
            set
            {
                if (_isDirectory == value) return;
                _isDirectory = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets deleted flag
        public bool IsDeleted
        {
            get => _isDeleted;
            set
            {
                if (_isDeleted == value) return;
                _isDeleted = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets expanded state
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets child nodes
        public ObservableCollection<FileTreeNode> Children
        {
            get => _children;
            set
            {
                if (_children == value) return;
                _children = value;
                OnPropertyChanged();
            }
        }

        // Gets or sets node type
        public FileTreeNodeType NodeType
        {
            get => _nodeType;
            set
            {
                if (_nodeType == value) return;
                _nodeType = value;
                OnPropertyChanged();
            }
        }

        // Initializes empty file tree node
        public FileTreeNode()
        {
        }

        // Initializes file tree node with values
        public FileTreeNode(string displayName, string fullPath, bool isDirectory, bool isDeleted)
        {
            _displayName = displayName;
            _fullPath = fullPath;
            _isDirectory = isDirectory;
            _isDeleted = isDeleted;
            _children = new ObservableCollection<FileTreeNode>();
        }

        // Property change notification event
        public event PropertyChangedEventHandler PropertyChanged;

        // Raises property changed event
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
