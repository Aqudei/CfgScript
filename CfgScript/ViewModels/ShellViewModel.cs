using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace CfgScript.ViewModels
{
    class ShellViewModel : BindableBase
    {
        private string _logs;

        public string Logs
        {
            get { return _logs; }
            set { SetProperty(ref _logs, value); }
        }

        private string _defaultFolder;

        public string DefaultFolder
        {
            get { return _defaultFolder; }
            set
            {
                SetProperty(ref _defaultFolder, value);
                Properties.Settings.Default.DefaultFolder = value;
                Properties.Settings.Default.Save();
            }
        }

        private string _logsDir;
        private string _searchFolder;

        public string SearchFolder
        {
            get { return _searchFolder; }
            set
            {
                SetProperty(ref _searchFolder, value);

            }
        }


        public ShellViewModel()
        {
            DefaultFolder = Properties.Settings.Default.DefaultFolder;
            _logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        }

        private DelegateCommand _setDefaultFolderCommand;

        public DelegateCommand SetDefaultFolderCommand
        {
            get { return _setDefaultFolderCommand = _setDefaultFolderCommand ?? new DelegateCommand(SetDefaultFolder); }
        }

        private void SetDefaultFolder()
        {
            var dlg = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            var result = dlg.ShowDialog();
            if (result.HasValue && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                DefaultFolder = dlg.SelectedPath;
            }
        }

        private DelegateCommand _setSearchFolderCommand;

        public DelegateCommand SetSearchFolderCommand
        {
            get { return _setSearchFolderCommand = _setSearchFolderCommand ?? new DelegateCommand(SetSearchFolder); }
        }

        private void SetSearchFolder()
        {
            if (!string.IsNullOrWhiteSpace(DefaultFolder) && Directory.Exists(DefaultFolder))
            {
                var dlg = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    InitialDirectory = DefaultFolder
                };

                var result = dlg.ShowDialog();
                if (result == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok && !string.IsNullOrWhiteSpace(dlg.FileName))
                {
                    SearchFolder = dlg.FileName;
                }
            }
        }

        private DelegateCommand _runCommand;

        public DelegateCommand RunCommand
        {
            get
            {
                return _runCommand = _runCommand ?? new DelegateCommand(DoRun, CanDoRun)
                  .ObservesProperty(() => DefaultFolder).ObservesProperty(() => SearchFolder);
            }
        }

        private bool CanDoRun()
        {
            return !string.IsNullOrEmpty(DefaultFolder) && !string.IsNullOrWhiteSpace(SearchFolder);
        }

        private void DoRun()
        {
            var files = Directory.EnumerateFiles(SearchFolder, "*-user.cfg", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                ProcessFile(file);
            }

            WriteLogs();
        }

        private DelegateCommand _openLogsCommand;

        public DelegateCommand OpenLogsCommand
        {
            get { return _openLogsCommand = _openLogsCommand ?? new DelegateCommand(OpenLogsFolder); }
        }

        private void OpenLogsFolder()
        {
            Process.Start("explorer.exe", $"\"{_logsDir}\"");
        }

        private void WriteLogs()
        {

            try
            {
                Directory.CreateDirectory(_logsDir);
            }
            catch (Exception)
            {
                //Ignored
            }

            var logFile = Path.Combine(_logsDir, $"{Path.GetFileName(SearchFolder)}-{DateTime.Now.ToShortDateString().Replace("/", "-").Replace("\\", "-")}.txt");
            File.WriteAllText(logFile, Logs);
        }

        private bool IsNumber(string text)
        {
            foreach (var t in text)
            {
                if (!char.IsDigit(t))
                    return false;
            }

            return true;
        }

        private void ProcessFile(string file)
        {
            var content = File.ReadAllText(file);
            var regexAuthUserId = new Regex(@"(reg\.([0-9]+)\.auth\.userId)=(.+?)[\s$]");
            var regexLabel = new Regex(@"(reg\.([0-9]+)\.label)=(.+?)[\s$]");

            var authUserIdMatches = regexAuthUserId.Matches(content);
            var labelMatches = regexLabel.Matches(content);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(content);

            for (int i = 0; i < 100; i++)
            {
                var authUserId = $"reg.{i + 1}.auth.userId";
                var label = $"reg.{i + 1}.label";
                var authUserIdXpath = string.Format("//*[@{0}]", authUserId);
                var labelXpath = string.Format("//*[@{0}]", label);

                foreach (XmlNode authUserIdNode in xmlDoc.SelectNodes(authUserIdXpath))
                {
                    if (string.IsNullOrWhiteSpace(authUserIdNode.Attributes[authUserId].Value))
                    {
                        Logs += string.Format("For file {0}, {1} skipped because \"userId is null or empty\"\n", file, label.Replace(".label", ""));
                        continue;
                    }

                    foreach (XmlNode labelNode in xmlDoc.SelectNodes(authUserIdXpath))
                    {
                        if (IsNumber(authUserIdNode.Attributes[authUserId].Value) && IsNumber(labelNode.Attributes[label].Value))
                        {
                            if (authUserIdNode.Attributes[authUserId].Value.Length < 5)
                            {
                                Logs += string.Format("For file {0}, {1} skipped because \"userId length is less than 5\"\n", file, label.Replace(".label", ""));
                                continue;
                            }
                            labelNode.Attributes[label].Value = authUserIdNode.Attributes[authUserId].Value.Substring(authUserIdNode.Attributes[authUserId].Value.Length - 5);
                        }
                        else
                        {
                            Logs += string.Format("For file {0}, {1} skipped because \"userId or label is not numeric\"\n", file, label.Replace(".label", ""));
                        }
                    }
                }
            }

            xmlDoc.Save(file);
        }
    }
}
