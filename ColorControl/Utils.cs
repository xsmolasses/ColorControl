﻿using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using NStandard;
using NvAPIWrapper.Display;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Deployment.Application;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Enumeration;
using Windows.Devices.Enumeration.Pnp;

namespace ColorControl
{
    static class Utils
    {
        [Flags]
        public enum ModKeys : int
        {
            Alt = 1,
            Control = 2,
            Shift = 4,
            Win = 8
        }

        public static string PKEY_PNPX_IpAddress = "{656a3bb3-ecc0-43fd-8477-4ae0404a96cd} 12297";
        public static string PKEY_PNPX_PhysicalAddress = "{656a3bb3-ecc0-43fd-8477-4ae0404a96cd} 12294";

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static bool WinKeyDown = false;

        public static Bitmap SubPixelShift(Bitmap bitmap)
        {
            Bitmap bitmap2 = (Bitmap)bitmap.Clone();

            int shift = -1;
            bool shiftRed = true;
            bool shiftGreen = true;
            bool shiftBlue = false;

            for (int i = 0; i < bitmap.Width; i++)
            {
                for (int j = 0; j < bitmap.Height; j++)
                {
                    Color pixel = bitmap.GetPixel(i, j);

                    if (shift > 0 && i < bitmap.Width - shift)
                    {
                        Color pixel2 = bitmap2.GetPixel(i + shift, j);

                        Color pixel3 = Color.FromArgb(shiftRed ? pixel.R : pixel2.R, shiftGreen ? pixel.G : pixel2.G, shiftBlue ? pixel.B : pixel2.B);

                        bitmap2.SetPixel(i + shift, j, pixel3);

                    }
                    else if (i + shift >= 0)
                    {
                        Color pixel2 = bitmap2.GetPixel(i + shift, j);

                        Color pixel3 = Color.FromArgb(shiftRed ? pixel.R : pixel2.R, shiftGreen ? pixel.G : pixel2.G, shiftBlue ? pixel.B : pixel2.B);

                        bitmap2.SetPixel(i + shift, j, pixel3);

                    }
                }
            }

            return bitmap2;
            //bitmap2.Save("d:\\ss_shifted.bmp");
        }

        public static string GetDataPath(string subDir = "")
        {
            var path = Directory.GetParent(Application.UserAppDataPath).FullName;
            if (!string.IsNullOrEmpty(subDir))
            {
                path = Path.Combine(path, subDir);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            return path;
        }

        public static (int mods, int key) ParseShortcut(string shortcut)
        {
            int mods = 0;
            var key = 0;

            shortcut = shortcut.Replace(",", "");
            shortcut = shortcut.Replace("+", "");
            shortcut = shortcut.Replace("  ", " ");
            var parts = shortcut.Split(' ');

            if (parts.Length > 0)
            {
                for (int i = 0; i + 1 < parts.Length; i++)
                {
                    switch (parts[i])
                    {
                        case "Alt":
                            mods += (int)ModKeys.Alt;
                            break;
                        case "Control":
                            mods += (int)ModKeys.Control;
                            break;
                        case "Shift":
                            mods += (int)ModKeys.Shift;
                            break;
                        case "Win":
                            mods += (int)ModKeys.Win;
                            break;
                    }
                }

                var keyName = parts[parts.Length - 1];

                key = (int)Enum.Parse(typeof(Keys), keyName);
            }

            return (mods, key);
        }

        internal static bool ExecuteElevated(string args)
        {
            var info = new ProcessStartInfo(Assembly.GetEntryAssembly().Location, args)
            {
                Verb = "runas", // indicates to elevate privileges
            };

            var process = new Process
            {
                EnableRaisingEvents = true, // enable WaitForExit()
                StartInfo = info
            };
            try
            {
                process.Start();
                process.WaitForExit(); // sleep calling process thread until evoked process exit
                return true;
            }
            catch (Exception e)
            {
                Logger.Error("ExecuteElevated: " + e.Message);
            }
            return false;
        }

        internal static bool IsChromeFixInstalled()
        {
            var key = Registry.ClassesRoot.OpenSubKey(@"ChromeHTML\shell\open\command");
            return key != null && key.GetValue(null).ToString().Contains("--disable-lcd-text");
        }

        internal static bool InstallChromeFix(bool install)
        {
            var argument = "--disable-lcd-text";

            var key = Registry.ClassesRoot.OpenSubKey(@"ChromeHTML\shell\open\command", true);
            if (key != null)
            {
                var value = key.GetValue(null).ToString();
                value = value.Replace("chrome.exe\" " + argument + " --", "chrome.exe\" --");
                if (install)
                {
                    value = value.Replace("chrome.exe\" --", "chrome.exe\" " + argument + " --");
                }
                key.SetValue(null, value);
            }

            var roamingFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            UpdateShortcut(Path.Combine(roamingFolder, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Google Chrome.lnk"), argument, !install);
            UpdateShortcut(Path.Combine(roamingFolder, @"Microsoft\Internet Explorer\Quick Launch\Google Chrome.lnk"), argument, !install);

            var allUsersStartMenuFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            UpdateShortcut(Path.Combine(allUsersStartMenuFolder, @"Programs\Google Chrome.lnk"), argument, !install);

            return true;
        }

        internal static bool IsChromeInstalled()
        {
            var key = Registry.ClassesRoot.OpenSubKey(@"ChromeHTML\shell\open\command");
            return key != null;
        }

        internal static void GetRegistryKeyValue(string keyname, string valueName, bool deepSearch = false)
        {
            var key = Registry.LocalMachine.OpenSubKey(keyname);

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                var subKey = key.OpenSubKey(subKeyName);
                if (subKey.GetValueNames().Contains(valueName))
                {
                    Thread.Sleep(0);
                }
            }

        }

        internal static bool UpdateShortcut(string path, string arguments, bool removeArguments = false)
        {
            if (File.Exists(path))
            {
                IWshRuntimeLibrary.WshShell shell = new IWshRuntimeLibrary.WshShell();

                IWshRuntimeLibrary.IWshShortcut shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(path);

                shortcut.Arguments = shortcut.Arguments.Replace(arguments, "");
                if (!removeArguments)
                {
                    if (!string.IsNullOrEmpty(shortcut.Arguments))
                    {
                        shortcut.Arguments = shortcut.Arguments + " ";
                    }
                    shortcut.Arguments = shortcut.Arguments + arguments;
                }

                // save it / create
                shortcut.Save();

                return true;
            }
            return false;
        }

        public static string GetDeviceProperty(PnpObject device, string propertyName)
        {
            object objValue;
            string value = null;

            //foreach (var key in device.Properties.Keys)
            //{
            //    Debug.WriteLine(key);
            //}

            if (device.Properties.TryGetValue(propertyName, out objValue))
            {
                if (objValue is string[])
                {
                    var arr = objValue as string[];
                    if (arr.Length > 0)
                    {
                        value = arr[0];
                    }
                }
                else if (objValue is byte[])
                {
                    value = BitConverter.ToString((byte[])objValue);
                }
                else
                {
                    value = objValue?.ToString();
                }
            }

            return value;
        }

        public static async Task<List<PnpDev>> GetPnpDevices(string deviceName)
        {
            var devices = new List<PnpDev>();

            var list = await DeviceInformation.FindAllAsync("", new List<string>());

            var reqs = new List<string>
            {
                PKEY_PNPX_IpAddress,
                PKEY_PNPX_PhysicalAddress
            };

            foreach (var dev in list)
            {
                string name = dev.Name;
                if (/*dev.IsEnabled &&*/ dev.Name.Contains(deviceName))
                {
                    var guid = dev.Properties["System.Devices.DeviceInstanceId"];

                    var aqs = "System.Devices.DeviceInstanceId:=\"" + guid + "\"";
                    var list2 = await PnpObject.FindAllAsync(PnpObjectType.Device, reqs, aqs);

                    foreach (var dev2 in list2)
                    {
                        var ipAddress = GetDeviceProperty(dev2, "System.Devices.IpAddress");
                        var macAddress = GetDeviceProperty(dev2, PKEY_PNPX_PhysicalAddress);

                        if (!devices.Any(x => x.Name.Equals(name) && x.IpAddress != null && x.IpAddress.Equals(ipAddress)))
                        {
                            var device = new PnpDev(dev, dev2, name, ipAddress, macAddress);
                            devices.Add(device);
                        }
                    }

                }
            }
            return devices;
        }

        public static void WaitForTask(System.Threading.Tasks.Task task)
        {
            while (task != null && (task.Status < TaskStatus.WaitingForChildrenToComplete))
            {
                Thread.Sleep(100);
                Application.DoEvents();
            }
        }

        public static void SetNotifyIconText(NotifyIcon ni, string text)
        {
            text = text.Substring(0, Math.Min(text.Length, 127));
            Type t = typeof(NotifyIcon);
            BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
            t.GetField("text", hidden).SetValue(ni, text);
            if ((bool)t.GetField("added", hidden).GetValue(ni))
                t.GetMethod("UpdateIcon", hidden).Invoke(ni, new object[] { true });
        }

        internal static Image GenerateGradientBitmap(int width, int height)
        {
            var bitmap = new Bitmap(512, height);

            for (var i = 0; i < 256; i++)
            {
                var pixel = Color.FromArgb(i, i, i);

                for (var h = 0; h < height; h++)
                {
                    bitmap.SetPixel(i * 2, h, pixel);
                    bitmap.SetPixel(i * 2 + 1, h, pixel);
                }
            }

            return bitmap;
        }

        public static string GetDescription<T>(this T e) where T : IConvertible
        {
            if (e is Enum)
            {
                var type = e.GetType();
                var values = Enum.GetValues(type);

                foreach (int val in values)
                {
                    if (val == e.ToInt32(CultureInfo.InvariantCulture))
                    {
                        var memInfo = type.GetMember(type.GetEnumName(val));
                        var descriptionAttribute = memInfo[0]
                            .GetCustomAttributes(typeof(DescriptionAttribute), false)
                            .FirstOrDefault() as DescriptionAttribute;

                        if (descriptionAttribute != null)
                        {
                            return descriptionAttribute.Description;
                        }
                    }
                }
            }

            return null; // could also return string.Empty
        }

        public static List<string> GetDescriptions<T>() where T : IConvertible
        {
            var list = new List<string>();
            foreach (var enumValue in Enum.GetValues(typeof(T)))
            {
                list.Add(((T)enumValue).GetDescription());
            }
            return list;
        }

        public static void RegisterTask(string taskName, bool enabled)
        {
            var file = Assembly.GetExecutingAssembly().Location;
            var directory = Path.GetDirectoryName(file);

            try
            {
                using (TaskService ts = new TaskService())
                {
                    if (enabled)
                    {
                        TaskDefinition td = ts.NewTask();
                        td.RegistrationInfo.Description = "Start ColorControl";

                        td.Triggers.Add(new LogonTrigger { UserId = WindowsIdentity.GetCurrent().Name });

                        td.Actions.Add(new ExecAction(file, StartUpParams.RunningFromScheduledTaskParam, directory));

                        ts.RootFolder.RegisterTaskDefinition(taskName, td);
                    }
                    else
                    {
                        ts.RootFolder.DeleteTask(taskName, false);
                    }
                }
            }
            catch (Exception e)
            {
                MessageForms.ErrorOk("Could not create/delete task: " + e.Message);
            }
        }

        public static bool TaskExists(string taskName, bool update)
        {
            var file = Assembly.GetExecutingAssembly().Location;
            using (TaskService ts = new TaskService())
            {
                var task = ts.RootFolder.Tasks.FirstOrDefault(x => x.Name.Equals(taskName));

                if (task != null)
                {
                    if (update && ApplicationDeployment.IsNetworkDeployed)
                    {
                        var action = task.Definition.Actions.FirstOrDefault(x => x.ActionType == TaskActionType.Execute);
                        if (action != null)
                        {
                            var execAction = action as ExecAction;
                            if (!execAction.Path.Equals(file))
                            {
                                RegisterTask(taskName, false);
                                RegisterTask(taskName, true);
                            }
                        }
                    }
                    return true;
                }
                return false;
            }
        }

        public static void RegisterShortcut(IntPtr handle, int id, string shortcut, bool clear = false)
        {
            if (clear)
            {
                UnregisterHotKey(handle, id);
            }

            if (!string.IsNullOrEmpty(shortcut))
            {
                var (mods, key) = ParseShortcut(shortcut);
                RegisterHotKey(handle, id, mods, key);
            }
        }

        public static void BuildDropDownMenu(ToolStripMenuItem mnuParent, string name, Type enumType, object colorData, string propertyName, EventHandler clickEvent)
        {
            PropertyInfo property;
            var subMenuItems = mnuParent.DropDownItems.Find("miColorSettings_" + name, false);
            ToolStripMenuItem subMenuItem;
            if (subMenuItems.Length == 0)
            {
                subMenuItem = (ToolStripMenuItem)mnuParent.DropDownItems.Add(name);
                subMenuItem.Name = "miColorSettings_" + name;

                property = colorData.GetType().GetDeclaredProperty(propertyName);
                subMenuItem.Tag = property;

                foreach (var enumValue in Enum.GetValues(enumType))
                {
                    var item = subMenuItem.DropDownItems.Add(enumValue.ToString());
                    item.Tag = enumValue;
                    item.Click += clickEvent;
                }
            }
            else
            {
                subMenuItem = (ToolStripMenuItem)subMenuItems[0];
                property = (PropertyInfo)subMenuItem.Tag;
            }

            var value = property.GetValue(colorData);

            foreach (var item in subMenuItem.DropDownItems)
            {
                if (item is ToolStripMenuItem)
                {
                    var menuItem = (ToolStripMenuItem)item;
                    if (menuItem.Tag != null)
                    {
                        menuItem.Checked = menuItem.Tag.Equals(value);
                    }
                }
            }
        }

        public static string FormatKeyboardShortcut(KeyEventArgs keyEvent)
        {
            var pressedModifiers = keyEvent.Modifiers;

            //Debug.WriteLine("KD: " + e.Modifiers + ", " + e.KeyCode);

            var shortcutString = (pressedModifiers > 0 ? pressedModifiers.ToString() : "");
            if (keyEvent.KeyCode == Keys.LWin || WinKeyDown)
            {
                WinKeyDown = true;
                if (!string.IsNullOrEmpty(shortcutString))
                {
                    shortcutString += ", ";
                }
                shortcutString += "Win";
            }

            if (!string.IsNullOrEmpty(shortcutString) && keyEvent.KeyCode != Keys.ControlKey && keyEvent.KeyCode != Keys.ShiftKey && keyEvent.KeyCode != Keys.Menu && keyEvent.KeyCode != Keys.LWin)
            {
                shortcutString += " + " + keyEvent.KeyCode.ToString();
            }

            if (pressedModifiers == 0 && !WinKeyDown)
            {
                keyEvent.SuppressKeyPress = true;
            }

            return shortcutString;
        }

        public static void HandleKeyboardShortcutUp(KeyEventArgs keyEvent)
        {
            if (keyEvent.KeyCode == Keys.LWin)
            {
                WinKeyDown = false;
            }
        }

        public static void InitListView(ListView listView, IEnumerable<string> columns)
        {
            foreach (var name in columns)
            {
                var columnName = name;
                var parts = name.Split('|');

                var width = 120;
                if (parts.Length > 1)
                {
                    width = int.Parse(parts[1]);
                    columnName = parts[0];
                }

                var header = listView.Columns.Add(columnName);
                header.Width = width == 120 ? -2 : width;
            }
        }

        public static void AddOrUpdateListItem<T>(ListView listView, List<T> presets, Config config, T preset = null, ListViewItem specItem = null) where T : PresetBase
        {
            ListViewItem item = null;
            if (preset == null)
            {
                item = listView.SelectedItems[0];
                preset = (T)item.Tag;
            }
            else
            {
                item = specItem;
            }

            if (preset.id == 0)
            {
                preset.id = preset.GetHashCode();
            }

            var values = preset.GetDisplayValues(config);

            if (item == null)
            {
                item = listView.Items.Add(values[0]);
                item.Tag = preset;
                for (var i = 1; i < values.Count; i++)
                {
                    item.SubItems.Add(values[i]);
                }
                if (!presets.Any(x => x.id == preset.id))
                {
                    presets.Add(preset);
                }
            }
            else
            {
                item.Text = values[0];
                for (var i = 1; i < values.Count; i++)
                {
                    item.SubItems[i].Text = values[i];
                }
            }
        }


    }
}
