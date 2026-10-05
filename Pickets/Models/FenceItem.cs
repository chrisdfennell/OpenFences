using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Pickets
{
    public class FenceItem : INotifyPropertyChanged
    {
        public string Path { get; set; } = "";
        public string DisplayName { get; set; } = "";

        // What anything that falls back to text (accessibility tools, debugging) shows.
        public override string ToString() => DisplayName;

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

        // List view's right-hand text ("12 KB · Mar 3"); filled in off the UI thread.
        private string _details = "";
        public string Details
        {
            get => _details;
            set => Set(ref _details, value);
        }

        // Faded out while a search is running and this item doesn't match.
        private bool _isDimmed;
        public bool IsDimmed
        {
            get => _isDimmed;
            set => Set(ref _isDimmed, value);
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
