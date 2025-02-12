﻿using System.Diagnostics;
using System.Net.Sockets;

namespace Messaging;

public class MessageStream
{
    public delegate void MessageHandler(int fromId, IData data);

    public delegate void OnDisconnect(int id);

    public const int DataBufferSize = 1024;
    private const long TimeoutMs = 30000;

    private readonly byte[] incomingDataBuffer = new byte[DataBufferSize];
    private readonly byte[] storedData = new byte[DataBufferSize];
    private int storedDataCount;

    private bool hasNextMessageLength;
    public int Id;
    private readonly Dictionary<Message.MessageType, MessageHandler> messageHandlers;
    private int nextMessageLength;

    private readonly OnDisconnect onDisconnect;
    private bool reading;

    private readonly NetworkStream stream;
    private readonly Thread streamThread;

    public MessageStream(TcpClient socket, int id, Dictionary<Message.MessageType, MessageHandler> messageHandlers,
        OnDisconnect onDisconnect)
    {
        Id = id;
        this.messageHandlers = messageHandlers;
        this.onDisconnect = onDisconnect;
        stream = socket.GetStream();
        streamThread = new Thread(ReceiveProc);
    }

    public void StartReading()
    {
        reading = true;
        streamThread.Start();
    }

    public void StopReading()
    {
        reading = false;
    }

    private void ReceiveProc()
    {
        try
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (reading)
            {
                if (stopwatch.ElapsedMilliseconds > TimeoutMs) break;
                if (!stream.DataAvailable) continue;

                int incomingDataLength = stream.Read(incomingDataBuffer);
                if (incomingDataLength != 0) Read(incomingDataLength);

                stopwatch.Restart();
            }

            EndReceive();
        }
        catch
        {
            EndReceive();
        }
    }

    private void EndReceive()
    {
        stream.Dispose();
        Console.WriteLine($"Stream closed with id of {Id}!");
        onDisconnect(Id);
    }

    public void SendMessage(Message.MessageType type, IData data)
    {
        try
        {
            // Get a stream object for writing. 			
            if (!stream.CanWrite) return;

            Message message = new(type, data);

            byte[] clientMessageAsByteArray = message.ToByteArray();

            // Write byte array to socketConnection stream.                 
            stream.Write(clientMessageAsByteArray, 0, clientMessageAsByteArray.Length);
        }
        catch (SocketException socketException)
        {
            Console.WriteLine("Socket exception: " + socketException);
        }
    }

    public void Read(int incomingDataLength)
    {
        // Read incoming stream into byte array. 						
        Array.Copy(incomingDataBuffer, 0, storedData, storedDataCount, incomingDataLength);
        storedDataCount += incomingDataLength;

        while (storedDataCount > 0)
            if (hasNextMessageLength)
            {
                if (storedDataCount >= nextMessageLength) // The whole message has been received
                {
                    HandleData(storedData, nextMessageLength);

                    storedDataCount -= nextMessageLength;
                    Array.Copy(storedData, nextMessageLength, storedData, 0, storedDataCount);
                    hasNextMessageLength = false;
                    nextMessageLength = 0;
                }
                else
                {
                    break; // Wait for more data
                }
            }
            else
            {
                if (storedDataCount >= sizeof(int)) // There is enough incoming data to read the next message's length
                {
                    nextMessageLength = BitConverter.ToInt32(storedData.ToArray(), 0);
                    hasNextMessageLength = true;
                }
                else
                {
                    break; // Wait for more data
                }
            }
    }

    public void HandleData(byte[] dataBuffer, int length)
    {
        var messageType = (Message.MessageType)BitConverter.ToInt32(dataBuffer, sizeof(int));

        const int offset = 2 * sizeof(int); // Length of message length and type
        var data = (IData)ByteUtils.ByteArrayToObject(Message.ToDataType(messageType),
            new ReadOnlySpan<byte>(dataBuffer, offset, length - offset));

        if (!messageHandlers.TryGetValue(messageType, out MessageHandler handler)) return;
        handler(Id, data);
    }
}