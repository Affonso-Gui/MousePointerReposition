﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;

namespace MousePointerReposition
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            string appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MousePointerReposition");
            string appLogFolder = Path.Combine(appFolder, "Logs");

            if(!Directory.Exists(appLogFolder))
                Directory.CreateDirectory(appLogFolder);
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {}
    }
}
