using System;
using wipbot.Models;
using Zenject;

namespace wipbot.Interfaces
{
    internal interface IChatIntegration : IInitializable, IDisposable
    {
        void SendChatMessage(string message);
        event Action<ChatMessage> OnMessageReceived;
    }
}
