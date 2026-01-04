using System;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Threading.Tasks;

namespace BACTBiometricClient.Helpers
{
    /// <summary>
    /// Helper class to check internet connectivity
    /// </summary>
    public static class InternetChecker
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        /// <summary>
        /// Check if internet connection is available (quick check)
        /// </summary>
        public static bool IsConnected()
        {
            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if internet connection is available by pinging a reliable server (async)
        /// </summary>
        public static async Task<bool> IsConnectedAsync()
        {
            if (!IsConnected())
                return false;

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 3000); // Google DNS
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a specific API URL is reachable
        /// </summary>
        public static async Task<bool> CanReachApi(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                return false;

            if (!IsConnected())
                return false;

            try
            {
                // Try to make a HEAD request to the API
                var response = await _httpClient.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                // Unauthorized is OK - means API is reachable but needs auth
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get connection status as string
        /// </summary>
        public static string GetConnectionStatus()
        {
            return IsConnected() ? "Online" : "Offline";
        }

        /// <summary>
        /// Get detailed connection status
        /// </summary>
        public static string GetDetailedConnectionStatus()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
                return "No Network Available";

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        return "Connected (WiFi)";
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                        return "Connected (Ethernet)";
                }
            }

            return "Connected (Unknown)";
        }
    }
}