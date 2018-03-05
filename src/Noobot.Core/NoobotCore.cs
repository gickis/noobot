using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Timer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Noobot.Core.Configuration;
using Noobot.Core.Extensions;
using Noobot.Core.MessagingPipeline.Middleware;
using Noobot.Core.MessagingPipeline.Request;
using Noobot.Core.MessagingPipeline.Request.Extensions;
using Noobot.Core.MessagingPipeline.Response;
using SlackConnector;
using SlackConnector.Models;

namespace Noobot.Core
{
    internal class NoobotCore : INoobotCore, IHostedService
    {
        private static readonly TimerOptions ResponseOptions = new TimerOptions
        {
            Name = "Response",
            DurationUnit = TimeUnit.Milliseconds,
            MeasurementUnit = Unit.Requests,
            RateUnit = TimeUnit.Seconds
        };

        private readonly NoobotOptions _options;
        private readonly ILogger<NoobotCore> _logger;
        private readonly IMetrics _metrics;

        private ISlackConnection _connection;

        public NoobotCore(NoobotOptions options, ILogger<NoobotCore> logger, IMetrics metrics)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        }

        public bool? IsConnected => _connection?.IsConnected;

        public async Task ConnectAsync()
        {
            string slackKey = _options.ApiKey;

            var connector = new SlackConnector.SlackConnector();
            _connection = await connector.Connect(slackKey);
            _connection.OnMessageReceived += MessageReceived;
            _connection.OnDisconnect += OnDisconnect;
            _connection.OnReconnecting += OnReconnecting;
            _connection.OnReconnect += OnReconnect;

            _logger.LogInformation("Connected!");
            _logger.LogInformation($"Bots Name: {_connection.Self.Name}");
            _logger.LogInformation($"Team Name: {_connection.Team.Name}");
        }

        private Task OnReconnect()
        {
            _logger.LogInformation("Connection Restored!");
            return Task.CompletedTask;
        }

        private Task OnReconnecting()
        {
            _logger.LogInformation("Attempting to reconnect to Slack...");
            return Task.CompletedTask;
        }

        private bool _isDisconnecting;
        public async Task DisconnectAsync()
        {
            _isDisconnecting = true;

            if (_connection != null && _connection.IsConnected)
            {
                await _connection.Close();
            }
        }

        private void OnDisconnect()
        {
            if (_isDisconnecting)
            {
                _logger.LogInformation("Disconnected.");
            }
            else
            {
                _logger.LogInformation("Disconnected from server, attempting to reconnect...");
                Reconnect();
            }
        }

        internal void Reconnect()
        {
            _logger.LogInformation("Reconnecting...");
            if (_connection != null)
            {
                _connection.OnMessageReceived -= MessageReceived;
                _connection.OnDisconnect -= OnDisconnect;
                _connection = null;
            }

            _isDisconnecting = false;

            ConnectAsync()
                .ContinueWith(task =>
                {
                    if (task.IsCompleted && !task.IsCanceled && !task.IsFaulted)
                    {
                        _logger.LogInformation("Connection restored.");
                    }
                    else
                    {
                        _logger.LogInformation($"Error while reconnecting: {task.Exception}");
                    }
                });
        }

        public async Task MessageReceived(SlackMessage message)
        {
            using (_metrics.Measure.Timer.Time(ResponseOptions))
            {
                _logger.LogInformation($"[Message found from '{message.User.Name}']");

                IMiddleware pipeline = null;//_container.GetMiddlewarePipeline();
                var incomingMessage = new IncomingMessage
                {
                    RawText = message.Text,
                    FullText = message.Text,
                    UserId = message.User.Id,
                    Username = GetUsername(message),
                    UserEmail = message.User.Email,
                    Channel = message.ChatHub.Id,
                    ChannelType = message.ChatHub.Type == SlackChatHubType.DM ? ResponseType.DirectMessage : ResponseType.Channel,
                    UserChannel = await GetUserChannel(message),
                    BotName = _connection.Self.Name,
                    BotId = _connection.Self.Id,
                    BotIsMentioned = message.MentionsBot
                };

                incomingMessage.TargetedText = incomingMessage.GetTargetedText();

                try
                {
                    foreach (ResponseMessage responseMessage in pipeline.Invoke(incomingMessage))
                    {
                        await SendMessage(responseMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"ERROR WHILE PROCESSING MESSAGE: {ex}");
                }
            }
        }

        public async Task Ping()
        {
            await _connection.Ping();
        }

        public async Task SendMessage(ResponseMessage responseMessage)
        {
            SlackChatHub chatHub = await GetChatHub(responseMessage);

            if (chatHub != null)
            {
                if (responseMessage is TypingIndicatorMessage)
                {
                    _logger.LogInformation($"Indicating typing on channel '{chatHub.Name}'");
                    await _connection.IndicateTyping(chatHub);
                }
                else
                {
                    var botMessage = new BotMessage
                    {
                        ChatHub = chatHub,
                        Text = responseMessage.Text,
                        Attachments = GetAttachments(responseMessage.Attachments)
                    };

                    string textTrimmed = botMessage.Text.Length > 50 ? botMessage.Text.Substring(0, 50) + "..." : botMessage.Text;
                    _logger.LogInformation($"Sending message '{textTrimmed}'");
                    await _connection.Say(botMessage);
                }
            }
            else
            {
                _logger.LogError($"Unable to find channel for message '{responseMessage.Text}'. Message not sent");
            }
        }

        private IList<SlackAttachment> GetAttachments(List<Attachment> attachments)
        {
            var slackAttachments = new List<SlackAttachment>();

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    slackAttachments.Add(new SlackAttachment
                    {
                        Text = attachment.Text,
                        Title = attachment.Title,
                        Fallback = attachment.Fallback,
                        ImageUrl = attachment.ImageUrl,
                        ThumbUrl = attachment.ThumbUrl,
                        AuthorName = attachment.AuthorName,
                        ColorHex = attachment.Color,
                        Fields = GetAttachmentFields(attachment)
                    });
                }
            }

            return slackAttachments;
        }

        private IList<SlackAttachmentField> GetAttachmentFields(Attachment attachment)
        {
            var attachmentFields = new List<SlackAttachmentField>();

            if (attachment?.AttachmentFields != null)
            {
                foreach (var attachmentField in attachment.AttachmentFields)
                {
                    attachmentFields.Add(new SlackAttachmentField
                    {
                        Title = attachmentField.Title,
                        Value = attachmentField.Value,
                        IsShort = attachmentField.IsShort
                    });
                }
            }

            return attachmentFields;
        }

        public string GetUserIdForUsername(string username)
        {
            var user = _connection.UserCache.FirstOrDefault(x => x.Value.Name.Equals(username, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(user.Key) ? string.Empty : user.Key;
        }

        public string GetUserIdForUserEmail(string email)
        {
            var user = _connection.UserCache.WithEmailSet().FindByEmail(email);
            return user?.Id ?? string.Empty;
        }

        public string GetChannelId(string channelName)
        {
            var channel = _connection.ConnectedChannels().FirstOrDefault(x => x.Name.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            return channel != null ? channel.Id : string.Empty;
        }

        public Dictionary<string, string> ListChannels()
        {
            return _connection.ConnectedHubs.Values.ToDictionary(channel => channel.Id, channel => channel.Name);
        }

        public string GetBotUserName()
        {
            return _connection?.Self.Name;
        }

        private string GetUsername(SlackMessage message)
        {
            return _connection.UserCache.ContainsKey(message.User.Id) ? _connection.UserCache[message.User.Id].Name : string.Empty;
        }

        private async Task<string> GetUserChannel(SlackMessage message)
        {
            return (await GetUserChatHub(message.User.Id, joinChannel: false) ?? new SlackChatHub()).Id;
        }

        private async Task<SlackChatHub> GetChatHub(ResponseMessage responseMessage)
        {
            SlackChatHub chatHub = null;

            if (responseMessage.ResponseType == ResponseType.Channel)
            {
                chatHub = new SlackChatHub { Id = responseMessage.Channel };
            }
            else if (responseMessage.ResponseType == ResponseType.DirectMessage)
            {
                if (string.IsNullOrEmpty(responseMessage.Channel))
                {
                    chatHub = await GetUserChatHub(responseMessage.UserId);
                }
                else
                {
                    chatHub = new SlackChatHub { Id = responseMessage.Channel };
                }
            }

            return chatHub;
        }

        private async Task<SlackChatHub> GetUserChatHub(string userId, bool joinChannel = true)
        {
            SlackChatHub chatHub = null;

            if (_connection.UserCache.ContainsKey(userId))
            {
                string username = "@" + _connection.UserCache[userId].Name;
                chatHub = _connection.ConnectedDMs().FirstOrDefault(x => x.Name.Equals(username, StringComparison.OrdinalIgnoreCase));
            }

            if (chatHub == null && joinChannel)
            {
                chatHub = await _connection.JoinDirectMessageChannel(userId);
            }

            return chatHub;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return ConnectAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return DisconnectAsync();
        }
    }
}