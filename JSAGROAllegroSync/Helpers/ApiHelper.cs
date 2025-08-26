using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models.Product;
using Serilog;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Helpers
{
    public static class ApiHelper
    {
        public static void AddDefaultHeaders(GaskaApiCredentials apiSettings, HttpClient httpClient)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiSettings.Acronym}|{apiSettings.Person}:{apiSettings.Password}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            httpClient.DefaultRequestHeaders.Add("X-Signature", ApiHelper.GetSignature(apiSettings));
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private static string GetSignature(GaskaApiCredentials apiSettings)
        {
            string body = $"acronym={apiSettings.Acronym}&person={apiSettings.Person}&password={apiSettings.Password}&key={apiSettings.ApiKey}";
            using (SHA256Managed sha = new SHA256Managed())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}