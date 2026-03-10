using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DSE.Desktop
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient HttpClient = new()
        {
            BaseAddress = new Uri("http://localhost:5070/")
        };

        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing;

        public MainWindow()
        {
            InitializeComponent();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
            _refreshTimer.Start();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _refreshTimer.Stop();
        }

        private async void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshStatusAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        private async Task RefreshStatusAsync()
        {
            if (_isRefreshing)
                return;

            _isRefreshing = true;

            try
            {
                var status = await HttpClient.GetFromJsonAsync<AgentStatusDto>("status");

                if (status == null)
                {
                    ShowOfflineState("No data returned from worker.");
                    return;
                }

                ServiceNameText.Text = status.Service;
                StatusText.Text = status.Status;
                MachineNameText.Text = status.MachineName;
                VersionText.Text = status.Version;
                StartedAtText.Text = status.StartedAt;
                LastHeartbeatText.Text = status.LastHeartbeat;
                UptimeText.Text = status.UptimeSeconds.ToString();
            }
            catch (Exception ex)
            {
                ShowOfflineState(ex.Message);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void ShowOfflineState(string reason)
        {
            ServiceNameText.Text = "Unavailable";
            StatusText.Text = "Offline";
            MachineNameText.Text = Environment.MachineName;
            VersionText.Text = "-";
            StartedAtText.Text = "-";
            LastHeartbeatText.Text = "-";
            UptimeText.Text = reason;
        }
    }

    public class AgentStatusDto
    {
        [JsonPropertyName("service")]
        public string Service { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("startedAt")]
        public string StartedAt { get; set; } = "";

        [JsonPropertyName("lastHeartbeat")]
        public string LastHeartbeat { get; set; } = "";

        [JsonPropertyName("uptimeSeconds")]
        public int UptimeSeconds { get; set; }
    }
}