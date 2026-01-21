using System.Windows.Controls;
using Vortex.UI.ViewModels;
using Vortex.UI.Helpers;

namespace Vortex.UI.Views
{
    public partial class DashboardView : Page
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        private void CopyValue_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var dataGrid = contextMenu?.PlacementTarget as DataGrid;

            if (dataGrid != null)
            {
                DataGridContextMenuHelper.CopyValue(dataGrid);
            }
        }

        private void CopyRow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var dataGrid = contextMenu?.PlacementTarget as DataGrid;

            if (dataGrid != null)
            {
                DataGridContextMenuHelper.CopyRow(dataGrid);
            }
        }
    }
}