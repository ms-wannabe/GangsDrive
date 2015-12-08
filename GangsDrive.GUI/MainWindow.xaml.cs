using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GangsDrive.GUI
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        GangsDriveManager manager;
        int sftpIndex;

        public MainWindow()
        {
            InitializeComponent();
            manager = GangsDriveManager.Instance;
        }

        private void btnSftpMount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                sftpIndex = manager.AddDriver(new GangsSFTPDriver(
                    tbSftpHost.Text,
                    Convert.ToInt32(tbSftpPort.Text),
                    tbSftpUsername.Text,
                    tbSftpPassword.Password,
                    "s:\\"));
            }
            catch (FormatException)
            {
                MessageBox.Show("Invalid value : Port number");
                return;
            }
            manager.MountDriver(sftpIndex);
        }

        private void btnSftpUnmount_Click(object sender, RoutedEventArgs e)
        {
            manager.UnmountDriver(sftpIndex);
        }
    }
}
