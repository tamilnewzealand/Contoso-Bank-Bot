using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;
using System.Collections.Generic;


namespace ContosoBankBot
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        private async Task<string> GetBalance(Entity[] entities)
        {
            string message;
            switch (entities[0].entity.ToLower())
            {
                case "current":
                    message = "Your current account balance is $5000.00.";
                    break;
                case "savings":
                    message = "Your savings account balance is $7000.00.";
                    break;
                case "credit card":
                    message = "Your credit card balance is $3000.00.";
                    break;
                case "credit":
                    message = "Your credit card balance is $3000.00.";
                    break;
                case "card":
                    message = "Your credit card balance is $3000.00.";
                    break;
                default:
                    message = "Your current account balance is $5000.00.\r\n Your savings account balance is $7000.00.\r\n Your credit card balance is $3000.00.";
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
        /// Receive a message from a user and reply to it
        /// </summary>
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            if (activity.Type == ActivityTypes.Message)
            {
                ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));

                var userMessage = activity.Text;

                StateClient stateClient = activity.GetStateClient();
                BotData userData = await stateClient.BotState.GetUserDataAsync(activity.ChannelId, activity.From.Id);

                if (!userData.GetProperty<bool>("LoggedIn"))
                {
                    Activity replyToConversation = activity.CreateReply();
                    replyToConversation.Recipient = activity.From;
                    replyToConversation.Type = "message";

                    replyToConversation.Attachments = new List<Attachment>();
                    List<CardAction> cardButtons = new List<CardAction>();
                    CardAction plButton = new CardAction()
                    {
                        Value = $"http://contosobankbot.azurewebsites.net/Home/Login.html",
                        Type = "signin",
                        Title = "Authentication Required"
                    };
                    cardButtons.Add(plButton);
                    SigninCard plCard = new SigninCard("Please login to Contoso Bank", new List<CardAction>() { plButton });
                    Attachment plAttachment = plCard.ToAttachment();
                    replyToConversation.Attachments.Add(plAttachment);

                    var reply = await connector.Conversations.SendToConversationAsync(replyToConversation);

                    userData.SetProperty<bool>("LoggedIn", true);
                    await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);
                }
                else
                {
                    string message;
                    Luis StLUIS = await GetEntityFromLUIS(userMessage);
                    if (StLUIS.intents.Count() > 0)
                    {
                        switch (StLUIS.intents[0].intent)
                        {
                            case "StockPrice":
                                message = await GetStock(StLUIS.entities[0].entity);
                                break;
                            case "Logout":
                                userData.SetProperty<bool>("LoggedIn", false);
                                await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);
                                message = "You have been successfully logged out.";
                                break;
                            case "CheckBalance":
                                message = await GetBalance(StLUIS.entities);
                                break;
                            default:
                                message = "Sorry, I am not getting you...";
                                break;
                        }
                    }
                    else
                    {
                        message = "Sorry, I am not getting you...";
                    }
                    Activity infoReply = activity.CreateReply(message);
                    await connector.Conversations.ReplyToActivityAsync(infoReply);
                }

            }
            else
            {
                HandleSystemMessage(activity);
            }
            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }

        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                // Implement user deletion here
                // If we handle user deletion, return a real message
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }
    }
}