using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vortex.UI.ViewModels;
using Drives.Core;
using Drives.Models;

namespace Vortex.UI.Views
{
    public partial class WelcomeView : Page
    {
        private MainWindowViewModel _viewModel;
        private bool _isTraceStarted = false;
        private readonly DispatcherTimer _dotsTimer;
        private int _dotsCount = 0;
        private Storyboard _logoSpinStoryboard;
        private DriveInfo _selectedDrive;

        public WelcomeView()
        {
            InitializeComponent();

            _dotsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _dotsTimer.Tick += DotsTimer_Tick;

            LoadAvailableDrives();
        }

        private string GetResourceString(string key)
        {
            try
            {
                var resource = Application.Current.TryFindResource(key);
                return resource as string ?? key;
            }
            catch
            {
                return key;
            }
        }

        private void LoadAvailableDrives()
        {
            try
            {
                var fatDrives = DriveDetector.GetFATDrives();
                DriveComboBox.ItemsSource = fatDrives;

                if (fatDrives.Count > 0)
                {
                    DriveComboBox.SelectedIndex = 0;
                }
                else
                {
                    ShowStatus(GetResourceString("NoFATDrivesFound"));
                }
            }
            catch (Exception ex)
            {
                ShowStatus(string.Format(GetResourceString("ErrorLoadingDrives"), ex.Message));
            }
        }

        private void DriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedDrive = DriveComboBox.SelectedItem as DriveInfo;
            StartTraceButton.IsEnabled = _selectedDrive != null;
        }

        private void DotsTimer_Tick(object sender, EventArgs e)
        {
            _dotsCount = (_dotsCount + 1) % 4;
            DotsText.Text = new string('.', _dotsCount);
        }

        private async void StartTrace_Click(object sender, RoutedEventArgs e)
        {
            if (_isTraceStarted || _selectedDrive == null) return;

            _isTraceStarted = true;
            _viewModel = DataContext as MainWindowViewModel;

            if (_viewModel == null)
            {
                MessageBox.Show(GetResourceString("UnableToAccessViewModel"), GetResourceString("Error"));
                return;
            }

            StartTraceButton.IsEnabled = false;
            DriveComboBox.IsEnabled = false;
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = GetResourceString("StartingAnalysis");

            StartLogoSpin();

            await Task.Delay(500);

            await FadeOutStatusText();

            StatusText.Text = string.Format(GetResourceString("AnalyzingDrive"), _selectedDrive.DriveLetter);
            await FadeInStatusText();

            _dotsCount = 0;
            DotsText.Text = "";
            _dotsTimer.Start();

            await AnalyzeDriveAsync();
        }

        private async Task AnalyzeDriveAsync()
        {
            try
            {
                StatusText.Text = GetResourceString("ScanningExistingFiles");

                var analyzer = new FATAnalyzer(_selectedDrive.DriveLetter);
                var allFiles = new System.Collections.Generic.List<FileEntry>();

                var existingFiles = await Task.Run(() => analyzer.GetExistingFiles());
                lock (allFiles)
                {
                    allFiles.AddRange(existingFiles);
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = GetResourceString("ScanningDeletedFiles");
                });

                await Task.Run(() =>
                {
                    try
                    {
                        var deletedFiles = analyzer.GetDeletedFiles();
                        analyzer.DetectReplacedFiles(existingFiles, deletedFiles);
                        lock (allFiles)
                        {
                            allFiles.AddRange(deletedFiles);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(GetResourceString("AdminPrivilegesRequired"), 
                                            GetResourceString("LimitedAccess"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error scanning deleted files: {ex.Message}");
                    }
                });

                _dotsTimer.Stop();
                StatusText.Text = GetResourceString("AnalysisComplete");
                DotsText.Text = "";

                StopLogoSpin();

                await Task.Delay(500);
                await FadeOutAndNavigateToResults(allFiles);
            }
            catch (Exception ex)
            {
                _dotsTimer.Stop();
                DotsText.Text = "";
                StatusText.Text = string.Format(GetResourceString("ErrorAnalyzingDrive"), ex.Message);
                StartTraceButton.IsEnabled = true;
                DriveComboBox.IsEnabled = true;
                _isTraceStarted = false;

                MessageBox.Show(string.Format(GetResourceString("ErrorAnalyzingDrive"), ex.Message), 
                                GetResourceString("AnalysisError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task FadeOutStatusText()
        {
            await AnimateOpacity(StatusPanel, 1.0, 0.0, 0.5);
        }

        private async Task FadeInStatusText()
        {
            await AnimateOpacity(StatusPanel, 0.0, 1.0, 0.5);
        }

        private async Task FadeOutAndNavigateToResults(System.Collections.Generic.List<FileEntry> files)
        {
            await AnimateOpacity(MainGrid, 1.0, 0.0, 0.5);
            _viewModel?.NavigateToFATAnalyzer(files, _selectedDrive.DriveLetter, _selectedDrive.FileSystem, _selectedDrive.Label);
        }

        private async Task AnimateOpacity(UIElement element, double from, double to, double durationSeconds)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                EasingFunction = new QuadraticEase { EasingMode = from > to ? EasingMode.EaseOut : EasingMode.EaseIn }
            };

            var tcs = new TaskCompletionSource<bool>();
            animation.Completed += (s, e) => tcs.SetResult(true);
            element.BeginAnimation(UIElement.OpacityProperty, animation);

            await tcs.Task;
        }

        private void ShowStatus(string message)
        {
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = message;
                DotsText.Text = "";
            }

            private void StartLogoSpin()
            {
                if (_logoSpinStoryboard != null)
                return;

            var logoImage = (Image)this.FindName("WelcomeLogoImage");
            if (logoImage == null)
                return;

            var storyboard = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            var rotationAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1.2),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new PowerEase
                {
                    EasingMode = EasingMode.EaseInOut,
                    Power = 2
                }
            };

            Storyboard.SetTarget(rotationAnimation, logoImage);
            Storyboard.SetTargetProperty(rotationAnimation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
            storyboard.Children.Add(rotationAnimation);

            _logoSpinStoryboard = storyboard;
                _logoSpinStoryboard.Begin();
            }

            private void StopLogoSpin()
            {
                if (_logoSpinStoryboard == null)
                return;

            var logoImage = (Image)this.FindName("WelcomeLogoImage");
            if (logoImage == null)
                return;

            _logoSpinStoryboard.Stop();
            
            var resetAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            if (logoImage.RenderTransform is RotateTransform rotateTransform)
            {
                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, resetAnimation);
            }
            _logoSpinStoryboard = null;
        }
    }
}


