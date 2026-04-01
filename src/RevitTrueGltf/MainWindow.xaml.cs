using System;
using System.Windows;
using Microsoft.Win32;

namespace RevitTrueGltf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly ExportSettingsVM _viewModel;

        public MainWindow(ExportSettingsVM viewModel)
        {
            InitializeComponent();
            
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "glTF Binary (*.glb)|*.glb|glTF JSON (*.gltf)|*.gltf";
            saveFileDialog.Title = "Select Output Location";
            saveFileDialog.FileName = System.IO.Path.GetFileName(_viewModel.ExportFilePath);
            
            if (saveFileDialog.ShowDialog() == true)
            {
                _viewModel.ExportFilePath = saveFileDialog.FileName;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_viewModel.ExportFilePath))
            {
                Wpf.Ui.Controls.MessageBox messageBox = new Wpf.Ui.Controls.MessageBox();
                messageBox.Title = "Missing Path";
                messageBox.Content = "Please specify an output path before exporting.";
                messageBox.ShowDialogAsync();
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
