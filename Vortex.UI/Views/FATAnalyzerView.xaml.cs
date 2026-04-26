using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Vortex.UI.ViewModels;
using Drives.Models;
using Drives.Core;
using Microsoft.Win32;

namespace Vortex.UI.Views
{
    public partial class FATAnalyzerView : Page
    {
        private readonly FATAnalyzerViewModel _viewModel;

        public FATAnalyzerView()
        {
            InitializeComponent();
            _viewModel = new FATAnalyzerViewModel();
            DataContext = _viewModel;
        }

        public void LoadFiles(List<FileEntry> files, string drivePath, string partitionName = null, string volumeLabel = null)
        {
            _viewModel.SelectedDrivePath = drivePath;
            _viewModel.PartitionName = partitionName;
            _viewModel.VolumeLabel = volumeLabel;
            _viewModel.LoadFiles(files);
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is FileTreeNode node)
            {
                _viewModel.SelectedNode = node;
            }
        }

        private void FileTreeView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FileTreeView.SelectedItem is FileTreeNode node && node.IsDirectory)
            {
                node.IsExpanded = !node.IsExpanded;
            }
        }

        private void FilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is FileEntry selectedFile)
            {
                _viewModel.SelectedFile = selectedFile;
            }
        }

        private void FilesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is FileEntry selectedFile && selectedFile.IsDirectory)
            {
                NavigateToFolderInTree(selectedFile);
            }
        }

        private void NavigateToFolderInTree(FileEntry folder)
        {
            if (folder == null || !folder.IsDirectory) return;

            var targetPath = NormalizePath(folder.FullPath);
            var node = FindNodeByPath(_viewModel.FileTree, targetPath);

            if (node != null)
            {
                ExpandParentNodes(node);
                node.IsExpanded = true;
                _viewModel.SelectedNode = node;
                SelectTreeViewItemAsync(node);
            }
        }

        private async void SelectTreeViewItemAsync(FileTreeNode targetNode)
        {
            if (targetNode == null) return;

            await Task.Delay(100);

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    FileTreeView.UpdateLayout();
                    var treeViewItem = FindTreeViewItem(FileTreeView, targetNode);

                    if (treeViewItem != null)
                    {
                        treeViewItem.IsSelected = true;
                        treeViewItem.Focus();
                        treeViewItem.BringIntoView();
                    }
                }
                catch
                {
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private TreeViewItem FindTreeViewItem(ItemsControl container, object item)
        {
            if (container == null) return null;

            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem containerItem)
                return containerItem;

            foreach (object childItem in container.Items)
            {
                if (!(container.ItemContainerGenerator.ContainerFromItem(childItem) is TreeViewItem parent)) continue;

                if (!parent.IsExpanded)
                {
                    parent.IsExpanded = true;
                    parent.UpdateLayout();
                }

                var foundItem = FindTreeViewItem(parent, item);
                if (foundItem != null)
                    return foundItem;
            }

            return null;
        }

        private FileTreeNode FindNodeByPath(ObservableCollection<FileTreeNode> nodes, string path)
        {
            if (nodes == null) return null;

            foreach (var node in nodes)
            {
                var nodePath = NormalizePath(node.FullPath);

                if (nodePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }

                var found = FindNodeByPath(node.Children, path);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void ExpandParentNodes(FileTreeNode node)
        {
            if (node == null) return;

            var parent = FindParentNode(_viewModel.FileTree, node);
            if (parent != null)
            {
                parent.IsExpanded = true;
                ExpandParentNodes(parent);
            }
        }

        private FileTreeNode FindParentNode(ObservableCollection<FileTreeNode> nodes, FileTreeNode targetNode)
        {
            if (nodes == null) return null;

            foreach (var node in nodes)
            {
                if (node.Children.Contains(targetNode))
                {
                    return node;
                }

                var found = FindParentNode(node.Children, targetNode);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.CurrentCell.Item != null)
            {
                var cellContent = FilesDataGrid.CurrentCell.Column.GetCellContent(FilesDataGrid.CurrentCell.Item);
                if (cellContent is TextBlock textBlock)
                {
                    Clipboard.SetText(textBlock.Text ?? string.Empty);
                }
            }
        }

        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            if (FilesDataGrid.SelectedItem is FileEntry entry)
            {
                var rowData = BuildFileEntryText(entry, includeHeaders: true);
                Clipboard.SetText(rowData);
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            var allData = new StringBuilder();
            allData.AppendLine("Name\tType\tStatus\tSize\tModified\tCreated\tAccessed\tSignature\tPath\tAttributes\tStart Cluster\tSlack Space\tReconstruction Source");

            foreach (FileEntry entry in _viewModel.FilteredFiles)
            {
                allData.AppendLine(BuildFileEntryText(entry, includeHeaders: false));
            }

            Clipboard.SetText(allData.ToString());
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ContextMenu contextMenu)) return;

            bool isDeletedFile = IsDeletedFile(FilesDataGrid.SelectedItem as FileEntry);
            int deletedFilesCount = _viewModel.FilteredFiles.Count(f => IsDeletedFile(f));
            bool hasMultipleDeletedFiles = deletedFilesCount > 1;

            SetMenuItemVisibility(contextMenu, "RecoverMenuItem", isDeletedFile);
            SetMenuItemVisibility(contextMenu, "RecoverAllMenuItem", hasMultipleDeletedFiles);
            SetSeparatorVisibility(contextMenu, "RecoverSeparator", isDeletedFile || hasMultipleDeletedFiles);
        }

        private void Recover_Click(object sender, RoutedEventArgs e)
        {
            if (!(FilesDataGrid.SelectedItem is FileEntry selectedFile))
                return;

            if (selectedFile.IsDirectory)
            {
                MessageBox.Show(
                    "Cannot recover directories. Only files can be recovered.",
                    "Recovery Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                FileName = selectedFile.DisplayName,
                Title = "Select location to save recovered file",
                Filter = "All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var analyzer = new FATAnalyzer(_viewModel.SelectedDrivePath);

                    bool success = analyzer.RecoverFile(selectedFile, saveFileDialog.FileName);

                    if (success)
                    {
                        MessageBox.Show(
                            $"File recovered successfully to:\n{saveFileDialog.FileName}",
                            "Recovery Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Failed to recover file. The file data may be corrupted or overwritten.",
                            "Recovery Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show(
                        "Administrator privileges are required to recover files.\nPlease restart the application as administrator.",
                        "Permission Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(
                        $"Error recovering file:\n{ex.Message}",
                        "Recovery Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private async void RecoverAll_Click(object sender, RoutedEventArgs e)
        {
            var deletedFiles = _viewModel.FilteredFiles.Where(IsDeletedFile).ToList();

            if (deletedFiles.Count == 0)
            {
                MessageBox.Show(
                    "No deleted files found in the current view.",
                    "Recovery Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Select a folder to recover {deletedFiles.Count} deleted file(s)",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string outputFolder = folderDialog.SelectedPath;

                _viewModel.IsLoadingOverlayVisible = true;
                _viewModel.LoadingMessage = "Recovering";

                var dotsTask = AnimateLoadingDots();

                try
                {
                    var result = await Task.Run(() => RecoverFilesAsync(deletedFiles, outputFolder));

                    _viewModel.IsLoadingOverlayVisible = false;

                    string message = $"Recovery Complete!\n\n" +
                                   $"Successfully recovered: {result.SuccessCount} file(s)\n" +
                                   $"Failed: {result.FailedCount} file(s)\n\n" +
                                   $"Files saved to:\n{outputFolder}\n\n" +
                                   $"See RecoveryInfo.txt for details.";

                    MessageBox.Show(
                        message,
                        "Recovery Complete",
                        MessageBoxButton.OK,
                        result.SuccessCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                catch (UnauthorizedAccessException)
                {
                    _viewModel.IsLoadingOverlayVisible = false;
                    MessageBox.Show(
                        "Administrator privileges are required to recover files.\nPlease restart the application as administrator.",
                        "Permission Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    _viewModel.IsLoadingOverlayVisible = false;
                    MessageBox.Show(
                        $"Error during batch recovery:\n{ex.Message}",
                        "Recovery Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private class RecoveryResult
        {
            public int SuccessCount { get; set; }
            public int FailedCount { get; set; }
            public List<string> RecoveredFiles { get; set; }
            public List<string> FailedFiles { get; set; }
        }

        private RecoveryResult RecoverFilesAsync(List<FileEntry> deletedFiles, string outputFolder)
        {
            var result = new RecoveryResult
            {
                RecoveredFiles = new List<string>(),
                FailedFiles = new List<string>()
            };

            // Use FATAnalyzer for file recovery
            var analyzer = new FATAnalyzer(_viewModel.SelectedDrivePath);

            foreach (var file in deletedFiles)
            {
                try
                {
                    if (file.IsDirectory || file.StartCluster < 2)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cannot recover {file.DisplayName}: Invalid file entry");
                        result.FailedFiles.Add(file.FullPath);
                        continue;
                    }

                    string fileName = file.DisplayName;

                    char[] invalidChars = Path.GetInvalidFileNameChars();
                    foreach (char c in invalidChars)
                    {
                        fileName = fileName.Replace(c, '_');
                    }

                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = $"RecoveredFile_{file.StartCluster}.dat";
                    }

                    string outputPath = Path.Combine(outputFolder, fileName);

                    int counter = 1;
                    while (File.Exists(outputPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string extension = Path.GetExtension(fileName);
                        outputPath = Path.Combine(outputFolder, $"{nameWithoutExt}_{counter}{extension}");
                        counter++;
                    }

                    System.Diagnostics.Debug.WriteLine($"Attempting to recover: {file.DisplayName} -> {outputPath}");
                    System.Diagnostics.Debug.WriteLine($"  Size: {file.FileSize} bytes, Start Cluster: {file.StartCluster}");

                    bool success = analyzer.RecoverFile(file, outputPath);

                    System.Diagnostics.Debug.WriteLine($"  Recovery result: {success}");

                    if (success && File.Exists(outputPath))
                    {
                        var fileInfo = new FileInfo(outputPath);
                        if (fileInfo.Length > 0)
                        {
                            result.SuccessCount++;
                            result.RecoveredFiles.Add(file.FullPath);
                            System.Diagnostics.Debug.WriteLine($"  Successfully recovered {fileInfo.Length} bytes");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  File created but is empty");
                            result.FailedFiles.Add(file.FullPath);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  Recovery failed or file not created");
                        result.FailedFiles.Add(file.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error recovering {file.DisplayName}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"  Stack trace: {ex.StackTrace}");
                    result.FailedFiles.Add(file.FullPath);
                }
            }

            result.FailedCount = result.FailedFiles.Count;

            string recoveryInfoPath = Path.Combine(outputFolder, "RecoveryInfo.txt");
            var infoContent = new StringBuilder();

            if (result.RecoveredFiles.Count > 0)
            {
                infoContent.AppendLine("Recovered Files");
                infoContent.AppendLine("------------------");
                foreach (var filePath in result.RecoveredFiles)
                {
                    infoContent.AppendLine(filePath);
                }
            }

            if (result.FailedFiles.Count > 0)
            {
                if (result.RecoveredFiles.Count > 0)
                    infoContent.AppendLine();
                infoContent.AppendLine("Failed to Recover");
                infoContent.AppendLine("------------------");
                foreach (var filePath in result.FailedFiles)
                {
                    infoContent.AppendLine(filePath);
                }
            }

            File.WriteAllText(recoveryInfoPath, infoContent.ToString());

            return result;
        }

        private async Task AnimateLoadingDots()
        {
            int dotCount = 0;
            while (_viewModel.IsLoadingOverlayVisible)
            {
                dotCount = (dotCount % 3) + 1;
                await Dispatcher.InvokeAsync(() =>
                {
                    _viewModel.LoadingMessage = "Recovering" + new string('.', dotCount);
                });
                await Task.Delay(500);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error opening link:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";

            path = path.Replace("/", "\\");

            if (path.Length >= 2 && path[1] == ':')
            {
                path = path.Substring(2);
            }

            path = path.TrimEnd('\\');
            return string.IsNullOrEmpty(path) ? "\\" : path;
        }

        private static bool IsDeletedFile(FileEntry entry)
        {
            return entry != null && !entry.IsDirectory && (entry.IsDeleted || entry.Status == "Replaced");
        }

        private static void SetMenuItemVisibility(ContextMenu contextMenu, string itemName, bool isVisible)
        {
            var menuItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.Name == itemName);
            if (menuItem != null)
            {
                menuItem.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void SetSeparatorVisibility(ContextMenu contextMenu, string separatorName, bool isVisible)
        {
            var separator = contextMenu.Items.OfType<Separator>().FirstOrDefault(sep => sep.Name == separatorName);
            if (separator != null)
            {
                separator.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static string BuildFileEntryText(FileEntry entry, bool includeHeaders)
        {
            if (includeHeaders)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Name: {entry.DisplayName}");
                sb.AppendLine($"Type: {entry.Type}");
                sb.AppendLine($"Status: {entry.Status}");
                sb.AppendLine($"Size: {entry.FileSizeFormatted}");
                sb.AppendLine($"Modified: {entry.DisplayModifiedTime?.ToString("yyyy-MM-dd HH:mm:ss")}");

                if (!string.IsNullOrEmpty(entry.DisplayAccessedTime))
                    sb.AppendLine($"Accessed: {entry.DisplayAccessedTime}");

                sb.AppendLine($"Created: {entry.CreationTime?.ToString("yyyy-MM-dd HH:mm:ss")}");
                sb.AppendLine($"Path: {entry.FullPath}");

                if (!string.IsNullOrEmpty(entry.Signature))
                    sb.AppendLine($"Signature: {entry.Signature}");

                sb.AppendLine($"Attributes: {entry.Attributes}");
                sb.AppendLine($"Start Cluster: {entry.StartCluster}");
                sb.AppendLine($"Slack Space: {entry.SlackSpace}");

                if (!string.IsNullOrEmpty(entry.ReconstructionSource))
                    sb.AppendLine($"Reconstruction Source: {entry.ReconstructionSource}");

                return sb.ToString();
            }
            else
            {
                return $"{entry.DisplayName}\t{entry.Type}\t{entry.Status}\t{entry.FileSizeFormatted}\t" +
                       $"{entry.DisplayModifiedTime?.ToString("yyyy-MM-dd HH:mm:ss")}\t" +
                       $"{entry.DisplayCreatedTime}\t{entry.DisplayAccessedTime}\t{entry.Signature}\t" +
                       $"{entry.FullPath}\t{entry.Attributes}\t{entry.StartCluster}\t" +
                       $"{entry.SlackSpace}\t{entry.ReconstructionSource}";
            }
        }
    }
}

