using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Web.Configuration;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace ContosoBankBot.Services
{
    public class AccessTokenInfo
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string scope { get; set; }
    }

    public sealed class Authentication
    {
        private static readonly object LockObject;
        private static readonly string ApiKey;
        private AccessTokenInfo token;
        private Timer timer;

        static Authentication()
        {
            LockObject = new object();
            ApiKey = WebConfigurationManager.AppSettings["MicrosoftSpeechApiKey"];
        }

        private Authentication()
        {
        }

        public static Authentication Instance { get; } = new Authentication();

        /// <summary>
        /// Gets the current access token.
        /// </summary>
        /// <returns>Current access token</returns>
        public AccessTokenInfo GetAccessToken()
        {
            // Token will be null first time the function is called.
            if (this.token == null)
            {
                lock (LockObject)
                {
                    // This condition will be true only once in the lifetime of the application
                    if (this.token == null)
                    {
                        this.RefreshToken();
                    }
                }
            }

            return this.token;
        }

        /// <summary>
        /// Issues a new AccessToken from the Speech Api
        /// </summary>
        /// This method couldn't be async because we are calling it inside of a lock.
        /// <returns>AccessToken</returns>
        private static AccessTokenInfo GetNewToken()
        {
            using (var client = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", ApiKey },
                    { "client_secret", ApiKey },
                    { "scope", "https://speech.platform.bing.com" }
                };

                var content = new FormUrlEncodedContent(values);

                var response = client.PostAsync("https://oxford-speech.cloudapp.net/token/issueToken", content).Result;

                var responseString = response.Content.ReadAsStringAsync().Result;

                return JsonConvert.DeserializeObject<AccessTokenInfo>(responseString);
            }
        }

        /// <summary>
        /// Refreshes the current token before it expires. This method will refresh the current access token.
        /// It will also schedule itself to run again before the newly acquired token's expiry by one minute.
        /// </summary>
        private void RefreshToken()
        {
            this.token = GetNewToken();
            this.timer?.Dispose();
            this.timer = new Timer(
                x => this.RefreshToken(),
                null,
                TimeSpan.FromSeconds(this.token.expires_in).Subtract(TimeSpan.FromMinutes(1)), // Specifies the delay before RefreshToken is invoked.
                TimeSpan.FromMilliseconds(-1)); // Indicates that this function will only run once
        }
    }

    public class MicrosoftCognitiveSpeechService
    {
        /// <summary>
        /// Gets text from an audio stream.
        /// </summary>
        /// <param name="audiostream"></param>
        /// <returns>Transcribed text. </returns>
        public async Task<string> GetTextFromAudioAsync(Stream audiostream)
        {
            var requestUri = @"https://speech.platform.bing.com/recognize?scenarios=smd&appid=D4D52672-91D7-4C74-8AD8-42B1D98141A5&locale=en-US&device.os=bot&form=BCSSTT&version=3.0&format=json&instanceid=565D69FF-E928-4B7E-87DA-9A750B96D9E3&requestid=" + Guid.NewGuid();

            using (var client = new HttpClient())
            {
                var token = Authentication.Instance.GetAccessToken();
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.access_token);

                using (var binaryContent = new ByteArrayContent(StreamToBytes(audiostream)))
                {
                    binaryContent.Headers.TryAddWithoutValidation("content-type", "audio/wav; codec=\"audio/pcm\"; samplerate=16000");

                    var response = await client.PostAsync(requestUri, binaryContent);
                    var responseString = await response.Content.ReadAsStringAsync();
                    dynamic data = JsonConvert.DeserializeObject(responseString);
                    return data.header.name;
                }
            }
        }

        /// <summary>
        /// Converts Stream into byte[].
        /// </summary>
        /// <param name="input">Input stream</param>
        /// <returns>Output byte[]</returns>
        private static byte[] StreamToBytes(Stream input)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}