using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TomsToolbox.Essentials;

namespace NuGetMonitor.View
{
    internal class HighlightingTextBlock : ContentControl
    {
        private static readonly FontWeight _bold = FontWeight.FromOpenTypeWeight(700);

        private readonly TextBlock _textBlock = new();

        public HighlightingTextBlock()
        {
            Content = _textBlock;
        }

        public object Text
        {
            get { return GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(object), typeof(HighlightingTextBlock),
            new PropertyMetadata(default(object), (o, args) => ((HighlightingTextBlock)o).Text_Changed(args.NewValue)));

        private void Text_Changed(object newValue)
        {
            CreateInlines(newValue, SearchText, HighLightBrush);
        }

        public object SearchText
        {
            get { return GetValue(SearchTextProperty); }
            set { SetValue(SearchTextProperty, value); }
        }
        public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
            nameof(SearchText), typeof(object), typeof(HighlightingTextBlock),
            new PropertyMetadata(default(object), (o, args) => ((HighlightingTextBlock)o).SearchText_Changed(args.NewValue)));

        private void SearchText_Changed(object newValue)
        {
            CreateInlines(Text, newValue, HighLightBrush);
        }

        public Brush HighLightBrush
        {
            get { return (Brush) GetValue(HighLightBrushProperty); }
            set { SetValue(HighLightBrushProperty, value); }
        }
        public static readonly DependencyProperty HighLightBrushProperty = DependencyProperty.Register(
            nameof(HighLightBrush), typeof(Brush), typeof(HighlightingTextBlock),
            new PropertyMetadata(default(Brush), (o, args) => ((HighlightingTextBlock)o).HighLightBrush_Changed((Brush)args.NewValue)));

        private void HighLightBrush_Changed(Brush newValue)
        {
            CreateInlines(Text, SearchText, newValue);
        }

        private void CreateInlines(object value, object parameter, Brush highlightBrush)
        {
            var inlines = _textBlock.Inlines;

            inlines.Clear();

            var text = value?.ToString();

            if (text.IsNullOrEmpty())
                return;

            var searchText = parameter?.ToString();

            if (searchText.IsNullOrEmpty())
            {
                inlines.Add(new Run(text));
                return;
            }

            var searchLength = searchText.Length;

            for (var index = 0; ;)
            {
                var pos = text.IndexOf(searchText, index, StringComparison.OrdinalIgnoreCase);

                if (pos < 0)
                {
                    inlines.Add(new Run(text.Substring(index)));
                    break;
                }

                if (pos > 0)
                {
                    inlines.Add(new Run(text.Substring(index, pos - index)));
                }

                inlines.Add(new Run(text.Substring(pos, searchLength)) { FontWeight = _bold, Foreground = highlightBrush });

                index = pos + searchLength;
            }
        }
    }
}