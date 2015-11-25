using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Diagnostics;

namespace AdsbTranslator
{
    class Server
    {
        private int sbsPort;
        private int rawPort;
        private int receiveTimeout;
        private int aircraftTTL;
        private bool fixCRC;
        private TcpListener rawListener;
        private SbsClients sbsClient;
        private AdsbFormatConverter konwerter;
        private volatile bool _shouldWork;
        private string sSource = "ADSBTranslate";
        private string sLog = "Application";
        private string sEvent = "";

        public Server(){
            readSettings();
            _shouldWork = true;
            Thread ctThread = new Thread(startServers);
            ctThread.Start();
        }

        public void stopServer(){
            _shouldWork = false;
        }
        private void readSettings()
        {
            if (!EventLog.SourceExists(sSource))
                EventLog.CreateEventSource(sSource, sLog);
            try
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey("Software\\ADSBTranslator", true);
                sbsPort = (int)key.GetValue("SBSOutput");
                rawPort = (int)key.GetValue("RAWInput");
                aircraftTTL = (int)key.GetValue("AircraftTTL");
                receiveTimeout = (int)key.GetValue("ReceiveTimeout");
                fixCRC = Convert.ToBoolean(key.GetValue("FixCRC"));
                String dane = "sbsPort: " + sbsPort + ", rawPort: " + rawPort + ", aircraftTTL: " + aircraftTTL + ", receiveTimeout: " + receiveTimeout + ", fixCRC: " + fixCRC + "." ;
                EventLog.WriteEntry(sSource, "Odczytywanie danych konfiguracyjnych z rejestru powiodło się. " + dane);
                receiveTimeout *= 1000;
            }
            catch
            {
                sbsPort = 30003;
                rawPort = 30001;
                receiveTimeout = 120;
                aircraftTTL = 20;
                fixCRC = true;
                EventLog.WriteEntry(sSource, "Odczytywanie danych konfiguracyjnych z rejestru nie powidodło się. Stosowane są domyślne wartości.");
                receiveTimeout *= 1000;
            }
        }

        private void startServers()
        {
            rawListener = new TcpListener(IPAddress.Any, rawPort);
            rawListener.Start();

            sbsClient = new SbsClients(sbsPort);
            sbsClient.newThread();

            konwerter = new AdsbFormatConverter(fixCRC, aircraftTTL);

            while (_shouldWork) //we wait for a connection
            {
                TcpClient client = rawListener.AcceptTcpClient();
                client.ReceiveTimeout = receiveTimeout;
                NetworkStream ns = client.GetStream();
                bool tcpConnectionOK = true;

                while (tcpConnectionOK && client.Connected && _shouldWork)
                {
                    byte[] msg = new byte[1024];
                    try
                    {
                        ns.Read(msg, 0, msg.Length);
                        String result = System.Text.Encoding.Default.GetString(msg);
                        Regex r = new Regex(@"\*(.+?)\;");
                        MatchCollection mc = r.Matches(result);

                        foreach (Match match in mc)
                        {
                            foreach (Capture capture in match.Captures)
                            {
                                string tempSBS = konwerter.convertRaw(capture.Value);
                                if (!String.IsNullOrEmpty(tempSBS))
                                {
                                    sbsClient.broadcast(tempSBS);
                                }
                            }
                        }
                    }
                    catch
                    {
                        EventLog.WriteEntry(sSource, "Utracono połączenie na porcie RAW lub nastąpił Timeout");
                        tcpConnectionOK = false;
                    }
                }
                sbsClient.stopThread();

                TcpClient clientTemp = new TcpClient("127.0.0.1", sbsPort);
                Byte[] data = System.Text.Encoding.ASCII.GetBytes("0");
                NetworkStream stream = client.GetStream();
                stream.Write(data, 0, 1);
                stream.Close();
                clientTemp.Close();


                client.Close();
            }
        }
       
    }

    class SbsClients
    {
        private static Hashtable clientsList = new Hashtable();
        private int sbsPort;
        private volatile bool _shouldWork;

        public SbsClients(int port = 30003)
        {
            sbsPort = port;
        }
        public void newThread()
        {
            _shouldWork = true;
            Thread ctThread = new Thread(multipleClients);
            ctThread.Start();
        }

        public void stopThread()
        {
            _shouldWork = false;
        }

        private void multipleClients()
        {
            TcpListener serverSocket = new TcpListener(IPAddress.Any, sbsPort);
            TcpClient clientSocket = default(TcpClient);

            serverSocket.Start();

            while (_shouldWork)
            {
                clientSocket = serverSocket.AcceptTcpClient();

                string uid = Guid.NewGuid().ToString();
                clientsList.Add(uid, clientSocket);
            }

            clientSocket.Close();
            serverSocket.Stop();
        }

        public void broadcast(string msg)
        {
            ArrayList arr = new ArrayList();
            foreach (DictionaryEntry Item in clientsList)
            {
                try
                {
                    TcpClient broadcastSocket;
                    broadcastSocket = (TcpClient)Item.Value;
                    NetworkStream broadcastStream = broadcastSocket.GetStream();
                    Byte[] broadcastBytes = null;

                    broadcastBytes = Encoding.ASCII.GetBytes(msg + "\r\n");

                    broadcastStream.Write(broadcastBytes, 0, broadcastBytes.Length);

                    broadcastStream.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    arr.Add(Item.Key);
                }
            }

            for (int i = 0; i < arr.Count; ++i)
            {
                clientsList.Remove(arr[i]);
            }
        }
    }
}
