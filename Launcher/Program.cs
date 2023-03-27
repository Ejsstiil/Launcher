using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Launcher {
    class Program : ApplicationContext {
        private readonly Form _form;
        private int _exitCode;
        private Process _proc;
        private readonly CancellationToken _cancellationToken = new CancellationToken();
        private static int _cnt = 5;
        private readonly string _ico = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) + ".ico";

        private Program(string[] mainArgs) {
            if(mainArgs.Length == 0) {
                mainArgs = new[] { string.Empty };

                try {
                    mainArgs = File.ReadAllText(Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) + ".txt").Split(Convert.ToChar("\t"));
                } catch(Exception) {
                    var file = File.Create(Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]) + ".txt");
                    // ignored
                    mainArgs = new string[] { };
                }
            }
            if( mainArgs[0].Length == 0) mainArgs = new string[] { };

            _form = new Form {
                    Visible = true,
                    ShowInTaskbar = true,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    Width = 0,
                    Height = 0,
                    Top = -100,
                    Left = -100,
                    TransparencyKey = Color.Tan,
                    BackColor = Color.Tan,
                    StartPosition = FormStartPosition.Manual,
                    Text = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]),
            };
            SetIcon();
            SetBackgroundImage();
            _form.Closed += (sender, args) => {
                                _proc?.CloseMainWindow();
                                ExitThread();
                            };
            _form.Activated += (sender, args) => { ActicateSubProcess(_proc); };
            _form.Shown += async (sender, args) => { await ThreadNewMethod(mainArgs); };
            _form.Show();
        }

        private void SetIcon() {
            var stream = GetFileStream(_ico);
            if(stream != null) _form.Icon = new Icon(stream);
        }

        private void SetBackgroundImage() {
            var stream = GetFileStream(_ico);
            if(stream == null) return;
            _form.BackgroundImage = new Bitmap(stream);
            _form.BackgroundImageLayout = ImageLayout.Stretch;
        }

        private static FileStream GetFileStream(string fileName) {
            return !File.Exists(fileName) ? null : File.OpenRead(fileName);
        }

        [DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void ActicateSubProcess(Process proc) {
            SetForegroundWindow(proc.MainWindowHandle);
        }

        private async Task ThreadNewMethod(string[] eventArgs) {
            // Prepare the process to run
            var start = new ProcessStartInfo();


            if(eventArgs.Length > 0) {
                start.FileName = eventArgs[0];
                if(eventArgs.Length > 1) start.Arguments = eventArgs[1];
            } else
                start.FileName = "calc";

            var rest = "";
            if(eventArgs.Length > 1) rest = string.Join(" ", eventArgs, 1, eventArgs.Length - 1);
            start.Arguments = rest;


            // Do you want to show a console window?
            //start.WindowStyle = ProcessWindowStyle.Hidden;
            //start.CreateNoWindow = true;


            // Run the external process & wait for it to finish
            _proc = Process.Start(start);

            if(_proc != null) {
                Thread.Sleep(100);
                HideAndPosSlaveProcess(); //try immediatelly

                //then after every X sec
                RecurringTask(HideAndPosSlaveProcess, 5, _cancellationToken);

                await WaitForExitAsync(_proc, cancellationToken: _cancellationToken);
                // Retrieve the app's exit code
                _exitCode = _proc.ExitCode;
            }


            _form.Close();
        }

        private void HideAndPosSlaveProcess() {
            if(_cnt > 0) {
                HideWindowFromTaskbar(_proc.MainWindowHandle);
                _cnt--;
            }

            SetWindowPosition(_proc.MainWindowHandle);

            if(IsSlaveAppOnTop(_proc.MainWindowHandle)) {
                CaptureClientArea(_proc.MainWindowHandle);
            }
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        private static bool IsSlaveAppOnTop(IntPtr pMainWindow) {
            var handle = GetForegroundWindow();

            return handle == pMainWindow;
        }

        private static Task WaitForExitAsync(Process process, CancellationToken cancellationToken = default(CancellationToken)) {
            if(process.HasExited) return Task.WhenAll();

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if(cancellationToken != default(CancellationToken))
                cancellationToken.Register(() => tcs.SetCanceled());

            return process.HasExited ? Task.WhenAll() : tcs.Task;
        }

        [DllImport("User32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("User32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0x00;
        private const int SW_SHOW = 0x05;
        private const int WS_EX_APPWINDOW = 0x40000;
        private const int GWL_EXSTYLE = -0x14;
        private const int WS_EX_TOOLWINDOW = 0x0080;

        private void HideWindowFromTaskbar(IntPtr pMainWindow) {
            var flags = GetWindowLong(pMainWindow, GWL_EXSTYLE);
            ShowWindow(pMainWindow, SW_HIDE);
            SetWindowLong(pMainWindow, GWL_EXSTYLE, flags | WS_EX_TOOLWINDOW);

            ShowWindow(pMainWindow, SW_SHOW);
        }


        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);

        public struct Rect {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Right { get; set; }
            public int Bottom { get; set; }
        }

        private void SetWindowPosition(IntPtr pMainWindow) {
            var rectangle = new Rect();
            GetWindowRect(pMainWindow, ref rectangle);

            var formLocation = new Point(rectangle.Left, rectangle.Top);
            _form.Location = formLocation;
            _form.Width = rectangle.Right - rectangle.Left;
            _form.Height = rectangle.Bottom - rectangle.Top;
        }

        private void CaptureClientArea(IntPtr pMainWindow) {
            if(pMainWindow == (IntPtr)0) return;
            var rectangle = new Rect();

            GetWindowRect(pMainWindow, ref rectangle);

            var rect = new Rectangle(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
            var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, _form.Size, CopyPixelOperation.SourceCopy);
            _form.BackgroundImage = bmp;
            _form.BackgroundImageLayout = ImageLayout.None;
        }

        private static void RecurringTask(Action action, int seconds, CancellationToken token) {
            if(action == null) {
                return;
            }

            Task.Run(async () => {
                         while(!token.IsCancellationRequested) {
                             action();
                             await Task.Delay(TimeSpan.FromSeconds(seconds), token);
                         }
                     }, token);
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args) {
            var context = new Program(args);
            Application.Run(context);
        }
    }
}