using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Builder.Dialogs;
using System.Web.Http.Description;
using System.Net.Http;
using System.Diagnostics;
using System;
using Microsoft.Bot.Sample.SimpleFacebookAuthBot.Services;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Net.Http.Headers;
using System.Globalization;

namespace Microsoft.Bot.Sample.SimpleFacebookAuthBot
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        private readonly MicrosoftCognitiveSpeechService speechService = new MicrosoftCognitiveSpeechService();

        private string SendMoney(Entity[] entities)
        {
            Models.BotDataEntities DB = new Models.BotDataEntities();
            Models.UserLog NewUserLog = new Models.UserLog();
            foreach (var Entity in entities)
            {
                if (Entity.type == "ToFrom") NewUserLog.ToFrom = Entity.entity;
                if (Entity.type == "builtin.money")
                {
                    string csd = Entity.entity;
                    csd = csd.Replace(" ", "");
                    csd = csd.Replace("$", "");
                    NewUserLog.Value = decimal.Parse(csd, NumberStyles.Currency);
                }
                if (Entity.type == "Account") NewUserLog.AccountType = Entity.entity;
            }

            NewUserLog.UserID = "CurrentUser";
            NewUserLog.NewBalance = CalcBalance(NewUserLog.AccountType) - NewUserLog.Value;
            NewUserLog.Created = DateTime.UtcNow;
            NewUserLog.Message = "Test message";
            DB.UserLogs.Add(NewUserLog);
            DB.SaveChanges();

            string message = "You have successfully sent $" + NewUserLog.Value + " from " + NewUserLog.AccountType + " to " + NewUserLog.ToFrom;
            return message;
        }

        private decimal CalcBalance(string Account)
        {
            Models.BotDataEntities DB = new Models.BotDataEntities();
            decimal value = 0.00M;
            var transactions = (from UserLog in DB.UserLogs
                                where UserLog.AccountType == Account
                                select UserLog);
            foreach (var Score in transactions)
            {
                value = Score.NewBalance;
            }
            return value;
        }

        private string GetBalance(Entity[] entities)
        {
            string message;
            switch (entities[0].entity.ToLower())
            {
                case "current":
                    message = "Your current account balance is $" + CalcBalance("current");
                    break;
                case "savings":
                    message = "Your savings balance is $" + CalcBalance("savings");
                    break;
                case "credit card":
                    message = "Your credit card balance is $" + CalcBalance("credit card");
                    break;
                default:
                    message = "Your current account balance is $" + CalcBalance("current") + ".\n\n Your savings account balance is $" + CalcBalance("savings") + ".\n\n Your credit card balance is $" + CalcBalance("credit card") + ".\n\n";
                    break;
            }
            return message;
        }

        private string GetRecent(Entity[] entities)
        {
            string message;
            Models.BotDataEntities DB = new Models.BotDataEntities();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            switch (entities[0].entity.ToLower())
            {
                case "current":
                    var transactions = (from UserLog in DB.UserLogs
                                        where UserLog.AccountType == "current"
                                        select UserLog)
                                        .Take(5);
                    sb.Append("Recent Transactions for Current:\n\n");
                    foreach (var Score in transactions)
                    {
                        sb.Append(String.Format("Transferred {0} to {1} on ({2} {3})\n\n"
                            , Score.Value
                            , Score.ToFrom
                            , Score.Created.ToLocalTime().ToShortDateString()
                            , Score.Created.ToLocalTime().ToShortTimeString()));
                    }
                    message = sb.ToString();
                    break;
                case "savings":
                    var transactionsav = (from UserLog in DB.UserLogs
                                          where UserLog.AccountType == "savings"
                                          select UserLog)
                                        .Take(5);
                    sb.Append("Recent Transactions for Savings:\n");
                    foreach (var Score in transactionsav)
                    {
                        sb.Append(String.Format("Transferred {0} to {1} on ({2} {3})\n"
                            , Score.Value
                            , Score.ToFrom
                            , Score.Created.ToLocalTime().ToShortDateString()
                            , Score.Created.ToLocalTime().ToShortTimeString()));
                    }
                    message = sb.ToString();
                    break;
                case "credit card":
                    var transactioncred = (from UserLog in DB.UserLogs
                                           where UserLog.AccountType == "credit"
                                           select UserLog)
                                          .Take(5);
                    sb.Append("Recent Transactions for Credit Card:\n");
                    foreach (var Score in transactioncred)
                    {
                        sb.Append(String.Format("Transferred {0} to {1} on ({2} {3})\n"
                            , Score.Value
                            , Score.ToFrom
                            , Score.Created.ToLocalTime().ToShortDateString()
                            , Score.Created.ToLocalTime().ToShortTimeString()));
                    }
                    message = sb.ToString();
                    break;
                default:
                    message = "Sorry, I am not getting you...";
                    break;
            }
            return message;
        }

        private async Task<string> GetStock(string StockSymbol)
        {
            double? dblStockValue = await YahooBot.GetStockRateAsync(StockSymbol);
            if (dblStockValue == null)
            {
                return string.Format("This \"{0}\" is not an valid stock symbol", StockSymbol);
            }
            else
            {
                return string.Format("Stock Price of {0} is ${1}", StockSymbol, dblStockValue);
            }
        }

        private static async Task<Luis> GetEntityFromLUIS(string Query)
        {
            Query = Uri.EscapeDataString(Query);
            Luis Data = new Luis();
            using (HttpClient client = new HttpClient())
            {
                string RequestURI = "https://api.projectoxford.ai/luis/v2.0/apps/abb9c320-d690-4062-acff-f6835344be3f?subscription-key=ea3d5065b78345c9b0d85a9c45874702&q=" + Query + "&verbose=true";
                HttpResponseMessage msg = await client.GetAsync(RequestURI);

                if (msg.IsSuccessStatusCode)
                {
                    var JsonDataResponse = await msg.Content.ReadAsStringAsync();
                    Data = JsonConvert.DeserializeObject<Luis>(JsonDataResponse);
                }
            }
            return Data;
        }

        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and send replies
        /// </summary>
        /// <param name="activity"></param>
        [ResponseType(typeof(void))]
        public virtual async Task<HttpResponseMessage> Post([FromBody] Activity activity)
        {
            if (activity != null)
            {
                // one of these will have an interface and process it
                if (activity.GetActivityType() == ActivityTypes.Message)
                {
                    if (activity.Text == "login" | activity.Text == "hi" | activity.Text == "logout")
                    {
                        await Conversation.SendAsync(activity, () => SimpleFacebookAuthDialog.dialog);
                    }
                    else
                    {
                        ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));
                        string userMessage = activity.Text;
                        string message = "Sorry, I am not getting you...";

                        try
                        {
                            var audioAttachment = activity.Attachments?.FirstOrDefault(a => a.ContentType.Equals("audio/wav") || a.ContentType.Equals("application/octet-stream"));
                            if (audioAttachment != null)
                            {
                                var stream = await GetImageStream(connector, audioAttachment);
                                var text = await this.speechService.GetTextFromAudioAsync(stream);
                                userMessage = text;
                            }
                        }
                        catch
                        {
                        }

                        Luis StLUIS = await GetEntityFromLUIS(userMessage);
                        if (StLUIS.intents.Count() > 0)
                        {
                            switch (StLUIS.intents[0].intent)
                            {
                                case "StockPrice":
                                    message = await GetStock(StLUIS.entities[0].entity);
                                    break;
                                case "SendMoney":
                                    message = SendMoney(StLUIS.entities);
                                    break;
                                case "GetRecent":
                                    message = GetRecent(StLUIS.entities);
                                    break;
                                case "CheckBalance":
                                    message = GetBalance(StLUIS.entities);
                                    break;
                                default:
                                    message = "Sorry, I am not getting you...";
                                    break;
                            }
                        }
                        Activity infoReply = activity.CreateReply(message);
                        await connector.Conversations.ReplyToActivityAsync(infoReply);
                    }
                }
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted);
        }
        private static async Task<Stream> GetImageStream(ConnectorClient connector, Attachment imageAttachment)
        {
            using (var httpClient = new HttpClient())
            {
                // The Skype attachment URLs are secured by JwtToken,
                // you should set the JwtToken of your bot as the authorization header for the GET request your bot initiates to fetch the image.
                // https://github.com/Microsoft/BotBuilder/issues/662
                var uri = new Uri(imageAttachment.ContentUrl);
                if (uri.Host.EndsWith("skype.com") && uri.Scheme == "https")
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(connector));
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                }
                else
                {
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(imageAttachment.ContentType));
                }

                return await httpClient.GetStreamAsync(uri);
            }
        }

        /// <summary>
        /// Gets the JwT token of the bot. 
        /// </summary>
        /// <param name="connector"></param>
        /// <returns>JwT token of the bot</returns>
        private static async Task<string> GetTokenAsync(ConnectorClient connector)
        {
            var credentials = connector.Credentials as MicrosoftAppCredentials;
            if (credentials != null)
            {
                return await credentials.GetTokenAsync();
            }

            return null;
        }
    }
}