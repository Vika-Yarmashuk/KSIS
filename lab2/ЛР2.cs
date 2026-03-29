using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MyTraceroute
{
   
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct IPHeader
    {
        public byte verlen;        
        public byte tos;            
        public ushort length;       
        public ushort id;            
        public ushort offset;       
        public byte ttl;           
        public byte protocol;       
        public ushort checksum;       
        public uint src;             
        public uint dest;           
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ICMPHeader
    {
        public byte type;            
        public byte code;              
        public ushort checksum;        
        public ushort id;              
        public ushort seq;             
    }

 

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ICMPEchoPacket
    {
        public ICMPHeader header;
    
        public ICMPEchoPacket()
        {
            header = new ICMPHeader();
        }
    }

    class Program
    {
        // Константы для ICMP
        private const int ICMP_ECHO_REQUEST = 8;
        private const int ICMP_ECHO_REPLY = 0;
        private const int ICMP_TIME_EXCEEDED = 11;

        private static Dictionary<ushort, long> sentTimes = new Dictionary<ushort, long>();

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out long lpFrequency);

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

        private static long perfFreq;

       
        /// Подсчет контрольной суммы (RFC 1071)
      
        private static ushort CalculateChecksum(byte[] buffer, int offset, int length)
        {
            uint cksum = 0;
            int i = offset;
            int size = length;

            // Суммируем все 16-битные слова
            while (size > 1)
            {
                cksum += (uint)((buffer[i] << 8) | buffer[i + 1]);
                i += 2;
                size -= 2;
            }

            // Если остался нечетный байт
            if (size == 1)
            {
                cksum += (uint)(buffer[i] << 8);
            }

            // Добавляем переносы из старших 16 бит в младшие
            cksum = (cksum >> 16) + (cksum & 0xFFFF);
            cksum += (cksum >> 16);

            return (ushort)(~cksum & 0xFFFF);
        }

     
        /// Разрешение доменного имени в IP-адрес
      
        private static IPAddress ResolveHostname(string host)
        {
            // Сначала пробуем интерпретировать как IP-адрес
            if (IsValidIPv4Address(host))
            {
                if (IPAddress.TryParse(host, out IPAddress ipAddr))
                {
                    return ipAddr;
                }
            }
            // Если не IP, значит это доменное имя - запрашиваем DNS
            try
            {
                IPHostEntry remoteHost = Dns.GetHostEntry(host);
                foreach (IPAddress addr in remoteHost.AddressList)
                {
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return addr;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
      
        /// Проверка, является ли строка корректным IPv4 адресом в формате "x.x.x.x"
     
        private static bool IsValidIPv4Address(string host)
        {
            // Разделяем строку по точкам
            string[] octets = host.Split('.');

            // Должно быть ровно 4 октета
            if (octets.Length != 4)
            {
                return false;
            }

            // Проверяем каждый октет
            foreach (string octet in octets)
            {
                // Проверяем, что октет не пустой
                if (string.IsNullOrEmpty(octet))
                {
                    return false;
                }

                // Проверяем, что все символы - цифры
                foreach (char c in octet)
                {
                    if (!char.IsDigit(c))
                    {
                        return false;
                    }
                }

                // Проверяем, что октет не начинается с нуля (кроме самого нуля)
                if (octet.Length > 1 && octet[0] == '0')
                {
                    return false; // Запрещаем ведущие нули, например "01"
                }

                // Проверяем диапазон значений
                try
                {
                    int value = int.Parse(octet);
                    if (value < 0 || value > 255)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        /// Обратное DNS-разрешение (по IP узнаем имя хоста)
      
        private static string ReverseDNS(IPAddress ip)
        {
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(ip);
                return hostEntry.HostName;
            }
            catch
            {
                return ip.ToString();
            }
        }

     
        /// Преобразование структуры в байтовый массив
      
        private static byte[] StructureToBytes<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(structure, ptr, false);
                Marshal.Copy(ptr, bytes, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            return bytes;
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.SetIn(new System.IO.StreamReader(Console.OpenStandardInput(), Encoding.UTF8));

            // Инициализация высокоточного таймера
            QueryPerformanceFrequency(out perfFreq);

            // Парсинг аргументов командной строки
            if (args.Length < 1)
            {
                string exeName = Process.GetCurrentProcess().ProcessName;
                Console.WriteLine($"Использование: {exeName} <цель> [-d]");
                Console.WriteLine("  <цель>     - IP-адрес или доменное имя");
                Console.WriteLine("  -d         - Включить обратное DNS-разрешение");
                Console.WriteLine("\nВАЖНО: Запустите от имени Администратора!");
                return;
            }

            string target = args[0];
            bool reverseLookup = (args.Length >= 2 && args[1] == "-d");

            // Разрешение целевого узла
            IPAddress destIP = ResolveHostname(target);
            if (destIP == null)
            {
                Console.WriteLine("[-] Не удалось разрешить имя хоста: " + target);
                return;
            }

            string destIPStr = destIP.ToString();
            
            if (IPAddress.TryParse(target, out IPAddress a))
            {

                target = ReverseDNS(destIP);
            }

            
            try
            {
                // Создание RAW-сокета
                Socket icmpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);

                // Установка таймаута на прием (3 секунды)
                icmpSocket.ReceiveTimeout = 4000;

                // Подготовка адреса назначения
                EndPoint destAddr = new IPEndPoint(destIP, 0);

                // Параметры трассировки
                const int MAX_HOPS = 30;
                const int TRIES_PER_HOP = 3;
                ushort processID = (ushort)(Process.GetCurrentProcess().Id & 0xFFFF);

                Console.WriteLine($"\nТрассировка маршрута к {target} [{destIPStr}]");
                Console.WriteLine($"с максимальным числом прыжков {MAX_HOPS}:\n");

                bool reachedDestination = false;

                for (int ttl = 1; ttl <= MAX_HOPS && !reachedDestination; ttl++)
                {
                    // Установка TTL
                    icmpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);

                    // Массивы для хранения результатов трех попыток
                    uint[] rttTimes = new uint[TRIES_PER_HOP];
                    string[] responderIPs = new string[TRIES_PER_HOP];
                    bool[] hasResponse = new bool[TRIES_PER_HOP];

                    // 3 попытки для текущего TTL
                    for (int attempt = 0; attempt < TRIES_PER_HOP; attempt++)
                    {
                        try
                        {
                            // Формирование ICMP-пакета
                            ICMPEchoPacket packet = new ICMPEchoPacket();
                            packet.header.type = ICMP_ECHO_REQUEST;
                            packet.header.code = 0;
                            packet.header.id = processID;
                            packet.header.seq = (ushort)((ttl - 1) * TRIES_PER_HOP + attempt + 1);

                            // Запоминаем время отправки
                            long sendTime;
                            QueryPerformanceCounter(out sendTime);
                            sentTimes[packet.header.seq] = sendTime;
                            // Преобразуем структуру в байтовый массив
                            byte[] sendBuffer = StructureToBytes(packet);

                            // Подсчет контрольной суммы (пропускаем поле checksum)
                            ushort checksum = CalculateChecksum(sendBuffer, 0, sendBuffer.Length);

                            // Обновляем контрольную сумму в заголовке (байты 2 и 3)
                            sendBuffer[2] = (byte)((checksum >> 8) & 0xFF);
                            sendBuffer[3] = (byte)(checksum & 0xFF);

                            // Отправляем пакет
                            icmpSocket.SendTo(sendBuffer, destAddr);

                            // Ожидание ответа
                            byte[] recvBuffer = new byte[4096];
                            EndPoint senderAddr = new IPEndPoint(IPAddress.Any, 0);
                            int bytesReceived = icmpSocket.ReceiveFrom(recvBuffer, ref senderAddr);

                            if (bytesReceived < 20) continue; // Слишком маленький пакет

                            // Анализ IP-заголовка пакета
                            IPHeader ipHeader = new IPHeader();
                            GCHandle handle = GCHandle.Alloc(recvBuffer, GCHandleType.Pinned);
                            try
                            {
                                ipHeader = (IPHeader)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(IPHeader));
                            }
                            finally
                            {
                                handle.Free();
                            }

                            int ipHeaderLen = (ipHeader.verlen & 0x0F) * 4;

                            if (ipHeader.protocol != 1) continue; // Не ICMP - пропускаем

                            // Получаем ICMP-заголовок
                            if (bytesReceived < ipHeaderLen + Marshal.SizeOf<ICMPHeader>()) continue;

                            IntPtr icmpPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ICMPHeader>());
                            try
                            {
                                Marshal.Copy(recvBuffer, ipHeaderLen, icmpPtr, Marshal.SizeOf<ICMPHeader>());
                                ICMPHeader icmpHeader = (ICMPHeader)Marshal.PtrToStructure(icmpPtr, typeof(ICMPHeader));

                                // Получаем IP отправителя
                                string senderIP = ((IPEndPoint)senderAddr).Address.ToString();

                                // Обработка TIME EXCEEDED (тип 11)
                                if (icmpHeader.type == ICMP_TIME_EXCEEDED)
                                {
                                    // Смещение до внутреннего IP-заголовка
                                    int innerIPOffset = ipHeaderLen + 8;
                                    if (bytesReceived < innerIPOffset + 20) continue;

                                    // Получаем внутренний IP-заголовок
                                    IPHeader innerIP = new IPHeader();
                                    handle = GCHandle.Alloc(recvBuffer, GCHandleType.Pinned);
                                    try
                                    {
                                        innerIP = (IPHeader)Marshal.PtrToStructure(new IntPtr(handle.AddrOfPinnedObject().ToInt64() + innerIPOffset), typeof(IPHeader));
                                    }
                                    finally
                                    {
                                        handle.Free();
                                    }

                                    int innerIPHeaderLen = (innerIP.verlen & 0x0F) * 4;

                                    // Смещение до внутреннего ICMP-заголовка
                                    int innerICMPOffset = innerIPOffset + innerIPHeaderLen;
                                    if (bytesReceived < innerICMPOffset + Marshal.SizeOf<ICMPHeader>()) continue;

                                    // Получаем внутренний ICMP-заголовок
                                    IntPtr innerICMPPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ICMPHeader>());
                                    try
                                    {
                                        Marshal.Copy(recvBuffer, innerICMPOffset, innerICMPPtr, Marshal.SizeOf<ICMPHeader>());
                                        ICMPHeader innerICMP = (ICMPHeader)Marshal.PtrToStructure(innerICMPPtr, typeof(ICMPHeader));

                                        // Проверяем, что это наш пакет
                                        if (innerICMP.id != processID) continue;
                                        if (innerICMP.seq != packet.header.seq) continue;

                                        // Сохраняем IP ответившего маршрутизатора
                                        responderIPs[attempt] = senderIP;
                                        hasResponse[attempt] = true;

                                        // Извлекаем время отправки и вычисляем RTT

                                        if (sentTimes.TryGetValue(innerICMP.seq, out long sentTime))
                                        {
                                            QueryPerformanceCounter(out long currentTime);
                                            long diff = currentTime - sentTime;
                                            double ms = (double)diff * 1000.0 / perfFreq;
                                            rttTimes[attempt] = (uint)(ms + 0.5);

                                            sentTimes.Remove(innerICMP.seq); // Очистка
                                        }

                                     
                                    }
                                    finally
                                    {
                                        Marshal.FreeHGlobal(innerICMPPtr);
                                    }
                                }
                                // Обработка ECHO REPLY (тип 0)
                                else if (icmpHeader.type == ICMP_ECHO_REPLY)
                                {
                                    if (icmpHeader.id != processID) continue;
                                    if (icmpHeader.seq != packet.header.seq) continue;

                                    reachedDestination = true;

                                    // Сохраняем IP целевого узла
                                    responderIPs[attempt] = senderIP;
                                    hasResponse[attempt] = true;

                                    // Извлекаем время отправки и вычисляем RTT

                                    if (sentTimes.TryGetValue(icmpHeader.seq, out long sentTime))
                                    {
                                        QueryPerformanceCounter(out long currentTime);
                                        long diff = currentTime - sentTime;
                                        double ms = (double)diff * 1000.0 / perfFreq;
                                        rttTimes[attempt] = (uint)(ms + 0.5);

                                        sentTimes.Remove(icmpHeader.seq); // Очистка
                                    }

                                   
                                }
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(icmpPtr);
                            }
                        }
                        catch (SocketException)
                        {
                            // Таймаут или ошибка - пропускаем попытку
                        }

                        Thread.Sleep(200); // Небольшая задержка между попытками
                    }

                    // Вывод результата для текущего хопа
                    Console.Write($"  {ttl,2}  ");

                    // Выводим времена ответа для трех попыток
                    for (int i = 0; i < TRIES_PER_HOP; i++)
                    {
                        if (rttTimes[i] == 0)
                        {
                            Console.Write("   *   ");
                        }
                        else
                        {
                            Console.Write($" {rttTimes[i],2}ms ");
                        }
                    }

                    // Определяем IP для вывода (берем первый успешный ответ)
                    string displayIP = "";
                    for (int i = 0; i < TRIES_PER_HOP; i++)
                    {
                        if (hasResponse[i])
                        {
                            displayIP = responderIPs[i];
                            break;
                        }
                    }

                    // Выводим IP (с именем, если запрошено)
                    if (!string.IsNullOrEmpty(displayIP))
                    {
                        if (reverseLookup && IPAddress.TryParse(displayIP, out IPAddress addr))
                        {
                            string hostname = ReverseDNS(addr);
                            if (hostname != displayIP)
                            {
                                Console.Write($"  {hostname} [{displayIP}]");
                            }
                            else
                            {
                                Console.Write($"  {displayIP}");
                            }
                        }
                        else
                        {
                            Console.Write($"  {displayIP}");
                        }
                    }

                    Console.WriteLine();
                    Thread.Sleep(1000); // Задержка между хопами
                }

                Console.WriteLine("\nТрассировка завершена.");
                icmpSocket.Close();
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"\n[-] Ошибка сокета: {ex.Message}");
                Console.WriteLine("Запустите программу от имени Администратора!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[-] Ошибка: {ex.Message}");
            }
        }
    }
}
