using System;

public static class ChatMessageBus
{
    private static event Action<ChatMessage> InternalMessageReceived;
    public static int SubscriberCount { get; private set; } = 0;

    public static void Subscribe(Action<ChatMessage> handler)
    {
        InternalMessageReceived += handler;
        SubscriberCount++;
    }

    public static void Unsubscribe(Action<ChatMessage> handler)
    {
        InternalMessageReceived -= handler;
        SubscriberCount = Math.Max(0, SubscriberCount - 1);
    }

    public static void Send(ChatMessage message)
    {
        InternalMessageReceived?.Invoke(message);
    }
}

public class ChatMessage
{
    public string Target { get; set; }
    public string Text { get; set; }
    public string Color { get; set; }
}
