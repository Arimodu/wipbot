using CP_SDK.Chat.Interfaces;
using CP_SDK.Chat.Services;
using SiraUtil.Logging;
using System;
using wipbot.Interfaces;
using wipbot.Models;
using Zenject;

namespace wipbot.Interop
{
    internal class ChatPlexSDKInterop : IChatIntegration
    {
        private readonly ChatServiceMultiplexer Multiplexer;
        [Inject] private readonly SiraLog Logger;
        public event Action<ChatMessage> OnMessageReceived;

        public ChatPlexSDKInterop()
        {
            CP_SDK.Chat.Service.Acquire();
            Multiplexer = CP_SDK.Chat.Service.Multiplexer;
        }

        public void Initialize()
        {
            Multiplexer.OnTextMessageReceived += Mux_OnTextMessageReceived;
            Logger.Info("ChatPlexSDK interop initialized");
        }

        private void Mux_OnTextMessageReceived(IChatService service, IChatMessage msg)
        {
            Logger.Debug($"ChatPlexSDK message received: {msg.Sender.UserName}: {msg.Message}");
            OnMessageReceived?.Invoke(new ChatMessage(msg.Sender.UserName, msg.Message.TrimEnd('?'), msg.Sender.IsBroadcaster, msg.Sender.IsModerator, msg.Sender.IsVip, msg.Sender.IsSubscriber));
        }

        public void SendChatMessage(string message) => Multiplexer.SendTextMessage(Multiplexer.Channels[0].Item2, message);

        public void Dispose()
        {
            Multiplexer.OnTextMessageReceived -= Mux_OnTextMessageReceived;
            CP_SDK.Chat.Service.Release();
            Logger.Info("ChatPlexSDK interop disposed");
        }
    }
}
