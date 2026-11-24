using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ChildSessionDesktop
{
    internal sealed class SessionHeader : ElementHost
    {
        internal SessionHeader(string title)
        {
            TabStop = false;
            AccessibleName = title;
            var text = new TextBlock
            {
                Text = title,
                Foreground = Brush(ChromeColors.Text),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(16, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new Grid { ClipToBounds = true };
            if (!SystemParameters.HighContrast)
            {
                // WPF composites this vector surface using its available hardware rendering tier.
                grid.Children.Add(new Path
                {
                    Data = System.Windows.Media.Geometry.Parse("M 0,66 L 39,0 52,0 13,66 Z M 27,66 L 66,0 70,0 31,66 Z"),
                    Fill = new SolidColorBrush(Color.FromArgb(76, 112, 209, 255)),
                    Width = 70,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 13, 0)
                });
            }
            grid.Children.Add(text);
            var background = SystemParameters.HighContrast ? (Brush)Brush(ChromeColors.Surface)
                : new LinearGradientBrush(Color.FromRgb(30, 46, 72), Color.FromRgb(28, 56, 74), 10);
            Child = new Border
            {
                Background = background,
                BorderBrush = Brush(ChromeColors.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                IsHitTestVisible = false,
                Focusable = false,
                Child = grid
            };
        }

        private static SolidColorBrush Brush(System.Drawing.Color color)
        {
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }
    }
}
