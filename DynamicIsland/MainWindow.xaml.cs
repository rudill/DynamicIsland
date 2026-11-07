using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland
{
    public partial class MainWindow : Window
    {
        #region Win32 Extended Window Styles
        private const int GwlExstyle = -20;
        private const int WsExToolwindow = 0x00000080;   // Hides window from Alt+Tab
        private const int WsExNoactivate = 0x08000000;   // Clicking will NOT steal focus from active app

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        #endregion

        private bool _isExpanded;
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly DispatcherTimer _progressTimer;

        public MainWindow()
        {
            InitializeComponent();
            PositionAtTopCenter();

            // Recalculate position if resolution or monitor setup changes
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.Invoke(PositionAtTopCenter);

            // Progress bar update timer (runs every second when playing)
            _progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _progressTimer.Tick += (s, e) => UpdateTimeline();

            // Initialize Windows Media Transport Controls (GSMTC)
            InitializeMediaListener();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Apply Win32 flags: Alt+Tab exclusion and click non-activation
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GwlExstyle);
            SetWindowLong(hwnd, GwlExstyle, exStyle | WsExToolwindow | WsExNoactivate);
        }

        private void PositionAtTopCenter()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = (screenWidth - this.Width) / 2;
            this.Top = 0;
        }

        #region Windows Media Session (GSMTC) Integration

        private async void InitializeMediaListener()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager == null) return;

                // Listen for active media session changes (switching between Spotify, Chrome, Edge, etc.)
                _sessionManager.CurrentSessionChanged += async (s, e) =>
                {
                    await Dispatcher.InvokeAsync(RefreshCurrentSessionAsync);
                };

                await RefreshCurrentSessionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing GSMTC: {ex.Message}");
            }
        }

        private async Task RefreshCurrentSessionAsync()
        {
            if (_sessionManager == null) return;

            // Unhook old session events if any
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
            }

            _currentSession = _sessionManager.GetCurrentSession();

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;

                SourceAppText.Text = CleanAppId(_currentSession.SourceAppUserModelId);
                await UpdateMediaPropertiesAsync();
                UpdatePlaybackInfo();
                UpdateTimeline();
            }
            else
            {
                // No active playback session
                CompactTrackText.Text = "Dynamic Island";
                CompactStatusIcon.Text = "•";
                LiveIndicatorDot.Fill = System.Windows.Media.Brushes.Gray;

                SourceAppText.Text = "Dynamic Island";
                ExpandedTrackTitle.Text = "No Media Playing";
                ExpandedArtist.Text = "Play something on Spotify, Chrome, or Apple Music";
                TimelineText.Text = "--:-- / --:--";
                PlaybackProgressBar.Value = 0;
                PlayPauseButton.Content = "▶";

                AlbumArtImage.Source = null;
                AlbumArtImage.Visibility = Visibility.Collapsed;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
                _progressTimer.Stop();
            }
        }

        private async void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await Dispatcher.InvokeAsync(UpdateMediaPropertiesAsync);
        }

        private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            Dispatcher.Invoke(UpdatePlaybackInfo);
        }

        private void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            Dispatcher.Invoke(UpdateTimeline);
        }

        private async Task UpdateMediaPropertiesAsync()
        {
            if (_currentSession == null) return;

            try
            {
                var mediaProps = await _currentSession.TryGetMediaPropertiesAsync();
                if (mediaProps == null) return;

                string title = string.IsNullOrWhiteSpace(mediaProps.Title) ? "Unknown Title" : mediaProps.Title;
                string artist = string.IsNullOrWhiteSpace(mediaProps.Artist) ? "Unknown Artist" : mediaProps.Artist;

                CompactTrackText.Text = title;
                ExpandedTrackTitle.Text = title;
                ExpandedArtist.Text = artist;

                // Load album art thumbnail
                await LoadThumbnailAsync(mediaProps.Thumbnail);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating media properties: {ex.Message}");
            }
        }

        private void UpdatePlaybackInfo()
        {
            if (_currentSession == null) return;

            var playback = _currentSession.GetPlaybackInfo();
            if (playback != null)
            {
                bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                PlayPauseButton.Content = isPlaying ? "⏸" : "▶";
                CompactStatusIcon.Text = isPlaying ? "♫" : "⏸";
                LiveIndicatorDot.Fill = isPlaying 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 185, 84)) 
                    : System.Windows.Media.Brushes.Orange;

                if (isPlaying && !_progressTimer.IsEnabled)
                {
                    _progressTimer.Start();
                }
                else if (!isPlaying && _progressTimer.IsEnabled)
                {
                    _progressTimer.Stop();
                }
            }
        }

        private void UpdateTimeline()
        {
            if (_currentSession == null) return;

            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null && timeline.EndTime > TimeSpan.Zero)
                {
                    PlaybackProgressBar.Maximum = timeline.EndTime.TotalSeconds;
                    PlaybackProgressBar.Value = Math.Clamp(timeline.Position.TotalSeconds, 0, timeline.EndTime.TotalSeconds);
                    TimelineText.Text = $"{timeline.Position:mm\\:ss} / {timeline.EndTime:mm\\:ss}";
                }
            }
            catch
            {
                // Timeline query might fail for some web streams
            }
        }

        private async Task LoadThumbnailAsync(IRandomAccessStreamReference? thumbnailRef)
        {
            if (thumbnailRef == null)
            {
                AlbumArtImage.Source = null;
                AlbumArtImage.Visibility = Visibility.Collapsed;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                using IRandomAccessStreamWithContentType ras = await thumbnailRef.OpenReadAsync();
                using Stream stream = ras.AsStreamForRead();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = memoryStream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                AlbumArtImage.Source = bitmap;
                AlbumArtImage.Visibility = Visibility.Visible;
                AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                AlbumArtImage.Source = null;
                AlbumArtImage.Visibility = Visibility.Collapsed;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private static string CleanAppId(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return "Now Playing";
            if (appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (appId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
            if (appId.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            if (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
            return "Now Playing";
        }

        #endregion

        #region Media Control Button Clicks

        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
                UpdatePlaybackInfo();
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }

        #endregion

        #region Storyboard Animations

        private void AnimateIsland(double targetWidth, double targetHeight, double targetRadius, bool expand)
        {
            var duration = TimeSpan.FromMilliseconds(380);

            // iOS fluid cubic ease-out
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var storyboard = new Storyboard();

            // 1. Width Animation
            var widthAnim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(widthAnim, IslandCapsule);
            Storyboard.SetTargetProperty(widthAnim, new PropertyPath(Border.WidthProperty));
            storyboard.Children.Add(widthAnim);

            // 2. Height Animation
            var heightAnim = new DoubleAnimation
            {
                To = targetHeight,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(heightAnim, IslandCapsule);
            Storyboard.SetTargetProperty(heightAnim, new PropertyPath(Border.HeightProperty));
            storyboard.Children.Add(heightAnim);

            // 3. Corner Radius Animation
            var radiusAnim = new CornerRadiusAnimation
            {
                To = new CornerRadius(targetRadius),
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(radiusAnim, IslandCapsule);
            Storyboard.SetTargetProperty(radiusAnim, new PropertyPath(Border.CornerRadiusProperty));
            storyboard.Children.Add(radiusAnim);

            // 4. Content Opacity Cross-fades
            var fadeOutDuration = TimeSpan.FromMilliseconds(120);
            var fadeInDuration = TimeSpan.FromMilliseconds(240);

            var compactOpacity = new DoubleAnimation
            {
                To = expand ? 0.0 : 1.0,
                Duration = expand ? fadeOutDuration : fadeInDuration
            };
            Storyboard.SetTarget(compactOpacity, CompactContent);
            Storyboard.SetTargetProperty(compactOpacity, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(compactOpacity);

            var expandedOpacity = new DoubleAnimation
            {
                To = expand ? 1.0 : 0.0,
                Duration = expand ? fadeInDuration : fadeOutDuration
            };
            Storyboard.SetTarget(expandedOpacity, ExpandedContent);
            Storyboard.SetTargetProperty(expandedOpacity, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(expandedOpacity);

            ExpandedContent.IsHitTestVisible = expand;
            CompactContent.IsHitTestVisible = !expand;

            storyboard.Begin();
        }

        private void IslandCapsule_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isExpanded)
            {
                _isExpanded = true;
                // Expand to Rich Widget size
                AnimateIsland(targetWidth: 390, targetHeight: 140, targetRadius: 28, expand: true);
            }
        }

        private void IslandCapsule_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isExpanded)
            {
                _isExpanded = false;
                // Return to compact Pill size
                AnimateIsland(targetWidth: 180, targetHeight: 36, targetRadius: 18, expand: false);
            }
        }

        #endregion
    }

    public class CornerRadiusAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(CornerRadius?), typeof(CornerRadiusAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(CornerRadius?), typeof(CornerRadiusAnimation));

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(CornerRadiusAnimation));

        public CornerRadius? From
        {
            get => (CornerRadius?)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public CornerRadius? To
        {
            get => (CornerRadius?)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction EasingFunction
        {
            get => (IEasingFunction)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override Type TargetPropertyType => typeof(CornerRadius);

        protected override Freezable CreateInstanceCore() => new CornerRadiusAnimation();

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            CornerRadius from = From ?? (CornerRadius)defaultOriginValue;
            CornerRadius to = To ?? (CornerRadius)defaultDestinationValue;

            double progress = animationClock.CurrentProgress.GetValueOrDefault();
            if (EasingFunction != null)
            {
                progress = EasingFunction.Ease(progress);
            }

            return new CornerRadius(
                from.TopLeft + (to.TopLeft - from.TopLeft) * progress,
                from.TopRight + (to.TopRight - from.TopRight) * progress,
                from.BottomRight + (to.BottomRight - from.BottomRight) * progress,
                from.BottomLeft + (to.BottomLeft - from.BottomLeft) * progress
            );
        }
    }
}
