using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class HttpProxyServer
{
    private readonly TcpListener _listener;
    private readonly string _proxyIp;
    private readonly int _proxyPort;
    private static readonly object _consoleLock = new object();

    public HttpProxyServer(string proxyIp, int proxyPort)
    {
        _proxyIp = proxyIp;
        _proxyPort = proxyPort;
        _listener = new TcpListener(IPAddress.Parse(proxyIp), proxyPort);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        lock (_consoleLock)
        {
            Console.WriteLine($"Прокси-сервер запущен на {_proxyIp}:{_proxyPort}");
            Console.WriteLine("Ожидание подключений...");
            Console.WriteLine(new string('-', 60));
        }

        while (true)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (Exception ex)
            {
                lock (_consoleLock)
                {
                    Console.WriteLine($"Ошибка при принятии подключения: {ex.Message}");
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            var clientStream = client.GetStream();
            var buffer = new byte[8192];

            try
            {
                int bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) return;

                string requestString = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                var requestLines = requestString.Split(new[] { "\r\n" }, StringSplitOptions.None);

                if (requestLines.Length == 0) return;

                var firstLine = requestLines[0].Split(' ');
                if (firstLine.Length < 2) return;

                string method = firstLine[0];
                string url = firstLine[1];
                string version = firstLine.Length > 2 ? firstLine[2] : "HTTP/1.1";

                string host;
                string path;
                int port = 80;

                if (url.StartsWith("http://"))
                {
                    var uri = new Uri(url);
                    host = uri.Host;
                    port = uri.Port;
                    path = uri.PathAndQuery;
                }
                else
                {
                    path = url;
                    host = null;

                    foreach (var line in requestLines)
                    {
                        if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                        {
                            host = line.Substring(6).Trim();
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(host))
                    {
                        lock (_consoleLock)
                        {
                            Console.WriteLine("Ошибка: не удалось определить хост");
                        }
                        return;
                    }

                    var hostParts = host.Split(':');
                    if (hostParts.Length > 1)
                    {
                        host = hostParts[0];
                        port = int.Parse(hostParts[1]);
                    }
                }

                // Формируем модифицированный запрос
                string modifiedRequest = $"{method} {path} {version}\r\n";

                for (int i = 1; i < requestLines.Length; i++)
                {
                    string line = requestLines[i];
                    if (!string.IsNullOrEmpty(line) &&
                        !line.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase))
                    {
                        modifiedRequest += line + "\r\n";
                    }
                }

                // Добавляем keep-alive 
                if (!modifiedRequest.Contains("Connection:"))
                {
                    modifiedRequest += "Connection: keep-alive\r\n";
                }
                modifiedRequest += "\r\n";

                using (var targetClient = new TcpClient())
                {
                    await targetClient.ConnectAsync(host, port);
                    var targetStream = targetClient.GetStream();

                    byte[] requestBytes = Encoding.ASCII.GetBytes(modifiedRequest);
                    await targetStream.WriteAsync(requestBytes, 0, requestBytes.Length);

                    if (bytesRead > requestString.Length)
                    {
                        await targetStream.WriteAsync(buffer, requestString.Length, bytesRead - requestString.Length);
                    }

                    var responseBuffer = new byte[8192];
                    int responseCode = 0;
                    bool headersRead = false;
                    string responseStatus = "";

                    while (true)
                    {
                        bytesRead = await targetStream.ReadAsync(responseBuffer, 0, responseBuffer.Length);
                        if (bytesRead == 0) break;

                        if (!headersRead)
                        {
                            string responseString = Encoding.ASCII.GetString(responseBuffer, 0, bytesRead);
                            var responseLines = responseString.Split(new[] { "\r\n" }, StringSplitOptions.None);
                            if (responseLines.Length > 0)
                            {
                                var statusLine = responseLines[0].Split(' ');
                                if (statusLine.Length >= 3 && int.TryParse(statusLine[1], out responseCode))
                                {
                                    responseStatus = statusLine[2];
                                    headersRead = true;

                                    string logUrl = url.StartsWith("http") ? url : $"http://{host}:{port}{path}";
                                    lock (_consoleLock)
                                    {
                                        Console.WriteLine($"{logUrl} - {responseCode} {responseStatus}");
                                    }
                                }
                            }
                        }

                        await clientStream.WriteAsync(responseBuffer, 0, bytesRead);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_consoleLock)
                {
                    Console.WriteLine($"Ошибка при обработке запроса: {ex.Message}");
                }
            }
        }
    }

    public void Stop()
    {
        _listener.Stop();
    }

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Настройка HTTP-прокси ===\n");

        
        Console.Write("Введите IP-адрес для прокси (по умолчанию 127.0.0.2): ");
        string proxyIpInput = Console.ReadLine();
        string proxyIp = string.IsNullOrWhiteSpace(proxyIpInput) ? "127.0.0.2" : proxyIpInput;

     
        Console.Write("Введите порт (по умолчанию 8888): ");
        string portInput = Console.ReadLine();
        int proxyPort = string.IsNullOrWhiteSpace(portInput) ? 8888 : int.Parse(portInput);

        // Проверка доступен ли IP 
        bool ipAvailable = false;
        try
        {
            var testListener = new TcpListener(IPAddress.Parse(proxyIp), 0);
            testListener.Start();
            testListener.Stop();
            ipAvailable = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n IP-адрес {proxyIp} недоступен: {ex.Message}");
          
        }

        if (ipAvailable)
        {
            var proxy = new HttpProxyServer(proxyIp, proxyPort);

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                lock (new object())
                {
                    Console.WriteLine("\nОстановка прокси-сервера...");
                }
                proxy.Stop();
                Environment.Exit(0);
            };

            Console.WriteLine("\nПрокси запускается...\n");
            await proxy.StartAsync();
        }
    }
}