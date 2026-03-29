using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class ChatClient
{
    private TcpClient client;
    private NetworkStream stream;
    private string userName;
    private bool isConnected;

    public void Connect(string serverIp, int port, string name, IPAddress localIp)
    {
        try
        {
            IPEndPoint localEndPoint = new IPEndPoint(localIp, 0);
            client = new TcpClient(localEndPoint);
            Console.WriteLine($"Подключение к {serverIp}:{port}...");
            client.Connect(serverIp, port);
            stream = client.GetStream();
            userName = name;
            isConnected = true;

            // Отправляем имя серверу
            byte[] nameData = Encoding.UTF8.GetBytes(userName);
            stream.Write(nameData, 0, nameData.Length);

            Console.Clear();
            Console.WriteLine($"=== Добро пожаловать в чат, {userName}! ===");
            Console.WriteLine($"Подключено к серверу: {serverIp}:{port}");
            Console.WriteLine("Для выхода из чата введите /q");
            Console.WriteLine("==========================================\n");

            // Поток для получения сообщений
            Thread receiveThread = new Thread(ReceiveMessages);
            receiveThread.Start();

            // Поток для отправки сообщений
            Thread sendThread = new Thread(SendMessages);
            sendThread.Start();

            receiveThread.Join();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nОшибка подключения к серверу {serverIp}:{port}");
            Console.WriteLine($"Детали: {ex.Message}");
            Console.ReadLine();
        }
    }

    private void ReceiveMessages()
    {
        byte[] buffer = new byte[4096];
        while (isConnected)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine(message);
                }
                else
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                if (isConnected)
                    Console.WriteLine($"Ошибка получения сообщения: {ex.Message}");
                break;
            }
        }

        if (!isConnected)
        {
            Console.WriteLine("\nСоединение с сервером потеряно");
            
        }
    }

    private void SendMessages()
    {
        while (isConnected)
        {
            string message = Console.ReadLine();

            if (message == "/q")
            {
                // Отправляем команду выхода на сервер
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes("/q");
                    stream.Write(data, 0, data.Length);
                }
                catch { }
                Disconnect();
                break;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    stream.Write(data, 0, data.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка отправки сообщения: {ex.Message}");
                    Disconnect();
                    break;
                }
            }
        }
    }

    private void Disconnect()
    {
        isConnected = false;
        stream?.Close();
        client?.Close();
        Console.WriteLine("\nВы вышли из чата");
        Environment.Exit(0);
    }
}

class Program
{
    static void Main(string[] args)
    {
        Console.Title = "Чат Клиент";
        Console.WriteLine("=== Чат Клиент ===");


        Console.Write("Введите клиентский IP-адрес: ");
        if (!IPAddress.TryParse(Console.ReadLine(), out IPAddress localIp))
        {
            Console.WriteLine("Неверный адрес!");
            return;
        }

        Console.Write("Введите IP-адрес сервера: ");
        string serverIp = Console.ReadLine();

        Console.Write("Введите порт сервера: ");
        if (!int.TryParse(Console.ReadLine(), out int port))
        {
            Console.WriteLine("Неверный порт!");
            return;
        }

        Console.Write("Введите ваше имя: ");
        string userName = Console.ReadLine();

       

        Console.WriteLine();
        ChatClient client = new ChatClient();
        client.Connect(serverIp, port, userName, localIp);
    }
}