using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HttpMonitorApp.Models;
using HttpMonitorApp.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HttpMonitorApp
{
    public partial class MainWindow : Window
    {
        private HttpServerService _serverService;
        private HttpClientService _clientService;
        private readonly ConcurrentQueue<RequestLog> _allLogs;
        private readonly ObservableCollection<RequestLog> _filteredLogs;
        private readonly ObservableCollection<MinuteStats> _minuteStats;
        private Timer _statsTimer;
        private PlotModel _plotModel;

        public MainWindow()
        {
            InitializeComponent();

            _allLogs = new ConcurrentQueue<RequestLog>();
            _filteredLogs = new ObservableCollection<RequestLog>();
            _minuteStats = new ObservableCollection<MinuteStats>();

            LogsListBox.ItemsSource = _filteredLogs;
            RequestsDataGrid.ItemsSource = _minuteStats;

            _serverService = new HttpServerService(_allLogs);
            _clientService = new HttpClientService();

            // Подписка на события
            _serverService.OnRequestLogged += OnRequestLogged;
            _serverService.OnStatsUpdated += OnStatsUpdated;
            _clientService.OnRequestLogged += OnRequestLogged;

            // Инициализация графика
            InitializePlot();

            // Таймер для обновления статистики по минутам
            _statsTimer = new Timer(UpdateMinuteStats, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));

            // Обработчик изменения метода
            MethodComboBox.SelectionChanged += MethodComboBox_SelectionChanged;
        }

        private void InitializePlot()
        {
            _plotModel = new PlotModel { Title = "Запросы в минуту" };

            var dateAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm",
                Title = "Время"
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0,
                Title = "Количество запросов"
            };

            _plotModel.Axes.Add(dateAxis);
            _plotModel.Axes.Add(valueAxis);

            LoadPlot.Model = _plotModel;
        }

        private void OnRequestLogged(RequestLog log)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyFilters();
                LogToFile(log);
            });
        }

        private void OnStatsUpdated(ServerStats stats)
        {
            Dispatcher.Invoke(() =>
            {
                StatsTextBlock.Text = $"Всего запросов: {stats.TotalRequests}\n" +
                                     $"GET: {stats.GetRequests}\n" +
                                     $"POST: {stats.PostRequests}\n" +
                                     $"Среднее время обработки: {stats.AverageProcessingTime:F2} мс\n" +
                                     $"Время работы: {stats.Uptime:hh\\:mm\\:ss}";
            });
        }

        private void LogToFile(RequestLog log)
        {
            try
            {
                var logMessage = $"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] " +
                                $"{(log.IsIncoming ? "ВХОД" : "ИСХОД")} {log.Method} " +
                                $"{log.Url} -> {log.StatusCode} ({log.ProcessingTimeMs}ms)\n" +
                                $"Заголовки: {log.Headers}\n" +
                                $"Тело запроса: {log.RequestBody}\n" +
                                $"Тело ответа: {log.ResponseBody}\n" +
                                $"{new string('-', 50)}\n";

                System.IO.File.AppendAllText("logs.txt", logMessage);
            }
            catch { }
        }

        private void ApplyFilters()
        {
            var logs = _allLogs.ToList();

            // Фильтр по методу
            var methodFilter = ((ComboBoxItem)LogFilterComboBox.SelectedItem)?.Content.ToString();
            if (methodFilter != "Все" && !string.IsNullOrEmpty(methodFilter))
            {
                logs = logs.Where(l => l.Method == methodFilter).ToList();
            }

            // Фильтр по статусу
            var statusFilter = ((ComboBoxItem)StatusFilterComboBox.SelectedItem)?.Content.ToString();
            if (statusFilter != "Все" && !string.IsNullOrEmpty(statusFilter))
            {
                if (statusFilter.Contains("200"))
                    logs = logs.Where(l => l.StatusCode == 200).ToList();
                else if (statusFilter.Contains("201"))
                    logs = logs.Where(l => l.StatusCode == 201).ToList();
                else if (statusFilter.Contains("400"))
                    logs = logs.Where(l => l.StatusCode == 400).ToList();
                else if (statusFilter.Contains("500"))
                    logs = logs.Where(l => l.StatusCode == 500).ToList();
            }

            _filteredLogs.Clear();
            foreach (var log in logs.TakeLast(100)) // Показываем последние 100 записей
            {
                _filteredLogs.Add(log);
            }
        }

        private void UpdateMinuteStats(object state)
        {
            Dispatcher.Invoke(() =>
            {
                var now = DateTime.Now;
                var minuteAgo = now.AddMinutes(-1);

                var recentLogs = _allLogs
                    .Where(l => l.Timestamp >= minuteAgo)
                    .ToList();

                var stat = new MinuteStats
                {
                    Time = now.ToString("HH:mm"),
                    GetCount = recentLogs.Count(l => l.Method == "GET"),
                    PostCount = recentLogs.Count(l => l.Method == "POST"),
                    TotalCount = recentLogs.Count
                };

                _minuteStats.Add(stat);

                // Обновляем график
                UpdatePlot();
            });
        }

        private void UpdatePlot()
        {
            _plotModel.Series.Clear();

            var getSeries = new LineSeries
            {
                Title = "GET",
                MarkerType = MarkerType.Circle
            };

            var postSeries = new LineSeries
            {
                Title = "POST",
                MarkerType = MarkerType.Square
            };

            var baseTime = DateTime.Now.AddMinutes(-_minuteStats.Count);

            for (int i = 0; i < _minuteStats.Count; i++)
            {
                var time = baseTime.AddMinutes(i + 1);
                getSeries.Points.Add(DateTimeAxis.CreateDataPoint(time, _minuteStats[i].GetCount));
                postSeries.Points.Add(DateTimeAxis.CreateDataPoint(time, _minuteStats[i].PostCount));
            }

            _plotModel.Series.Add(getSeries);
            _plotModel.Series.Add(postSeries);
            LoadPlot.InvalidatePlot(true);
        }

        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Введите корректный порт (1-65535)", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _serverService.Start(port);

                ServerStatusLabel.Content = $"Сервер запущен на порту {port}";
                ServerStatusLabel.Foreground = System.Windows.Media.Brushes.Green;
                StartServerButton.IsEnabled = false;
                StopServerButton.IsEnabled = true;
                PortTextBox.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска сервера: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            _serverService.Stop();

            ServerStatusLabel.Content = "Сервер остановлен";
            ServerStatusLabel.Foreground = System.Windows.Media.Brushes.Gray;
            StartServerButton.IsEnabled = true;
            StopServerButton.IsEnabled = false;
            PortTextBox.IsEnabled = true;
        }

        private async void SendRequestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = UrlTextBox.Text;
                if (string.IsNullOrWhiteSpace(url))
                {
                    MessageBox.Show("Введите URL", "Ошибка", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var method = ((ComboBoxItem)MethodComboBox.SelectedItem).Content.ToString();
                SendRequestButton.IsEnabled = false;

                string response;
                RequestLog log;

                if (method == "GET")
                {
                    (response, log) = await _clientService.SendGetRequest(url);
                }
                else
                {
                    var body = RequestBodyTextBox.Text;
                    (response, log) = await _clientService.SendPostRequest(url, body);
                }

                ResponseTextBox.Text = response;
                ResponseTimeLabel.Content = $"{log.ProcessingTimeMs} мс";
                ResponseStatusLabel.Content = log.StatusCode.ToString();
            }
            catch (Exception ex)
            {
                ResponseTextBox.Text = $"Error: {ex.Message}";
                ResponseStatusLabel.Content = "Error";
            }
            finally
            {
                SendRequestButton.IsEnabled = true;
            }
        }

        private void MethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var method = ((ComboBoxItem)MethodComboBox.SelectedItem)?.Content.ToString();
            RequestBodyTextBox.IsEnabled = method == "POST";

            if (method == "GET")
            {
                UrlTextBox.Text = "http://localhost:8080/";
            }
        }

        private void LogFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }
    }

    public class MinuteStats
    {
        public string Time { get; set; }
        public int GetCount { get; set; }
        public int PostCount { get; set; }
        public int TotalCount { get; set; }
    }
}