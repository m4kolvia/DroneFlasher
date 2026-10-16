using System.ComponentModel;

namespace DroneFlasher.Models
{
    public sealed class ThemeOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Name { get; }
        public string Description { get; }
        public string Accent { get; }
        public string Key { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ThemeOption(string name, string description, string accent, string key)
        {
            Name = name;
            Description = description;
            Accent = accent;
            Key = key;
        }
    }
}
