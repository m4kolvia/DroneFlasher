using System.Windows.Media;

namespace DroneFlasher.Models
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error,
        Gray
    }

    public record LogEntry(string Text, Brush Color)
    {

        public LogEntry(string text, LogLevel level) : this(text, GetBrush(level)) { }

        private static Brush GetBrush(LogLevel level) => level switch
        {
            LogLevel.Success => Brushes.LimeGreen,
            LogLevel.Warning => Brushes.Orange,
            LogLevel.Error => Brushes.IndianRed,
            LogLevel.Gray => Brushes.Gray,
            _ => Brushes.DodgerBlue
        };
    }
}
