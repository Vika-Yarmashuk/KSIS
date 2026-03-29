using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class ChatServer
{
    private TcpListener server;
    private List<ClientHandler> clients = new List<ClientHandler>();
    private bool isRunning;

    public void Start(string ipAddress, int port)
    {
        try
        {
            if (!IsPortAvailable(port))
            {
                Console.WriteLine($"Порт {port} уже используется! Попробуйте другой порт.");
                return;
            }

            IPAddress localAddr = IPAddress.Parse(ipAddress);
            server = new TcpListener(localAddr, port);
            server.Start();
            isRunning = true;

            Console.WriteLine($"Сервер запущен на {ipAddress}:{port}");
            Console.WriteLine("Ожидание подключения клиентов...");
            Console.WriteLine("Для остановки сервера введите /q");

            Thread acceptThread = new Thread(AcceptClients);
            acceptThread.Start();

            Thread commandThread = new Thread(ReadCommands);
            commandThread.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка запуска сервера: {ex.Message}");
        }
    }

    private bool IsPortAvailable(int port)
    {
        try
        {
            TcpListener tempListener = new TcpListener(IPAddress.Loopback, port);
            tempListener.Start();
            tempListener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void AcceptClients()
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = server.AcceptTcpClient();
                ClientHandler handler = new ClientHandler(client, this);
                lock (clients)
                {
                    clients.Add(handler);
                }
                Thread clientThread = new Thread(handler.HandleClient);
                clientThread.Start();
            }
            catch (Exception ex)
            {
                if (isRunning)
                    Console.WriteLine($"Ошибка при принятии клиента: {ex.Message}");
            }
        }
    }

    private void ReadCommands()
    {
        while (isRunning)
        {
            string input = Console.ReadLine();
            if (input == "/q")
            {
                Stop();
                break;
            }
        }
    }

    public void BroadcastMessage(string message, ClientHandler sender)
    {
        lock (clients)
        {
            foreach (var client in clients)
            {
                if (client != sender)
                {
                    client.SendMessage(message);
                }
            }
        }
    }

    public void RemoveClient(ClientHandler client)
    {
        lock (clients)
        {
            clients.Remove(client);
        }
        BroadcastMessage($"*** {client.ClientName} покинул чат ***", null);
        Console.WriteLine($"{client.ClientName} ({client.ClientIP}) отключился от чата");
    }

    public void Stop()
    {
        isRunning = false;
        lock (clients)
        {
            foreach (var client in clients)
            {
                client.Stop();
            }
            clients.Clear();
        }
        server?.Stop();
        Console.WriteLine("Сервер остановлен");
        Environment.Exit(0);
    }
}

class ClientHandler
{
    private TcpClient client;
    private NetworkStream stream;
    private ChatServer server;
    public string ClientName { get; private set; }
    public string ClientIP { get; private set; }
    private bool isConnected;

    public ClientHandler(TcpClient tcpClient, ChatServer chatServer)
    {
        client = tcpClient;
        server = chatServer;
        stream = client.GetStream();
        ClientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
        isConnected = true;
    }

    public void HandleClient()
    {
        try
        {
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            ClientName = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            Console.WriteLine($"{ClientName} ({ClientIP}) подключился к чату");
            server.BroadcastMessage($"*** {ClientName} присоединился к чату ***", this);

            while (isConnected)
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                       
                        if (message == "/q")
                        {
                            break;
                        }

                        string formattedMessage = $"{ClientName}: {message}";
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {formattedMessage}");
                        server.BroadcastMessage(formattedMessage, this);
                    }
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (isConnected)
                Console.WriteLine($"Ошибка при обработке клиента {ClientName}: {ex.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    public void SendMessage(string message)
    {
        try
        {
            if (isConnected && stream != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                stream.Write(data, 0, data.Length);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки сообщения клиенту {ClientName}: {ex.Message}");
        }
    }

    private void Disconnect()
    {
        isConnected = false;
        server.RemoveClient(this);
        stream?.Close();
        client?.Close();
    }

    public void Stop()
    {
        isConnected = false;
        stream?.Close();
        client?.Close();
    }
}

class Program
{
    static void Main(string[] args)
    {
        Console.Title = "Чат Сервер";
        Console.WriteLine("=== Чат Сервер ===");

        Console.Write("Введите IP-адрес для запуска сервера: ");
        string ipAddress = Console.ReadLine();

        Console.Write("Введите порт для запуска сервера: ");
        if (int.TryParse(Console.ReadLine(), out int port))
        {
            ChatServer server = new ChatServer();
            server.Start(ipAddress, port);
        }
        else
        {
            Console.WriteLine("Неверный порт!");
        }
    }
}