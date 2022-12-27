﻿using System.Net;
using System.Net.Sockets;

namespace Messaging;

public static class Server
{
    public delegate void OnTick();

    private static TcpListener TcpListener;
    private static Dictionary<int, MessageStream> Clients;
    private static Dictionary<Message.MessageType, MessageStream.MessageHandler> MessageHandlers;
    
    private static int TickRate;

    private static MessageStream.OnDisconnect OnDisconnectCallback;
    private static OnTick OnTickCallback;
    
    public static void StartServer(string ip, Dictionary<Message.MessageType, 
        MessageStream.MessageHandler> messageHandlers, int tickRate, OnTick onTick, MessageStream.OnDisconnect onDisconnect)
    {
        MessageHandlers = messageHandlers;
        Clients = new Dictionary<int, MessageStream>();
        TickRate = tickRate;

        OnDisconnectCallback = onDisconnect;
        OnTickCallback = onTick;
            
        TcpListener = new TcpListener(IPAddress.Parse(ip), 8052);
        TcpListener.Start();
        TcpListener.BeginAcceptTcpClient(TcpConnectCallback, null);

        Tick();
        
        SpinWait.SpinUntil(() => false);
    }

    private static void Tick()
    {
        Task.Delay(1000 / TickRate).ContinueWith(_ => Tick());

        OnTickCallback();
    }
    
    private static void TcpConnectCallback(IAsyncResult result)
    {
        TcpClient client = TcpListener.EndAcceptTcpClient(result); // Finish accepting client
        TcpListener.BeginAcceptTcpClient(TcpConnectCallback, null); // Begin accepting new clients
        Console.WriteLine($"Connection from: {client.Client.RemoteEndPoint}...");

        int newClientId = Clients.Count;
        Clients.Add(newClientId, new MessageStream(client, newClientId, MessageHandlers, OnDisconnect));
        Clients[newClientId].StartReading();

        InitializeData initData = new()
        {
            Id = newClientId
        };
            
        Clients[newClientId].SendMessage(Message.MessageType.Initialize, initData);
    }

    private static void OnDisconnect(int id)
    {
        Clients.Remove(id);
        OnDisconnectCallback(id);
    }

    public static void SendMessage(int id, Message.MessageType type, Data data)
    {
        if (!Clients.ContainsKey(id)) return;
        Clients[id].SendMessage(type, data);
    }

    public static void SendMessageToAll(Message.MessageType type, Data data)
    {
        foreach (KeyValuePair<int, MessageStream> client in Clients)
        {
            SendMessage(client.Key, type, data);
        }
    }

    public static void SendMessageToAllExcluding(int excludedId, Message.MessageType type, Data data)
    {
        foreach (KeyValuePair<int, MessageStream> client in Clients)
        {
            if (client.Key == excludedId) continue;
            SendMessage(client.Key, type, data);
        }
    }
}