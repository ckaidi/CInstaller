using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CheckBox = Xceed.Wpf.Toolkit.CheckBox;
using Application = System.Windows.Application;

namespace CInstaller
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Xceed.Wpf.Toolkit.Window
    {
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private AppModel _appModel = new AppModel();
        private int _filesCount = 0;

        public MainWindow()
        {
            InitializeComponent();
            // 获取 "Program Files" 目录
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            InstallPath.Text = Path.Combine(programFiles, _appModel.EN);
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var revits = GetInstalledRevitVersions().Where(x =>
            {
                string pattern = @"Autodesk Revit (\d{4})";
                Match match = Regex.Match(x, pattern);
                return match.Success && int.TryParse(match.Groups[1].Value, out var year) && year >= 2020;
            }).ToList();
            if (revits.Count == 0)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show(this, "电脑中没有安装任何大于2020的Autodesk Revit,安装程序退出");
                Close();
            }
        }

        /// <summary>
        /// 应用退出的时候打开安装的程序
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnClosed(object sender, EventArgs e)
        {
            var p = Path.Combine(InstallPath.Text, _appModel.StartUpApp);
            Process.Start(p);
        }

        /// <summary>
        /// 下载安装按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DownloadAndInstallButtonClick(object sender, RoutedEventArgs e)
        {
            var zipFile = Properties.Resources.ResourceManager.GetObject("app");
            var installPaths = GetRevitInstallationPath();
            foreach (var item in installPaths)
            {
                var addinFolder = Directory.CreateDirectory(item);
                foreach (var version in addinFolder.GetDirectories())
                {
                    if (int.TryParse(version.Name, out var versionNumber) && versionNumber >= 2020)
                    {
                        ExtractZipFromResources(version.FullName);
                    }
                }
            }
            Xceed.Wpf.Toolkit.MessageBox.Show(this, "安装完成!");
            Close();


            //MainButton.Content = "取消";
            //MainButton.Click -= DownloadAndInstallButtonClick;
            //MainButton.Click += CancelDownloadButtonClick;
            //_cancellationTokenSource = new CancellationTokenSource();

            //ProgressBarGrid.Visibility = Visibility.Visible;
            //LocationGrid.Visibility = Visibility.Collapsed;
            //await DownloadFilelist();
        }

        public void ExtractZipFromResources(string outputDirectory)
        {
            try
            {
                // 确保输出目录存在
                Directory.CreateDirectory(outputDirectory);
                // 从程序集资源中获取 ZIP 文件的流
                var resourceStream = Properties.Resources.ResourceManager.GetObject("app");
                if (resourceStream is byte[] bs)
                {
                    using (MemoryStream memoryStream = new MemoryStream(bs))
                    {
                        if (resourceStream != null)
                        {
                            using (var zipArchive = new ZipArchive(memoryStream))
                            {
                                var count = zipArchive.Entries.Count;
                                var inter = 100d / count;
                                // 遍历 ZIP 文件中的所有条目并解压
                                foreach (ZipArchiveEntry entry in zipArchive.Entries)
                                {
                                    string destinationPath = Path.Combine(outputDirectory, entry.FullName);

                                    // 如果条目是目录，则创建目录
                                    if (entry.FullName.EndsWith("/"))
                                    {
                                        Directory.CreateDirectory(destinationPath);
                                    }
                                    else
                                    {
                                        // 确保文件的目标目录存在
                                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                                        // 解压文件到目标路径
                                        entry.ExtractToFile(destinationPath, overwrite: true);
                                    }
                                    Dispatcher.Invoke(() =>
                                    {
                                        MainProgressBar.Value += inter;
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (IOException ex)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show(this, $"Error extracting resource zip: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取revit的安装位置
        /// </summary>
        /// <returns></returns>
        public static List<string> GetRevitInstallationPath()
        {
            // 指定 Revit 的注册表键路径
            string registryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

            // 打开注册表项
            RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKeyPath);
            var result = new List<string>();
            if (key != null)
            {
                // 遍历 "Uninstall" 下所有子键
                foreach (string subkeyName in key.GetSubKeyNames())
                {
                    RegistryKey subkey = key.OpenSubKey(subkeyName);

                    // 检查子键的显示名称是否包含 "Autodesk Revit"
                    if (subkey.GetValue("DisplayName") != null &&
                        subkey.GetValue("DisplayName").ToString().Contains("Autodesk Revit"))
                    {
                        // 获取并返回安装路径
                        if (subkey.GetValue("InstallLocation") != null)
                        {
                            var v = subkey.GetValue("InstallLocation").ToString();
                            if (v.Contains("Addins"))
                                result.Add(v);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 获取电脑上安装的revit版本
        /// </summary>
        /// <returns></returns>
        public static List<string> GetInstalledRevitVersions()
        {
            List<string> installedVersions = new List<string>();
            string uninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                // 查询 Uninstall 键以查找 Revit 的版本
                using (RegistryKey uninstallKey = baseKey.OpenSubKey(uninstallKeyPath))
                {
                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using (RegistryKey subKey = uninstallKey.OpenSubKey(subKeyName))
                        {
                            if (subKey.GetValue("DisplayName") is string displayName && displayName.Contains("Autodesk Revit"))
                            {
                                string pattern = @"Autodesk Revit \d{4}";
                                if (displayName.Length == "Autodesk Revit 2020".Length && Regex.Match(displayName, pattern).Success)
                                    installedVersions.Add(displayName);
                            }
                        }
                    }
                }
            }

            return installedVersions;
        }

        private void CancelDownloadButtonClick(object sender, RoutedEventArgs e)
        {
            MainButton.Content = "下载安装";
            ProgressBarGrid.Visibility = Visibility.Collapsed;
            LocationGrid.Visibility = Visibility.Visible;
            MainButton.Click -= CancelDownloadButtonClick;
            MainButton.Click += DownloadAndInstallButtonClick;
            _cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// 安装完成
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OkButtonClick(object sender, RoutedEventArgs e)
        {
            Closed += OnClosed;
            Close();
        }

        private async Task DownloadFilelist()
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    using (var response = await httpClient.GetAsync(_appModel.URL + "files.txt", HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token))
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        var files = responseText.Split('\n').Select(x => x.Trim('\r')).ToArray();
                        if (!File.Exists(InstallPath.Text))
                        {
                            Directory.CreateDirectory(InstallPath.Text);
                        }
                        _filesCount = files.Length;
                        var start = 0d;
                        var interval = 100d / _filesCount;
                        foreach (var file in files)
                        {
                            var filePath = Path.Combine(InstallPath.Text, file);
                            await DownloadFileAsync(start, _appModel.URL + file, filePath);
                            if (_cancellationTokenSource.IsCancellationRequested)
                                Dispatcher.Invoke(() =>
                                {
                                    DownloadProgress.Value = 0;
                                    DownloadProgressTB.Text = $"0%";
                                });
                            start += interval;
                            var ext = Path.GetExtension(filePath);
                            if (ext == ".zip")
                            {
                                var zipName = Path.GetFileNameWithoutExtension(file);
                                await ExtractZipFileAsync(filePath, Path.Combine(InstallPath.Text, zipName));
                                File.Delete(filePath);
                            }
                        }
                        Dispatcher.Invoke(() =>
                        {
                            MainButton.Content = "完成";
                            MainButton.Click -= DownloadAndInstallButtonClick;
                            MainButton.Click -= CancelDownloadButtonClick;
                            MainButton.Click += OkButtonClick;
                        });
                    }
                }
                catch (TaskCanceledException)
                {
                    Dispatcher.Invoke(() =>
                    {
                        DownloadProgress.Value = 0;
                        DownloadProgressTB.Text = $"0%";
                    });
                }
            }
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="start"></param>
        /// <param name="url"></param>
        /// <param name="outputPath"></param>
        /// <returns></returns>
        private async Task DownloadFileAsync(double start, string url, string outputPath)
        {
            using (var httpClient = new HttpClient())
            {
                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var totalRead = 0L;
                            var buffer = new byte[8192];
                            var isMoreToRead = true;
                            do
                            {
                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fileStream.WriteAsync(buffer, 0, read);

                                    totalRead += read;
                                    var percentage = (double)totalRead / totalBytes * 100;
                                    Dispatcher.Invoke(() =>
                                    {
                                        DownloadProgress.Value = start + percentage / _filesCount;
                                        DownloadProgressTB.Text = $"{start + percentage / _filesCount:f0}%";
                                    });
                                }
                            }
                            while (isMoreToRead && !_cancellationTokenSource.IsCancellationRequested);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 编写解压方法
        /// </summary>
        /// <param name="zipPath"></param>
        /// <param name="extractPath"></param>
        public static Task ExtractZipFileAsync(string zipPath, string extractPath)
        {
            return Task.Run(() =>
            {
                // 确保目标解压目录存在
                Directory.CreateDirectory(extractPath);

                // 打开ZIP文件读取内容
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var encoding = Encoding.GetEncoding("gbk");
                using (var archive = new ZipArchive(new FileStream(zipPath, FileMode.Open), ZipArchiveMode.Read, false, encoding))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        // 获取解压目标文件的完整路径
                        string destinationPath = Path.Combine(extractPath, entry.FullName);
                        if (entry.FullName.EndsWith("/"))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            var ddPath = Path.GetDirectoryName(destinationPath);
                            if (ddPath == null) continue;
                            // 确保文件的目标目录存在
                            Directory.CreateDirectory(ddPath);

                            // 提取文件
                            entry.ExtractToFile(destinationPath, overwrite: true);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 选择安装目录点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectInstallPathButtonClick(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new FolderBrowserDialog();

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var folderPath = openFileDialog.SelectedPath;
                InstallPath.Text = Path.Combine(folderPath, _appModel.EN);
            }
        }
    }
}