using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        private readonly string _optimizedDirectory;
        private readonly string _ffmpegPath;
        private readonly Grid _root;
        private readonly MediaElement _media;
        private readonly System.Windows.Controls.Image _image;
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripMenuItem _pauseItem;
        private readonly ToolStripMenuItem _startupItem;
        private GifAnimation _gifAnimation;
        private string _videoPath;
        private bool _videoLoaded;
        private bool _manualPaused;

        public WallpaperWindow()
        {
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniWallpaper");
            _configPath = Path.Combine(_configDirectory, "wallpaper.txt");
            _optimizedDirectory = Path.Combine(_configDirectory, "optimized");
            _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

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
                Volume = 0.0,
                ScrubbingEnabled = false
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
                SleepVideo();
                StopGif();
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
            path = PrepareWallpaper(path);

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

        private string PrepareWallpaper(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return path;
            }

            if (IsOptimizedWallpaper(path))
            {
                return path;
            }

            var extension = Path.GetExtension(path);
            if (!IsOptimizableMedia(extension) || !File.Exists(_ffmpegPath))
            {
                return path;
            }

            Directory.CreateDirectory(_optimizedDirectory);
            var optimizedPath = Path.Combine(_optimizedDirectory, OptimizedFileName(path));
            if (File.Exists(optimizedPath))
            {
                return optimizedPath;
            }

            var temporaryPath = optimizedPath + ".tmp.mp4";
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                RunFfmpeg(path, temporaryPath);
                if (File.Exists(temporaryPath) && new FileInfo(temporaryPath).Length > 0)
                {
                    if (File.Exists(optimizedPath))
                    {
                        File.Delete(optimizedPath);
                    }

                    File.Move(temporaryPath, optimizedPath);
                    return optimizedPath;
                }
            }
            catch
            {
                // If an import cannot be optimized, keep the original usable instead of failing the wallpaper change.
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return path;
        }

        private bool IsOptimizedWallpaper(string path)
        {
            var optimizedRoot = Path.GetFullPath(_optimizedDirectory)
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(optimizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOptimizableMedia(string extension)
        {
            return String.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".wmv", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".mov", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase);
        }

        private string OptimizedFileName(string path)
        {
            var info = new FileInfo(path);
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var fingerprint = String.Join("|",
                Path.GetFullPath(path),
                info.Length.ToString(),
                info.LastWriteTimeUtc.Ticks.ToString(),
                screen.Width.ToString(),
                screen.Height.ToString(),
                "fps30",
                "crf23");

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString() + ".mp4";
            }
        }

        private void RunFfmpeg(string sourcePath, string outputPath)
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var filter = String.Format(
                "scale=w='min(iw,{0})':h='min(ih,{1})':force_original_aspect_ratio=decrease,fps=30",
                screen.Width,
                screen.Height);
            var arguments = String.Join(" ",
                "-y",
                "-i", Quote(sourcePath),
                "-vf", Quote(filter),
                "-c:v", "libx264",
                "-preset", "slow",
                "-crf", "23",
                "-pix_fmt", "yuv420p",
                "-movflags", "+faststart",
                "-an",
                Quote(outputPath));

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Impossible de lancer ffmpeg.");
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("ffmpeg a échoué.");
                }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void ApplyPlaybackState()
        {
            if (_gifAnimation != null)
            {
                if (_manualPaused)
                {
                    _gifAnimation.Pause();
                }
                else
                {
                    _gifAnimation.Play();
                }

                return;
            }

            WakeVideo();
            if (_manualPaused)
            {
                _media.Pause();
            }
            else
            {
                _media.Play();
            }
        }

        private void ShowVideo(string path)
        {
            StopGif();
            SleepVideo();
            _videoPath = path;
            _image.Visibility = Visibility.Collapsed;
            _media.Visibility = Visibility.Visible;
        }

        private void ShowGif(string path)
        {
            SleepVideo();
            _videoPath = null;
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

        private void WakeVideo()
        {
            if (String.IsNullOrWhiteSpace(_videoPath))
            {
                return;
            }

            if (!_videoLoaded)
            {
                _media.Source = new Uri(_videoPath, UriKind.Absolute);
                _videoLoaded = true;
            }
        }

        private void SleepVideo()
        {
            if (!_videoLoaded)
            {
                return;
            }

            _media.Stop();
            _media.Source = null;
            _videoLoaded = false;
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

    }
}
