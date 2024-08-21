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
        private async void DownloadAndInstallButtonClick(object sender, RoutedEventArgs e)
        {
            MainButton.Content = "取消";
            MainButton.Click -= DownloadAndInstallButtonClick;
            MainButton.Click += CancelDownloadButtonClick;
            _cancellationTokenSource = new CancellationTokenSource();

            ProgressBarGrid.Visibility = Visibility.Visible;
            LocationGrid.Visibility = Visibility.Collapsed;
            await DownloadFilelist();
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