using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using Vortex.UI.Views;

// ViewModels for application UI binding
namespace Vortex.UI.ViewModels
{
    // Main window navigation and lifecycle manager
    public partial class MainWindowViewModel : INotifyPropertyChanged
    {
        // Navigation frame for page routing
        private Frame _mainFrame;
        // Indicates if data loaded successfully
        private bool _isDataLoaded = false;

        // Gets or sets data loaded state
        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            set
            {
                if (_isDataLoaded != value)
                {
                    _isDataLoaded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanNavigateToDashboard));
                }
            }
        }

        // Checks if navigation to dashboard allowed
        public bool CanNavigateToDashboard => IsDataLoaded;

        // Initializes viewmodel and command bindings
        public MainWindowViewModel()
        {
        }


        // Assigns navigation frame reference
        public void SetFrame(Frame frame)
        {
            _mainFrame = frame;
            NavigateToWelcome();
        }

        // Navigates to welcome page
        public void NavigateToWelcome()
        {
            if (_mainFrame != null)
            {
                var welcomeView = new WelcomeView { DataContext = this };
                _mainFrame.Navigate(welcomeView);
            }
        }


        // Navigates to FAT analyzer with file data
        public void NavigateToFATAnalyzer(System.Collections.Generic.List<Drives.Models.FileEntry> files, string drivePath, string partitionName = null, string volumeLabel = null)
        {
            if (_mainFrame != null)
            {
                var fatAnalyzerView = new FATAnalyzerView();
                fatAnalyzerView.LoadFiles(files, drivePath, partitionName, volumeLabel);
                _mainFrame.Navigate(fatAnalyzerView);
            }
        }


        public void RefreshCurrentView()
        {
            IsDataLoaded = false;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            NavigateToWelcome();
        }

        // Reloads current active view
        public void ReloadCurrentView()
        {
            NavigateToWelcome();
        }

        // Property change notification event
        public event PropertyChangedEventHandler PropertyChanged;

        // Raises property changed event
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple ICommand implementation for actions
    public class RelayCommand : ICommand
    {
        // Action to execute
        private readonly Action _execute;
        // Predicate for execution availability
        private readonly Func<bool> _canExecute;

        // Initializes command with action and predicate
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // Execution state change notification event
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        // Determines if command can execute
        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        // Executes command action
        public void Execute(object parameter) => _execute();
    }
}