using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevitTrueGltf
{
    public class ExportSettingsVM : INotifyPropertyChanged
    {
        private bool _isUpdatingFromPreset = false;

        // Model
        private ExportSettings _settings;
        private ExportPreset _selectedPreset;

        public ExportSettingsVM(ExportSettings settings)
        {
            _settings = settings;
            // Start with Balanced
            ApplyPreset(ExportPreset.Balanced);
        }

        #region Properties (Bindings)

        public string ExportFilePath
        {
            get => _settings.ExportFilePath;
            set { if (_settings.ExportFilePath != value) { _settings.ExportFilePath = value; OnPropertyChanged(); } }
        }

        public ExportPreset SelectedPreset
        {
            get => _selectedPreset;
            set { if (_selectedPreset != value) { _selectedPreset = value; OnPropertyChanged(); if (!_isUpdatingFromPreset) ApplyPreset(value); } }
        }

        public bool ExportFloors
        {
            get => _settings.ExportFloors;
            set { if (_settings.ExportFloors != value) { _settings.ExportFloors = value; OnPropertyChanged(); } }
        }

        public bool VisibleElementsOnly
        {
            get => _settings.VisibleElementsOnly;
            set { if (_settings.VisibleElementsOnly != value) { _settings.VisibleElementsOnly = value; OnPropertyChanged(); } }
        }

        public bool ExportBimProperties
        {
            get => _settings.ExportBimProperties;
            set { if (_settings.ExportBimProperties != value) { _settings.ExportBimProperties = value; OnPropertyChanged(); } }
        }

        public bool IncludeLinkedModels
        {
            get => _settings.IncludeLinkedModels;
            set { if (_settings.IncludeLinkedModels != value) { _settings.IncludeLinkedModels = value; OnPropertyChanged(); } }
        }

        public MaterialMode MaterialExportMode
        {
            get => _settings.MaterialExportMode;
            set { if (_settings.MaterialExportMode != value) { _settings.MaterialExportMode = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        public bool UseKtx2
        {
            get => _settings.UseKtx2TextureCompression;
            set { if (_settings.UseKtx2TextureCompression != value) { _settings.UseKtx2TextureCompression = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        public int TextureQuality
        {
            get => _settings.TextureQuality;
            set { if (_settings.TextureQuality != value) { _settings.TextureQuality = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        public bool UseMeshopt
        {
            get => _settings.UseMeshoptimizer;
            set { if (_settings.UseMeshoptimizer != value) { _settings.UseMeshoptimizer = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        public bool UseVertexPrecision
        {
            get => _settings.UseCustomVertexPrecision;
            set { if (_settings.UseCustomVertexPrecision != value) { _settings.UseCustomVertexPrecision = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        public VertexPrecision VertexPositionPrecision
        {
            get => _settings.VertexPositionPrecision;
            set { if (_settings.VertexPositionPrecision != value) { _settings.VertexPositionPrecision = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        public double SimplificationRatio
        {
            get => _settings.SimplificationRatio;
            set { if (_settings.SimplificationRatio != value) { _settings.SimplificationRatio = value; OnPropertyChanged(); UpdatePresetToCustom(); } }
        }

        #endregion

        #region Logic

        private void ApplyPreset(ExportPreset preset)
        {
            if (preset == ExportPreset.Custom) return;

            _isUpdatingFromPreset = true;
            _selectedPreset = preset;

            switch (preset)
            {
                case ExportPreset.Draft:
                    MaterialExportMode = MaterialMode.ColorOnly;
                    UseMeshopt = true;
                    UseVertexPrecision = true;
                    VertexPositionPrecision = VertexPrecision.Standard;
                    UseKtx2 = false;
                    SimplificationRatio = 0.0;
                    break;
                case ExportPreset.Balanced:
                    MaterialExportMode = MaterialMode.Texture;
                    UseKtx2 = true;
                    TextureQuality = 8;
                    UseMeshopt = false;
                    UseVertexPrecision = true;
                    VertexPositionPrecision = VertexPrecision.High;
                    SimplificationRatio = 0.0;
                    break;
                case ExportPreset.HighFidelity:
                    MaterialExportMode = MaterialMode.Texture;
                    UseKtx2 = true;
                    TextureQuality = 10;
                    UseMeshopt = false;
                    UseVertexPrecision = true;
                    VertexPositionPrecision = VertexPrecision.VeryHigh;
                    SimplificationRatio = 0.0;
                    break;
            }

            // Notify SelectedPreset change
            OnPropertyChanged(nameof(SelectedPreset));

            _isUpdatingFromPreset = false;
        }

        private void UpdatePresetToCustom()
        {
            if (!_isUpdatingFromPreset && _selectedPreset != ExportPreset.Custom)
            {
                _selectedPreset = ExportPreset.Custom;
                OnPropertyChanged(nameof(SelectedPreset));
            }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
