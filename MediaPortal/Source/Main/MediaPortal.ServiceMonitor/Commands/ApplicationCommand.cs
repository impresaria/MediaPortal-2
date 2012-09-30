﻿#region Copyright (C) 2007-2012 Team MediaPortal

/*
    Copyright (C) 2007-2012 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Common.Threading;
using MediaPortal.ServiceMonitor.ViewModel;

namespace MediaPortal.ServiceMonitor.Commands
{
  /// <summary>
  /// Opens / closes / minimizes the application.
  /// </summary>
  public class ApplicationCommand : CommandExtension<ApplicationCommand>
  {
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetTopWindow(IntPtr hWnd);



    /// <summary>
    /// Opens, closes, or minimizes the application depending on the
    /// submitted command parameter.
    /// </summary>
    public override void Execute(object parameter)
    {
      if (!ServiceRegistration.IsRegistered<IAppController>())
        return;

      var controller = ServiceRegistration.Get<IAppController>();
      switch ((string) parameter)
      {
        case "DoubleClick": // DoubleClick of TrayIcon 
          if ((Application.Current.MainWindow != null) && (Application.Current.MainWindow.Visibility == Visibility.Visible))
          {
            //the window is opened => it should be closed
            controller.CloseMainApplication(false);
          }
          else
          {
            //the window is closed => it should be opened
            controller.ShowMainWindow();
          }
          break;
        case "Open":
          controller.ShowMainWindow();
          break;
        case "Quit":
          controller.CloseMainApplication(true);
          break;
        case "Close":
          controller.CloseMainApplication(false);
          break;
        case "Minimize":
          controller.HideMainWindow();
          break;
        case "StartService":
          // avoid any delay due to starting of the MP2 Server Service
          ServiceRegistration.Get<IThreadPool>().Add(() => controller.StartServerService());
          break;
        case "StopService":
          // avoid any delay due to stopping of the MP2 Server Service
          ServiceRegistration.Get<IThreadPool>().Add(() => controller.StopServerService());
          break;
        case "EnableAutoStart":
          controller.IsAutoStartEnabled = true;
          break;
        case "DisableAutoStart":
          controller.IsAutoStartEnabled = false;
          break;
        default:
          var msg = String.Format("ApplicationCommand fired with missing or invalid parameter: {0}", parameter);
          throw new InvalidOperationException(msg);
      }
    }

    public override bool CanExecute(object parameter)
    {
      Visibility visibility;
      IAppController controller = null;
      if (ServiceRegistration.IsRegistered<IAppController>())
        controller = ServiceRegistration.Get<IAppController>();
      
      if (Application.Current.MainWindow != null)
      {
        visibility = Application.Current.MainWindow.Visibility;
      }
      else if (controller != null)
      {
        controller = ServiceRegistration.Get<IAppController>();
        if (controller.TaskbarIcon != null)
        {
          visibility = controller.TaskbarIcon.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }
        else visibility = Visibility.Collapsed;
      }
      else return true;
      
     
      switch ((string)parameter)
      {
        case "DoubleClick": // DoubleClick of TrayIcon 
          return true;
        case "Open": // Open Main Window
          return visibility != Visibility.Visible;

        case "Close": // Close Main Window
          return visibility == Visibility.Visible;

        case "Quit": // Quit Main Application
          return true;

        case "Minimize": // Minimize to Tray
          return visibility == Visibility.Visible;

        case "StartService": // Start the MP2-Server as Service
          return (controller != null) && (controller.IsServerServiceInstalled()) && (!controller.IsServerServiceRunning());

        case "StopService": // Stop the MP2-Server Service
          return (controller != null) && (controller.IsServerServiceInstalled()) && (controller.IsServerServiceRunning());

        case "EnableAutoStart":
          return (controller != null) && (!controller.IsAutoStartEnabled);

        case "DisableAutoStart":
          return (controller != null) && (controller.IsAutoStartEnabled);

        default:
          var msg = String.Format("ApplicationCommand fired with missing or invalid parameter: {0}", parameter);
          throw new InvalidOperationException(msg);
      }
    }

  }
}