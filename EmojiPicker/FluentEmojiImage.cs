using System;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Image = System.Windows.Controls.Image;

namespace EmojiPicker
{
    internal static class FluentEmojiImage
    {
        internal static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
            "Source", typeof(string), typeof(FluentEmojiImage), new PropertyMetadata(null, OnSourceChanged));
        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(RequestState), typeof(FluentEmojiImage));

        public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);
        public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

        private sealed class RequestState
        {
            internal CancellationTokenSource? Cancellation;
            internal Task Loading = Task.CompletedTask;
            internal void Cancel()
            {
                Cancellation?.Cancel(); Cancellation?.Dispose(); Cancellation = null;
            }
        }

        private static void OnSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        {
            if (element is not Image image) { return; }
            if (image.GetValue(StateProperty) is not RequestState)
            {
                image.SetValue(StateProperty, new RequestState());
                image.Loaded += (_, _) => Start(image);
                image.Unloaded += (_, _) => Release(image);
                image.IsVisibleChanged += (_, _) => { if (image.IsVisible) { Start(image); } else { Release(image); } };
            }
            Start(image);
        }

        private static void Release(Image image)
        {
            ((RequestState)image.GetValue(StateProperty)).Cancel();
            image.Source = null;
        }

        internal static Task WhenLoadedAsync(Image image) => image.GetValue(StateProperty) is RequestState state ? state.Loading : Task.CompletedTask;

        private static void Start(Image image)
        {
            var state = (RequestState)image.GetValue(StateProperty);
            state.Cancel();
            state.Loading = Task.CompletedTask;
            var text = GetSource(image);
            if (!image.IsLoaded || !image.IsVisible || string.IsNullOrEmpty(text)) { image.Source = null; return; }
            if (FluentEmojiImages.TryGetCached(text, out var ready)) { image.Source = ready; return; }
            image.Source = SystemFallback(text);
            if (!FluentEmojiImages.HasImage(text)) { return; }
            var cancellation = new CancellationTokenSource();
            state.Cancellation = cancellation;
            var token = cancellation.Token;
            state.Loading = LoadAndAssignAsync(image, state, text, cancellation, token);
        }

        private static async Task LoadAndAssignAsync(Image image, RequestState state, string text,
            CancellationTokenSource cancellation, CancellationToken token)
        {
            try
            {
                var bitmap = await FluentEmojiImages.LoadAsync(text, token).ConfigureAwait(false);
                // Recycling can change the binding while decoding is in flight.
                if (token.IsCancellationRequested) { return; }
                await image.Dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested && ReferenceEquals(state.Cancellation, cancellation)
                        && image.IsLoaded && image.IsVisible && GetSource(image) == text && bitmap != null)
                    {
                        image.Source = bitmap;
                    }
                }, System.Windows.Threading.DispatcherPriority.Render, token).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Logger.LogAlways($"Fluent image binding failed: {error.GetType().Name}"); }
        }

        internal static ImageSource SystemFallback(string text)
        {
            var temporary = new Image();
            ImageSource? source = null;
            try
            {
                global::Emoji.Wpf.Image.SetSource(temporary, text);
                source = temporary.Source;
            }
            catch (Exception error) { Logger.Log($"System image fallback unavailable: {error.GetType().Name}"); }
            if (source == null || (source is DrawingImage drawing && (drawing.Drawing == null || drawing.Drawing.Bounds.IsEmpty)))
            {
                global::Emoji.Wpf.Image.SetSource(temporary, "❓");
                source = temporary.Source;
            }
            if (source == null)
            {
                var placeholder = new FormattedText("?", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 26, System.Windows.Media.Brushes.Gray, 1);
                source = new DrawingImage(new GeometryDrawing(System.Windows.Media.Brushes.Gray, null,
                    placeholder.BuildGeometry(new System.Windows.Point(0, 0))));
            }
            if (source?.CanFreeze == true && !source.IsFrozen) { source.Freeze(); }
            return source!;
        }
    }
}
