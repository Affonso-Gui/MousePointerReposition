﻿using FMUtils.KeyboardHook;
using Microsoft.Win32;
using MousePointerReposition.Helper;
using MousePointerReposition.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace MousePointerReposition
{
    public class MainWindowVM : INotifyPropertyChanged
    {
        #region constants

        /// <summary>
        /// Timeout waiting for new foreground application 
        /// </summary>
        private const double CHECK_TIMEOUT = 5000; // ms

        /// <summary>
        /// Period for detecting multiple CTRL key pressing
        /// </summary>
        private const long CTRL_KEY_TIMEOUT = 1000; // ms

        #endregion constants


        #region fields

        private Vanara.PInvoke.RECT foreGroundWindowRectStart;
        private Dictionary<Vanara.PInvoke.HWND, Vanara.PInvoke.RECT> windowSizeStart;
        private WindowState windowState;
        private bool showInTaskbar;
        private bool autostart;
        private RelayCommand loaded;
        private RelayCommand closing;
        private RelayCommand exit;
        private RelayCommand hide;
        private bool? disableAltTab;
        private bool? disableWinLeftRight;
        private bool? disableManualReposition;
        private bool? disableFocusToggle;
        private bool autoStartDisabled;

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion fields


        #region private properties

        private NotifyIcon NotifyIcon { get; set; }
        private Hook KeyboardHook { get; set; }
        private double CheckPeriod { get; set; }
        private bool IsMousePositioningTriggered { get; set; }
        private bool IsManualMousePositioningTriggered { get; set; }
        private int CtrlKeyPressCounter { get; set; }
        private DateTime CtrlKeyPressLast { get; set; }
        private System.Timers.Timer RepositioningTimer { get; set; }

        #endregion private properties


        #region public properties

        /// <summary>
        /// Window loaded command
        /// </summary>
        public RelayCommand Loaded
        {
            get
            {
                if (loaded == null)
                    loaded = new RelayCommand(OnLoaded);
                return loaded;
            }
        }

        /// <summary>
        /// Window closing command
        /// </summary>
        public RelayCommand Closing
        {
            get
            {
                if (closing == null)
                    closing = new RelayCommand(OnClosing);
                return closing;
            }
        }

        /// <summary>
        /// Close programm
        /// </summary>
        public RelayCommand Exit
        {
            get
            {
                if (exit == null)
                    exit = new RelayCommand(OnExit);
                return exit;
            }
        }

        /// <summary>
        /// Hide window
        /// </summary>
        public RelayCommand Hide
        {
            get
            {
                if (hide == null)
                    hide = new RelayCommand(OnHide);
                return hide;
            }
        }

        

        /// <summary>
        /// Window state
        /// </summary>
        public WindowState WindowState
        {
            get => windowState;
            set
            {
                windowState = value;
                OnPropertyChanged(nameof(WindowState));
            }
        }

        /// <summary>
        /// Show in taskbar
        /// </summary>
        public bool ShowInTaskbar
        {
            get => showInTaskbar;
            set
            {
                showInTaskbar = value;
                OnPropertyChanged(nameof(ShowInTaskbar));
            }
        }





        public bool AutoStartDisabled
        {
            get => autoStartDisabled; set
            {
                autoStartDisabled = value;
                OnPropertyChanged(nameof(AutoStartDisabled));
            }
        }

        /// <summary>
        /// Autostart
        /// </summary>
        public bool Autostart
        {
            get
            {
                return autostart;
            }
            set
            {
                if (autostart != value) // value changed
                {
                    autostart = value;

#if _UWP
                    var startupTask = GetStartupTask();
                    if (value)
                    {
                        var task = startupTask.RequestEnableAsync().AsTask();
                        task.Wait();
                        SetAutostartState(task.Result);
                    }
                    else
                        startupTask.Disable();
#else
                    RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    if (value)
                    {
                        string assemblyPath = Assembly.GetExecutingAssembly().Location;
                        rkApp.SetValue("MousePointerReposition", assemblyPath, RegistryValueKind.String);
                    }
                    else
                    {
                        rkApp.DeleteValue("MousePointerReposition");
                    }
                    
#endif
                    OnPropertyChanged(nameof(Autostart));
                }
            }
        }

        /// <summary>
        ///  Disable ALT+TAB key combination
        /// </summary>
        public bool DisableAltTab
        {
            get
            {
                if (!disableAltTab.HasValue)
                    disableAltTab = SettingsHelper.Instance.DisableAltTab;
                return disableAltTab ?? false;
            }
            set
            {
                disableAltTab = value;
                SettingsHelper.Instance.DisableAltTab = value;
                OnPropertyChanged(nameof(DisableAltTab));
            }
        }

        /// <summary>
        ///  Disable WIN+LEFT or RIGHT key combination
        /// </summary>
        public bool DisableWinLeftRight
        {
            get
            {
                if (!disableWinLeftRight.HasValue)
                    disableWinLeftRight = SettingsHelper.Instance.DisableWinLeftRight;
                return disableWinLeftRight ?? false;
            }
            set
            {
                disableWinLeftRight = value;
                SettingsHelper.Instance.DisableWinLeftRight = value;
                OnPropertyChanged(nameof(DisableWinLeftRight));
            }

        }

        /// <summary>
        /// Disable manual cursor repositioning (2x CTRL key)
        /// </summary>
        public bool DisableManuelPositioning
        {
            get
            {
                if (!disableManualReposition.HasValue)
                    disableManualReposition = SettingsHelper.Instance.DisableManualCursorReposition;
                return disableManualReposition ?? false;
            }
            set
            {
                disableManualReposition = value;
                SettingsHelper.Instance.DisableManualCursorReposition = value;
                OnPropertyChanged(nameof(DisableManuelPositioning));
            }
        }

        /// <summary>
        /// Disable Multi-Monitor Focus Mode Toggle
        /// </summary>
        public bool DisableFocusToggle
        {
            get
            {
                if (!disableFocusToggle.HasValue)
                    disableFocusToggle = SettingsHelper.Instance.DisableFocusToggle;
                return disableFocusToggle ?? false;
            }
            set
            {
                disableFocusToggle = value;
                SettingsHelper.Instance.DisableFocusToggle = value;
                OnPropertyChanged(nameof(DisableFocusToggle));
            }
        }
        #endregion public properties


        #region .ctor

        /// <summary>
        /// .ctor
        /// </summary>
        public MainWindowVM()
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = true;

            // timer 
            RepositioningTimer = new System.Timers.Timer(200);
            RepositioningTimer.Elapsed += RepositioningTimer_Elapsed;
            RepositioningTimer.AutoReset = false;

            // dict
            windowSizeStart = new Dictionary<Vanara.PInvoke.HWND, Vanara.PInvoke.RECT>();

            // setup keyboard hook
            KeyboardHook = new Hook("Global Keyboard Hook");
            KeyboardHook.KeyUpEvent += KeyUpHook;
            KeyboardHook.KeyDownEvent += KeyDownHook;

            // check autostart task state
            AutoStartDisabled = true;
            RefreshAutostartState();
        }

        #endregion .ctor


        #region private methods
        /// <summary>
        /// Window on load
        /// </summary>
        /// <param name="state"></param>
        private void OnLoaded(object state)
        {
            // notifyIcon
            NotifyIcon = new NotifyIcon();

            System.IO.Stream iconStream = this.GetType().Assembly.GetManifestResourceStream("MousePointerReposition.Icons.mouse_gray.ico");

            NotifyIcon.Icon = new System.Drawing.Icon(iconStream);
            NotifyIcon.Visible = true;
            NotifyIcon.Click += NotifyIcon_Click;

            // use background dispatcher
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                new Action(() =>
                    {
                        ShowInTaskbar = false;
                    }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Window in closing
        /// </summary>
        /// <param name="state"></param>
        private void OnClosing(object state)
        {
            NotifyIcon.Visible = false;
        }

        /// <summary>
        /// Exit programm
        /// </summary>
        /// <param name="state"></param>
        private void OnExit(object state)
        {
            System.Windows.Application.Current.MainWindow.Close();
        }

        /// <summary>
        /// Hide window
        /// </summary>
        /// <param name="state"></param>
        private void OnHide(object state)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }

        /// <summary>
        /// Retries to move the mouse cursor to the center of a new active application window for a specific amount of time (CHECK_TIMEOUT).
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RepositioningTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            CheckPeriod += RepositioningTimer.Interval;

            // check timeout
            // and only restart if repositiong does not occur
            if (CheckPeriod < CHECK_TIMEOUT
                && !TryMouseRepositioning())
            {
                RepositioningTimer.Start();
            }
        }

        /// <summary>
        /// Open main window when notification icon is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
            }
            else
            {
                WindowState = WindowState.Normal;
                ShowInTaskbar = true;
                System.Windows.Application.Current.MainWindow.Focus();
            }
        }


        /// <summary>
        /// Keyboard key down event handler method
        /// </summary>
        /// <param name="e"></param>
        private void KeyDownHook(KeyboardHookEventArgs e)
        {
            // win + shift + left or right key
            if (!DisableWinLeftRight)
            {
                if (e.Key == Keys.Left
                    || e.Key == Keys.Right && e.isWinPressed) // with or without shift //&& e.isShiftPressed)
                {
                    TriggerMousePositioning();
                    // Don't wait until the key is released
                    ExecuteMousePositioning();
                }
            }

            // alt + tap
            if (!DisableAltTab)
            {
                if (e.Key == Keys.Tab && e.isAltPressed)
                {
                    TriggerMousePositioning();
                }
            }

            if (!DisableFocusToggle)
            {
                if (e.Key == Keys.F11 && e.isAltPressed)
                {
                    ToggleMultiMonitorFocusMode();
                }
            }

            if (!DisableManuelPositioning)
            {
                if (e.isCtrlPressed && e.Key == Keys.None)
                {
                    CtrlKeyPressCounter++;

                    if (CtrlKeyPressCounter == 1)
                    {

                        CtrlKeyPressLast = DateTime.Now;
                    }


                    if (CtrlKeyPressCounter >= 2)
                    {
                        if ((DateTime.Now - CtrlKeyPressLast).TotalMilliseconds <= CTRL_KEY_TIMEOUT)
                        {
                            TriggerManualMousePositioning();
                        }
                        else
                        {
                            CtrlKeyPressLast = DateTime.Now;
                            CtrlKeyPressCounter = 1;
                        }
                    }
                }

                if (e.isCtrlPressed && e.Key != Keys.None)
                {
                    CtrlKeyPressCounter = 0;
                    IsManualMousePositioningTriggered = false;
                }
            }
        }

        /// <summary>
        /// Keyboard key up event handler method
        /// </summary>
        /// <param name="e"></param>
        private void KeyUpHook(KeyboardHookEventArgs e)
        {
            // when all keys are released
            if (e.Key == Keys.None && IsMousePositioningTriggered)
            {
                ExecuteMousePositioning();
            }

            if (!DisableManuelPositioning)
            {
                if (e.isCtrlPressed && e.Key == Keys.None && IsManualMousePositioningTriggered)
                {
                    CtrlKeyPressCounter = 0;
                    IsManualMousePositioningTriggered = false;
                    ExecuteMousePositioning();
                }
            }
        }

        private void ToggleMultiMonitorFocusMode()
        {
            var foregroundWindowHandle = Vanara.PInvoke.User32.GetForegroundWindow();

            if (windowSizeStart.ContainsKey(foregroundWindowHandle))
            {
                // Already has entry. Delete entry and return to previous size.
                var rect = windowSizeStart[foregroundWindowHandle];
                windowSizeStart.Remove(foregroundWindowHandle);
                Vanara.PInvoke.User32.MoveWindow(foregroundWindowHandle, rect.left, rect.top, rect.Size.Width, rect.Size.Height, true);
                return;
            }

            // No entry found. Store current size and then maximize.
            var currentRect = new Vanara.PInvoke.RECT();
            Vanara.PInvoke.User32.GetWindowRect(foregroundWindowHandle, out currentRect);
            windowSizeStart.Add(foregroundWindowHandle, currentRect);

            int left = Vanara.PInvoke.User32.GetSystemMetrics(Vanara.PInvoke.User32.SystemMetric.SM_XVIRTUALSCREEN);
            int top = Vanara.PInvoke.User32.GetSystemMetrics(Vanara.PInvoke.User32.SystemMetric.SM_YVIRTUALSCREEN);
            int width = Vanara.PInvoke.User32.GetSystemMetrics(Vanara.PInvoke.User32.SystemMetric.SM_CXVIRTUALSCREEN);
            int height = Vanara.PInvoke.User32.GetSystemMetrics(Vanara.PInvoke.User32.SystemMetric.SM_CYVIRTUALSCREEN);
            Vanara.PInvoke.User32.MoveWindow(foregroundWindowHandle, left, top, width, height, true);
        }

        /// <summary>
        /// Called when key combination for cursor repositioning is detected.
        /// Stores current foreground window geometry.
        /// </summary>
        private void TriggerMousePositioning()
        {
            RepositioningTimer.Stop(); // stop currently executing repositioning
            IsMousePositioningTriggered = true;

            // save current foreground window rectangle
            var foregroundWindowHandleStart = Vanara.PInvoke.User32.GetForegroundWindow();
            foreGroundWindowRectStart = new Vanara.PInvoke.RECT();
            Vanara.PInvoke.User32.GetWindowRect(foregroundWindowHandleStart, out foreGroundWindowRectStart);
        }


        /// <summary>
        /// Called when key combination for manual cursor repositioning is detected.
        /// Stores current foreground window geometry.
        /// </summary>
        private void TriggerManualMousePositioning()
        {
            RepositioningTimer.Stop(); // stop currently executing repositioning
            IsManualMousePositioningTriggered = true;

            // save empty current foreground window rectangle
            foreGroundWindowRectStart = new Vanara.PInvoke.RECT();
        }


        /// <summary>
        /// Start timer for trying cursor repositining to a new foreground application window.
        /// </summary>
        private void ExecuteMousePositioning()
        {
            IsMousePositioningTriggered = false;

            CheckPeriod = 0;
            // start timer that checks active window change (rectangle change)
            RepositioningTimer.Start();
        }


        /// <summary>
        /// Try to move cursor to the center of a new foreground application window.
        /// The active foreground window is compared with the active window before pressing the triggering key combinations.
        /// If the active foreground window has changed the mouse cursor is only repositioned 
        /// if the current cursor location is not already within the foreground window rectangle.
        /// </summary>
        /// <returns></returns>
        private bool TryMouseRepositioning()
        {
            // get foreground window
            var foregroundWindowHandle = Vanara.PInvoke.User32.GetForegroundWindow();
            Vanara.PInvoke.User32.GetWindowRect(foregroundWindowHandle, out Vanara.PInvoke.RECT windowRect);

            if (foreGroundWindowRectStart.left != windowRect.left
                || foreGroundWindowRectStart.Width != windowRect.Width
                || foreGroundWindowRectStart.top != windowRect.top
                || foreGroundWindowRectStart.Height != windowRect.Height)
            {
                // check if mouse position is within new active application window
                // get current cursor position
                Vanara.PInvoke.User32.GetCursorPos(out System.Drawing.Point cursorPos);

                if (windowRect.left <= cursorPos.X
                    && windowRect.right >= cursorPos.X
                    && windowRect.top <= cursorPos.Y
                    && windowRect.bottom >= cursorPos.Y)
                {
                    return true;
                }
                else
                {
                    int x = windowRect.left + windowRect.Width / 2;
                    int y = windowRect.top + windowRect.Height / 2;

                    Debug.WriteLine(String.Format("New cursor: x={0} y={1}", x, y));
                    Vanara.PInvoke.User32.SetCursorPos(x + 1, y + 1); // sometimes cursor is not positioned right
                    Vanara.PInvoke.User32.SetCursorPos(x, y); // calling twice sets the correct postion

                    return true;
                }
            }
            else
            {
                return false;
            }
                

            
        }


#if _UWP
        /// <summary>
        /// Get windows universal app startup task.
        /// </summary>
        /// <returns></returns>
        private Windows.ApplicationModel.StartupTask GetStartupTask()
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync("MousePointerRepositionStartupTask").AsTask();
            task.Wait();
            return task.Result;
        }

        /// <summary>
        /// Get windows universal app startup task.
        /// </summary>
        private void GetStartupTaskAndContinue(Action<Task<Windows.ApplicationModel.StartupTask>> action)
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync("MousePointerRepositionStartupTask").AsTask();
            task.ContinueWith(action);
        }
    
        /// <summary>
        /// Get start task state and set AutostartDisabled and Autostart property values;
        /// </summary>
        private void RefreshAutostartState()
        {
            GetStartupTaskAndContinue((t) =>
            {
                SetAutostartState(t.Result.State);
            });
        }

        /// <summary>
        /// Set AutostartDisabled and Autostart property view startup task state (universal app startup)
        /// </summary>
        /// <param name="startupTask"></param>
        private void SetAutostartState(Windows.ApplicationModel.StartupTaskState startupTaskState)
        {
            AutoStartDisabled = startupTaskState == Windows.ApplicationModel.StartupTaskState.DisabledByUser ||
                    startupTaskState == Windows.ApplicationModel.StartupTaskState.DisabledByPolicy ||
                    startupTaskState == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;

            autostart = startupTaskState == Windows.ApplicationModel.StartupTaskState.Enabled ||
                startupTaskState == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            OnPropertyChanged(nameof(Autostart));
        }
#else
        /// <summary>
        /// Get start task state and set AutostartDisabled and Autostart property values;
        /// </summary>
        private void RefreshAutostartState()
        {
            AutoStartDisabled = false;

            RegistryKey rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            string appPath = rkApp.GetValue("MousePointerReposition")?.ToString();
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            if (appPath == null || !assemblyPath.Equals(appPath))
            {
                autostart = false;
            }
            else
            {
                autostart = true;
            }
            OnPropertyChanged(nameof(Autostart));
        }
#endif

        #endregion private methods


        #region INotifyPropertyChanged
        /// <summary>
        /// Raises this object's PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The property that has a new value.</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChangedEventArgs e = new PropertyChangedEventArgs(propertyName);
                PropertyChanged(this, e);
            }
        }
        #endregion INotifyPropertyChanged
    }

}
