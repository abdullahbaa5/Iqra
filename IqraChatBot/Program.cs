using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Rystem.OpenAi;
using Rystem.OpenAi.Chat;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IqraChatBot
{
    public class Program
    {
        private IConfiguration _appConfig;

        private DiscordSocketClient _discordClient;
        private ulong _currentDiscordClientId;

        private IOpenAiChat _openAiChatApi;
        private decimal _currentChatApiCost = 0;

        private ChatModelType? _iqraChatModel = null;
        private ChatModelType? _iqraRelevancyChatModel = null;

        private string _iqraSystemContext;
        private string _iqraRelevancySystemContext;
        
        private int _previousMessagesContextLimit;
        private int _iqraRelevancyMessagesContextLimit;

        private ulong _generalChatChannel;
        private List<ulong> _discordAdminIds;

        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        // Dynamic Variables
        private string _iqraLastMessageContent = "no_reply_no_reply_no_reply_no_reply";

        // Init
        public static void Main(string[] args)
            => new Program().MainAsync(args).GetAwaiter().GetResult();
        public async Task MainAsync(string[] args)
        {
            // Initalize Application Settings from appsettings.json
            InitalizeAppSettingsConfiguration();

            // Initalize App Related Config
            InitalizeAppConfig();

            // Initalize Open AI Chat Factory with API Token
            InitalizeOpenAIChat();

            // Initalize Discord Bot with Bot Token
            await InitalizeDiscordBot();   

            // Initalized!!!
            ConsoleWriteLog(
                "Ready to Recieve & Process Messages!",
                ConsoleColor.Green
            );
            await Task.Delay(-1);
        }

        /**
         * 
         * Functions Related To Initalizing The App
         *
        **/
        private void InitalizeAppSettingsConfiguration()
        {
            var environmentName = Environment.GetEnvironmentVariable("IQRA_RUN_ENVIRONMENT");
            if (environmentName == null) environmentName = "development";

            var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

            _appConfig = builder.Build();
        }
        private void InitalizeOpenAIChat()
        {
            string? openAIToken = _appConfig.GetSection("OpenAIApiToken").Value;
            if (string.IsNullOrWhiteSpace(openAIToken))
            {
                throw new Exception("OpenAIApiToken value is empty or invaid in appsettings json file.");
            }

            OpenAiService.Instance.AddOpenAi(settings =>
            {
                settings.ApiKey = openAIToken;
            }, "IqraChat");

            _openAiChatApi = OpenAiService.Factory.CreateChat("IqraChat");
        }
        private async Task InitalizeDiscordBot()
        {
            string? discordBotToken = _appConfig.GetSection("DiscordBotToken").Value;
            if (string.IsNullOrWhiteSpace(discordBotToken))
            {
                throw new Exception("DiscordBotToken value is empty or invaid in appsettings json file.");
            }

            DiscordSocketConfig config = new DiscordSocketConfig()
            {
                GatewayIntents = GatewayIntents.MessageContent | GatewayIntents.AllUnprivileged
            };

            _discordClient = new DiscordSocketClient(config);

            // Login Discord Bot
            await _discordClient.LoginAsync(TokenType.Bot, discordBotToken);
            Stopwatch discordTimeout = Stopwatch.StartNew();
            while (_discordClient.LoginState != LoginState.LoggedIn)
            {
                if (discordTimeout.Elapsed.TotalSeconds > 10)
                {
                    throw new Exception("Timed out while trying to wait for the discord client to log-in.\nWaited 10 seconds.");
                }
                await Task.Delay(10);
            }

            // Connect Discord Bot
            discordTimeout.Restart();
            await _discordClient.StartAsync();
            while (_discordClient.ConnectionState != ConnectionState.Connected)
            {
                if (discordTimeout.Elapsed.TotalSeconds > 10)
                {
                    throw new Exception("Timed out while trying to wait for the discord client to get connected.\nWaited 10 seconds.");
                }
                await Task.Delay(10);
            }
            _currentDiscordClientId = _discordClient.CurrentUser.Id;

            // Check if the given General Chat Channel exists
            if ((await _discordClient.GetChannelAsync(_generalChatChannel)) == null)
            {
                throw new Exception("Discord Text Channel not found for the given General Chat Channel Id.");
            }

            // Check if the given Admin Users exists
            foreach (var discordAdminId in _discordAdminIds)
            {
                if ((await _discordClient.GetUserAsync(discordAdminId)) == null)
                {
                    throw new Exception($"Discord User not found for the given Channel Admin Id {discordAdminId}.");
                }
            }

            // Finally bind message receiving
            _discordClient.MessageReceived += MessageReceived;
        }
        private void InitalizeAppConfig()
        {
            // Iqra's Open AI Chat Models Type
            _iqraChatModel = _appConfig.GetValue<ChatModelType>("IqraChatModel");
            if (_iqraChatModel == null)
            {
                throw new Exception("IqraChatModel value is empty or invaid in appsettings json file.");
            }
            _iqraRelevancyChatModel = _appConfig.GetValue<ChatModelType>("IqraRelevancyChatModel");
            if (_iqraRelevancyChatModel == null)
            {
                throw new Exception("IqraRelevancyChatModel value is empty or invaid in appsettings json file.");
            }

            // Iqra's OpenAI General Chat System Context
            string IqraSystemContextPath = _appConfig.GetSection("IqraSystemContextPath").Value;
            if (IqraSystemContextPath == null)
            {
                throw new Exception("IqraSystemContextPath value is empty or invaid in appsettings json file.");
            }
            if (!File.Exists(IqraSystemContextPath))
            {
                throw new Exception("IqraSystemContextPath file not found at the specified path.");
            }
            _iqraSystemContext = File.ReadAllText(IqraSystemContextPath);

            // Iqra's OpenAI Check Relevancy In Chat System Context
            string IqraRelevancySystemContext = _appConfig.GetSection("IqraRelevancySystemContext").Value;
            if (IqraRelevancySystemContext == null)
            {
                throw new Exception("IqraRelevancySystemContext value is empty or invaid in appsettings json file.");
            }
            if (!File.Exists(IqraRelevancySystemContext))
            {
                throw new Exception("IqraRelevancySystemContext file not found at the specified path.");
            }
            _iqraRelevancySystemContext = File.ReadAllText(IqraRelevancySystemContext);

            // Initalize OpenAI Messages Limit
            string PreviousMessagesContextLimit = _appConfig.GetSection("PreviousMessagesContextLimit").Value;
            if (PreviousMessagesContextLimit == null || !int.TryParse(PreviousMessagesContextLimit, out _previousMessagesContextLimit))
            {
                throw new Exception("PreviousMessagesContextLimit value is empty or invaid in appsettings json file.");
            }
            string IqraRelevancyMessagesContextLimit = _appConfig.GetSection("IqraRelevancyMessagesContextLimit").Value;
            if (IqraRelevancyMessagesContextLimit == null || !int.TryParse(IqraRelevancyMessagesContextLimit, out _iqraRelevancyMessagesContextLimit))
            {
                throw new Exception("IqraRelevancyMessagesContextLimit value is empty or invaid in appsettings json file.");
            }

            // Initalize Discord Related Ids
            string GeneralChatChannelId = _appConfig.GetSection("GeneralChatChannelId").Value;
            if (GeneralChatChannelId == null || !ulong.TryParse(GeneralChatChannelId, out _generalChatChannel))
            {
                throw new Exception("GeneralChatChannelId value is empty or invaid in appsettings json file.");
            }
            List<ulong>? DiscordAdminIds = _appConfig.GetSection("DiscordAdminIds").Get<List<ulong>>();
            if (DiscordAdminIds == null)
            {
                throw new Exception("DiscordAdminIds array invaid or missing in appsettings json file.");
            }
            _discordAdminIds = DiscordAdminIds;
        }

        /**
         * 
         * Functions Related To Recieve and Process The Messages
         *
        **/
        private async Task MessageReceived(SocketMessage message)
        {
            // we wait for the current message being processed before moving to next one to avoid mixing of responses or duplicates
            await _semaphore.WaitAsync();

            try
            {
                if (message.Channel.Id != _generalChatChannel)
                    return;

                if (message.Author.Id == _discordClient.CurrentUser.Id)
                    return;

                if (message.Content == "?iqra cost")
                {
                    if (!_discordAdminIds.Contains(message.Author.Id)) return;

                    await message.Channel.SendMessageAsync($"Iqra's OpenAI API Cost so Far: {_currentChatApiCost}");
                    return;
                }

                ConsoleWriteLog(
                    $"\nProcessing Message (ID {message.Id}):\n{(message.Content.Length > 50 ? message.Content.Substring(0, 50) : message.Content)}...",
                    ConsoleColor.Cyan
                );

                // Get last X messages as memory for Iqra
                var previousIMessages = await GetLastXDiscordChannelMessages(message.Channel, _previousMessagesContextLimit);

                // Check if Iqra is mentioned in current message or indirectly within the previous on-goign conversation
                if (!(await isIqraMentionedInMessage(message, previousIMessages)))
                {
                    ConsoleWriteLog(
                        "Skipped Message. Does not involve, mention or reference Iqra",
                        ConsoleColor.Yellow
                    );

                    Console.WriteLine("\n-\n");
                    return;
                }

                ConsoleWriteLog(
                        "Iqra relevant. Proceeding",
                        ConsoleColor.Blue
                    );

                IDisposable inTypingState = message.Channel.EnterTypingState();

                var (previousMessages, userIdsAndTheirUsernames) = await ConvertListMessageToJsonFormat(previousIMessages);

                var iqraApiResponse = await GenerateOpenAIChatResponse(previousMessages, message);

                // Enforce checking if gpt decided to respond literally the same thing as before.. then try to just lower down temperature hoping we get the right response
                if (_iqraLastMessageContent == iqraApiResponse.Result.Choices[0].Message.Content && !iqraApiResponse.Result.Choices[0].Message.Content.Contains("no_reply"))
                {
                    iqraApiResponse = await GenerateOpenAIChatResponse(previousMessages, message, 0.6);
                    if (_iqraLastMessageContent == iqraApiResponse.Result.Choices[0].Message.Content)
                    {
                        ConsoleWriteLog(
                            "Skipped Message. Seems to be generating the same response as last time twice.",
                            ConsoleColor.Yellow
                        );

                        Console.WriteLine("\n-\n");
                        return;
                    }
                }

                // Process the response from openAIApi and respond back
                await ConvertAndSendMessageBack(message.Channel, iqraApiResponse, userIdsAndTheirUsernames);

                // Be Done with The Current Message Request
                inTypingState.Dispose();
                ConsoleWriteLog(
                    $"Finishing Processing Message (ID {message.Id})",
                    ConsoleColor.Green
                );
                Console.WriteLine("\n-\n");
            }
            finally
            {
                _semaphore.Release();
            }
        }
        private async Task<CostResult<ChatResult>?> GenerateOpenAIChatResponse(string previousMessages, IMessage currentMessage, double temperate = 0.6)
        {
            var results = await _openAiChatApi
                    .RequestWithUserMessage(previousMessages)
                    .AddSystemMessage(_iqraSystemContext + $"\nThe message you are supposed to (reply or not reply) to is the message (ID {currentMessage.Id}) sent by @{currentMessage.Author.Username}")
                    .WithModel((ChatModelType)_iqraChatModel)
                    .WithNumberOfChoicesPerPrompt(1)
                    .WithTemperature(temperate)
                    .SetMaxTokens(250)
                    .ExecuteAndCalculateCostAsync();

            _currentChatApiCost += results.CalculateCost();

            return results;
        }
        private async Task ConvertAndSendMessageBack(ISocketMessageChannel channel, CostResult<ChatResult>? results, Dictionary<ulong, string> userIdsAndUserNames)
        {
            try
            {
                JsonNode jsonReply = JsonNode.Parse(results.Result.Choices[0].Message.Content);

                try
                {
                    JsonArray rootArray = jsonReply.AsArray();
                    foreach (var message_reply in rootArray)
                    {
                        JsonObject messageOject = message_reply.AsObject();

                        int handleResult = await handleMessage(messageOject, channel, userIdsAndUserNames);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    JsonObject jsonReplyAsObject = jsonReply.AsObject();

                    int handleResult = await handleMessage(jsonReplyAsObject, channel, userIdsAndUserNames);
                }

            }
            catch (JsonException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(results.Result.Choices[0].Message.Content);
            }
        }
        private async Task<int> handleMessage(JsonObject messageOject, ISocketMessageChannel channel, Dictionary<ulong, string> userIdsAndUserNames)
        {
            if (!messageOject.ContainsKey("message_type")) return 0;

            string message_type = messageOject["message_type"].ToString();

            if (message_type == "no_reply")
            {
                if (messageOject.ContainsKey("reason"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"decided to not reply because: {messageOject["reason"].ToString()}");
                    Console.ResetColor();
                }

                return 200;
            }

            if (message_type == "reply_to_message")
            {
                string message_content = "";

                if (messageOject.ContainsKey("message_content"))
                {
                    message_content = messageOject["message_content"].ToString();
                }
                else if (messageOject.ContainsKey("message"))
                {
                    message_content = messageOject["message"].ToString();
                }
                else
                {
                    Console.WriteLine("Skipping reply: No message content");
                    return 1;
                }

                foreach (var (userId, userName) in userIdsAndUserNames)
                {
                    message_content = message_content.Replace($"@{userName}", $"<@{userId}>");
                }

                ulong replying_to_message_id = ulong.Parse(messageOject["replying_to_message_id"].ToString());
                IMessage replying_to_message = await channel.GetMessageAsync(replying_to_message_id);

                if (replying_to_message == null)
                {
                    return 4;
                }

                if (replying_to_message.Author.Id == _currentDiscordClientId)
                {
                    return 3;
                }

                MessageReference messageReference = new MessageReference(replying_to_message_id, failIfNotExists: true);

                await channel.SendMessageAsync(message_content, messageReference: messageReference);

                _iqraLastMessageContent = message_content;

                return 200;
            }

            if (message_type == "react_to_message")
            {
                Console.WriteLine("todo reaction");

                return 200;
            }

            return 2;
        }


        /**
         * 
         * Functions Related To Checking if Iqra is Relevant to the conversation
         *
        **/
        private async Task<bool> isIqraMentionedInMessage(IMessage message, IEnumerable<IMessage>? previousIMessages)
        {
            if (message.Type == MessageType.Reply)
            {
                if ((await message.Channel.GetMessageAsync(message.Reference.MessageId.Value)).Author.Id != _currentDiscordClientId)
                {
                    return (await isIqraMentionedDirectly(message, previousIMessages));
                }

                return true;
            }
            else if (message.Type == MessageType.Default)
            {
                return (await isIqraMentionedDirectly(message, previousIMessages));
            }

            return false;
        }
        private async Task<bool> isIqraMentionedDirectly(IMessage message, IEnumerable<IMessage>? previousIMessages)
        {
            if (!message.Content.Contains("Iqra")
                && !message.Content.Contains("iqra")
                && !message.Content.Contains($"<@{_currentDiscordClientId}>")
                && !message.Content.Contains("<@&1121904828986179639>")) // hardcoded role id for now, should make dynamic in future
            {
                var (previousMessages, userIdsAndTheirUsernames) = await ConvertListMessageToJsonFormat(previousIMessages.Take(_iqraRelevancyMessagesContextLimit), false);

                return (await isIqraMentionedIndirectly(previousMessages));
            }

            return true;
        }
        private async Task<bool> isIqraMentionedIndirectly(string previousMessages)
        {
            var results = await _openAiChatApi
                .RequestWithUserMessage(previousMessages)
                .AddSystemMessage(_iqraRelevancySystemContext)
                .WithModel((ChatModelType)_iqraRelevancyChatModel)
                .WithNumberOfChoicesPerPrompt(1)
                .WithTemperature(0.3)
                .SetMaxTokens(30)
                .ExecuteAndCalculateCostAsync();

            _currentChatApiCost += results.CalculateCost();

            try
            {
                JsonObject? result = (JsonObject?)JsonObject.Parse(results.Result.Choices[0].Message.Content);
                if (result.ContainsKey("result"))
                {
                    return result["result"].GetValue<bool>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("isIqraMentionedInMessage() Failed:\n" + ex.Message);
            }

            return false;
        }
        

        /**
         * 
         * Functions Related To Converting list of Messages to Format for OpenAI Chat To Input
         *
        **/
        private async Task<(string, Dictionary<ulong, string>)> ConvertListMessageToJsonFormat(IEnumerable<IMessage> messages, bool outputMessagesRepliedTo = true)
        {
            List<ulong>? messagesRepliedToByBot = null;
            if (outputMessagesRepliedTo)
            {
                messagesRepliedToByBot = new List<ulong>();
            }

            Dictionary<ulong, string> usernameAndTheirId = new Dictionary<ulong, string>();

            string finalResult = "";

            string messagesResult = "[\n\n";

            int messagesCount = messages.Count();

            // create a list to store the tasks
            List<Task<string>> tasks = new List<Task<string>>();

            for (int index = 0; index < messagesCount; index++)
            {
                var message = messages.ElementAt(index);

                if (message.Type != MessageType.Default && message.Type != MessageType.Reply) continue;

                if (!usernameAndTheirId.ContainsKey(message.Author.Id))
                {
                    if (message.Author.Id == _currentDiscordClientId)
                    {
                        usernameAndTheirId.Add(message.Author.Id, "Iqra");
                    }
                    else
                    {
                        usernameAndTheirId.Add(message.Author.Id, message.Author.Username);
                    }
                }

                // start the task and add it to the list of tasks
                tasks.Add(ConvertMessageToJsonData(message, (index + 1)));

                if (outputMessagesRepliedTo)
                {
                    if (message.Type == MessageType.Reply)
                    {
                        if (message.Author.Id == _currentDiscordClientId)
                        {
                            if (@messagesRepliedToByBot.Contains(message.Reference.MessageId.Value))
                            {
                                messagesRepliedToByBot.Add(message.Reference.MessageId.Value);
                            }
                        }
                    }
                }
            }

            // wait for all tasks to complete and then add the results to the "messagesResult" string in the correct order
            var results = await Task.WhenAll(tasks);
            for (int i = 0; i < results.Length; i++)
            {
                messagesResult += results[i];
                if (i + 1 < results.Length)
                {
                    messagesResult += "---------------------\n\n";
                }
            }

            messagesResult += "]";

            foreach (var (userId, userName) in usernameAndTheirId)
            {
                messagesResult = messagesResult.Replace($"<@{userId}>", $"@{userName}");
            }

            // Construct the Final JSON
            finalResult += "\"List of Text Messages Iqra must used as Context to think of a response\": ";
            finalResult += messagesResult;
            if (outputMessagesRepliedTo)
            {
                finalResult += ",";
                finalResult += "\n\n\"Ids of Message from the Messages List Above Iqra has replied to in the Past already\": ";
                finalResult += "[\n";
                finalResult += string.Join(",\n", messagesRepliedToByBot);
                finalResult += "\n]";
            }

            return (finalResult, usernameAndTheirId);
        }
        private async Task<string> ConvertMessageToJsonData(IMessage message, int? position = null)
        {
            string result = "";

            if (message.Type == MessageType.Reply)
            {
                IMessage referencedMessage = (await message.Channel.GetMessageAsync(message.Reference.MessageId.Value));

                if (message.Author.Id == _currentDiscordClientId)
                {
                    result += $"{(position != null ? $"{position}: " : "")}@Iqra Replied to @{referencedMessage.Author.Username}'s Message (ID {message.Reference.MessageId}) at {message.Timestamp.ToString("dddd, dd MMMM yyyy h:mm tt")}:\n";
                }
                else
                {
                    if (referencedMessage.Author.Id == _currentDiscordClientId)
                    {
                        result += $"{(position != null ? $"{position}: " : "")}@{message.Author.Username} sent a Message (ID {message.Id}) while replying to Iqra's Message (ID {message.Reference.MessageId.Value}) at {message.Timestamp.ToString("dddd, dd MMMM yyyy h:mm tt")}:\n";
                    }
                    else
                    {
                        result += $"{(position != null ? $"{position}: " : "")}@{message.Author.Username} sent a Message (ID {message.Id}) while replying to @{referencedMessage.Author.Username}'s Message (ID {message.Reference.MessageId.Value}) at {message.Timestamp.ToString("dddd, dd MMMM yyyy h:mm tt")}:\n";
                    }
                }             
            }
            else
            {
                result += $"{(position != null ? $"{position}: " : "")}@{message.Author.Username} Sent a Message (ID {message.Id}) at {message.Timestamp.ToString("dddd, dd MMMM yyyy h:mm tt")}:\n";
            }

            if (position != null && position == 1)
            {
                result += "*Below is the message Iqra is the most recent message Iqra is supposed to reply to or choose not to reply to:*\nMessage: ";
            }

            result += $"{message.Content}\n\n";

            return result;
        }


        /**
         * 
         * Other Functions
         *
        **/
        private void ConsoleWriteLog(string message, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }
        private async Task<IEnumerable<IMessage>> GetLastXDiscordChannelMessages(ISocketMessageChannel channel, int x)
        {
            return await channel.GetMessagesAsync(x).FlattenAsync();
        }
    }
}
