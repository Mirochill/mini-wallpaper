using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MiniWallpaper.Native
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var app = new System.Windows.Application();
            app.Run(new WallpaperWindow());
        }
    }

    internal sealed class WallpaperWindow : Window
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "MiniWallpaper";
        private readonly string _configDirectory;
        private readonly string _configPath;
        private readonly Grid _root;
        private readonly MediaElement _media;
        private readonly System.Windows.Controls.Image _image;
        private readonly DispatcherTimer _timer;
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _pauseItem;
        private readonly ToolStripMenuItem _startupItem;
        private GifAnimation _gifAnimation;
        private bool _manualPaused;
        private bool _fullscreenPaused;

        public WallpaperWindow()
        {
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniWallpaper");
            _configPath = Path.Combine(_configDirectory, "wallpaper.txt");

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Left = SystemParameters.PrimaryScreenWidth > 0 ? 0 : SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.PrimaryScreenHeight > 0 ? 0 : SystemParameters.VirtualScreenTop;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Background = System.Windows.Media.Brushes.Black;

            _root = new Grid
            {
                Background = System.Windows.Media.Brushes.Black
            };

            _media = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Volume = 0.0
            };
            _media.MediaEnded += delegate
            {
                _media.Position = TimeSpan.Zero;
                _media.Play();
            };

            _image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.UniformToFill,
                Visibility = Visibility.Collapsed
            };

            _root.Children.Add(_media);
            _root.Children.Add(_image);
            Content = _root;

            SourceInitialized += delegate
            {
                NativeMethods.AttachToDesktop(new WindowInteropHelper(this).Handle);
            };
            Loaded += delegate
            {
                LoadInitialWallpaper();
            };
            Closed += delegate
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            };

            _pauseItem = new ToolStripMenuItem("Mettre en pause", null, delegate
            {
                _manualPaused = !_manualPaused;
                _pauseItem.Text = _manualPaused ? "Reprendre" : "Mettre en pause";
                ApplyPlaybackState();
            });

            _startupItem = new ToolStripMenuItem("Lancer au démarrage")
            {
                CheckOnClick = true,
                Checked = StartupEnabled()
            };
            _startupItem.CheckedChanged += delegate
            {
                SetStartup(_startupItem.Checked);
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Choisir un fond…", null, delegate
            {
                ChooseWallpaper();
            }));
            menu.Items.Add(_pauseItem);
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quitter", null, delegate
            {
                Close();
            }));

            _trayIcon = new NotifyIcon
            {
                Text = "Mini Wallpaper",
                Icon = SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += delegate
            {
                var fullscreen = NativeMethods.ForegroundWindowIsFullscreen();
                if (fullscreen != _fullscreenPaused)
                {
                    _fullscreenPaused = fullscreen;
                    ApplyPlaybackState();
                }
            };
            _timer.Start();
        }

        private void LoadInitialWallpaper()
        {
            var path = LoadWallpaperPath();
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                path = DefaultWallpaperPath();
            }

            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ChooseWallpaper();
                return;
            }

            SetWallpaper(path);
        }

        private string LoadWallpaperPath()
        {
            if (!File.Exists(_configPath))
            {
                return null;
            }

            return File.ReadAllText(_configPath).Trim();
        }

        private string DefaultWallpaperPath()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "Gifs", "wallpaper.mp4");
        }

        private void ChooseWallpaper()
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Title = "Choisir un fond animé";
                dialog.Filter = "Fonds animés|*.mp4;*.wmv;*.avi;*.mov;*.gif|Vidéos|*.mp4;*.wmv;*.avi;*.mov|GIF animés|*.gif|Tous les fichiers|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SetWallpaper(dialog.FileName);
                }
            }
        }

        private void SetWallpaper(string path)
        {
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(_configPath, path);
            _manualPaused = false;
            _pauseItem.Text = "Mettre en pause";

            var extension = Path.GetExtension(path);
            if (String.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                ShowGif(path);
            }
            else
            {
                ShowVideo(path);
            }

            ApplyPlaybackState();
        }

        private void ApplyPlaybackState()
        {
            if (_manualPaused || _fullscreenPaused)
            {
                if (_gifAnimation != null)
                {
                    _gifAnimation.Pause();
                }
                else
                {
                    _media.Pause();
                }
            }
            else
            {
                if (_gifAnimation != null)
                {
                    _gifAnimation.Play();
                }
                else
                {
                    _media.Play();
                }
            }
        }

        private void ShowVideo(string path)
        {
            StopGif();
            _image.Visibility = Visibility.Collapsed;
            _media.Visibility = Visibility.Visible;
            _media.Source = new Uri(path, UriKind.Absolute);
            _media.Play();
        }

        private void ShowGif(string path)
        {
            _media.Stop();
            _media.Source = null;
            _media.Visibility = Visibility.Collapsed;
            _image.Visibility = Visibility.Visible;

            StopGif();
            _gifAnimation = new GifAnimation(_image, path);
            _gifAnimation.Play();
        }

        private void StopGif()
        {
            if (_gifAnimation != null)
            {
                _gifAnimation.Dispose();
                _gifAnimation = null;
            }
        }

        private static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                return key != null && key.GetValue(RunValueName) != null;
            }
        }

        private static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                {
                    var executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key.SetValue(RunValueName, "\"" + executable + "\"");
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }
    }

    internal sealed class GifAnimation : IDisposable
    {
        private readonly System.Windows.Controls.Image _image;
        private readonly BitmapDecoder _decoder;
        private readonly DispatcherTimer _timer;
        private int _frameIndex;

        public GifAnimation(System.Windows.Controls.Image image, string path)
        {
            _image = image;
            _decoder = BitmapDecoder.Create(
                new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            _timer = new DispatcherTimer();
            _timer.Tick += delegate
            {
                AdvanceFrame();
            };

            if (_decoder.Frames.Count > 0)
            {
                _image.Source = _decoder.Frames[0];
                _timer.Interval = FrameDelay(_decoder.Frames[0]);
            }
        }

        public void Play()
        {
            if (_decoder.Frames.Count <= 1)
            {
                return;
            }

            _timer.Start();
        }

        public void Pause()
        {
            _timer.Stop();
        }

        public void Dispose()
        {
            _timer.Stop();
            _image.Source = null;
        }

        private void AdvanceFrame()
        {
            if (_decoder.Frames.Count == 0)
            {
                return;
            }

            _frameIndex = (_frameIndex + 1) % _decoder.Frames.Count;
            var frame = _decoder.Frames[_frameIndex];
            _image.Source = frame;
            _timer.Interval = FrameDelay(frame);
        }

        private static TimeSpan FrameDelay(BitmapFrame frame)
        {
            var metadata = frame.Metadata as BitmapMetadata;
            object delayValue = null;

            try
            {
                if (metadata != null && metadata.ContainsQuery("/grctlext/Delay"))
                {
                    delayValue = metadata.GetQuery("/grctlext/Delay");
                }
            }
            catch
            {
                delayValue = null;
            }

            var hundredths = delayValue == null ? 10 : Convert.ToInt32(delayValue);
            if (hundredths < 2)
            {
                hundredths = 10;
            }

            return TimeSpan.FromMilliseconds(hundredths * 10);
        }
    }

    internal static class NativeMethods
    {
        private const uint SpawnWorkerW = 0x052C;
        private const uint SmtoNormal = 0x0000;
        private const uint MonitorDefaultToNearest = 0x00000002;

        public static void AttachToDesktop(IntPtr window)
        {
            var progman = FindWindow("Progman", null);
            IntPtr result;
            SendMessageTimeout(progman, SpawnWorkerW, IntPtr.Zero, IntPtr.Zero, SmtoNormal, 1000, out result);

            IntPtr workerW = IntPtr.Zero;
            EnumWindows(delegate(IntPtr topHandle, IntPtr lParam)
            {
                var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView != IntPtr.Zero)
                {
                    workerW = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);

            SetParent(window, workerW == IntPtr.Zero ? progman : workerW);
        }

        public static bool ForegroundWindowIsFullscreen()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            RECT rect;
            if (!GetWindowRect(foreground, out rect))
            {
                return false;
            }

            return rect.Left <= monitorInfo.rcMonitor.Left
                && rect.Top <= monitorInfo.rcMonitor.Top
                && rect.Right >= monitorInfo.rcMonitor.Right
                && rect.Bottom >= monitorInfo.rcMonitor.Bottom;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(
            IntPtr parentHandle,
            IntPtr childAfter,
            string className,
            string windowTitle);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO monitorInfo);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }
}
