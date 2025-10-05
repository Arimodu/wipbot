using CatCore;
using CatCore.Services.Twitch.Interfaces;
using CatCore.Models.Twitch.IRC;
using System;
using wipbot.Interfaces;
using wipbot.Models;
using Zenject;
using SiraUtil.Logging;

namespace wipbot.Interop
{
    internal class CatCoreInterop : IChatIntegration
    {
        private readonly ITwitchService Service;
        [Inject] private readonly SiraLog Logger;
        public event Action<ChatMessage> OnMessageReceived;

        public CatCoreInterop()
        {
            CatCoreInstance inst = CatCoreInstance.Create();
            Service = inst.RunTwitchServices();
        }

        public void Initialize()
        {
            Logger.Info("CatCore interop initialized");
            Service.OnTextMessageReceived += Serv_OnTextMessageReceived;
        }

        private void Serv_OnTextMessageReceived(ITwitchService service, TwitchMessage msg)
        {
            Logger.Debug($"CatCore message received: {msg.Sender.UserName}: {msg.Message}");
            OnMessageReceived?.Invoke(
                new ChatMessage(
                    msg.Sender.UserName, 
                    msg.Message, 
                    msg.Sender.IsBroadcaster, 
                    msg.Sender.IsModerator, 
                    ((TwitchUser)msg.Sender).IsVip, 
                    ((TwitchUser)msg.Sender).IsSubscriber));
        }

        public void SendChatMessage(string message) => Service.DefaultChannel.SendMessage(message);

        public void Dispose()
        {
            Service.OnTextMessageReceived -= Serv_OnTextMessageReceived;
            Logger.Info("CatCore interop disposed");
        }
    }
}
