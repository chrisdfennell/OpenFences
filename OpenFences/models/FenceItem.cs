using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace OpenFences
{
    public class FenceItem : INotifyPropertyChanged
    {
        public string Path { get; set; } = "";
        public string DisplayName { get; set; } = "";

        private ImageSource? _icon;
        public ImageSource? Icon
        {
            get => _icon;
            set => Set(ref _icon, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
