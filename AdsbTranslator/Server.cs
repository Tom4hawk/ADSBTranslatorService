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
        //Settings read from registry
        private int sbsPort;
        private int rawPort;
        private int receiveTimeout;//czy potrzebne
        private int aircraftTTL;
        private string sourceAddress;
        private bool fixCRC;

        //Custom Objects
        private TcpListener rawListener;
        private SbsClients sbsClient;
        private AdsbFormatConverter konwerter;

        //thread control
        private volatile bool _shouldWork;

        //Loggin information
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
        private void readSettings()//reads settings from windows registry
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
                sourceAddress = (string)key.GetValue("SourceIPAddress");
                fixCRC = Convert.ToBoolean(key.GetValue("FixCRC"));
                String dane = "sbsPort: " + sbsPort + ", Source IP Address: " + sourceAddress + ", rawPort: " + rawPort + ", aircraftTTL: " + aircraftTTL + ", receiveTimeout: " + receiveTimeout + ", fixCRC: " + fixCRC + ".";
                EventLog.WriteEntry(sSource, "Successfully read settings from windows registry. " + dane);//logging information about successful settings retrival
                receiveTimeout *= 1000;
            }
            catch
            {
                sbsPort = 30003;
                rawPort = 30001;
                sourceAddress = "127.0.0.1";
                receiveTimeout = 120;
                aircraftTTL = 20;
                fixCRC = true;
                String dane = "sbsPort: " + sbsPort + ", Source IP Address: " + sourceAddress + ", rawPort: " + rawPort + ", aircraftTTL: " + aircraftTTL + ", receiveTimeout: " + receiveTimeout + ", fixCRC: " + fixCRC + ".";
                EventLog.WriteEntry(sSource, "Reading settings from registry failed. Using default values (" + dane + ").");
                receiveTimeout *= 1000;
            }
        }

        private void startServers()
        {
            sbsClient = new SbsClients(sbsPort);
            sbsClient.newThread();

            konwerter = new AdsbFormatConverter(fixCRC, aircraftTTL);
            
            byte[] data = new byte[1024];
            string stringData;
            int recv;
            TcpClient server;

            while (_shouldWork)
            {
                try
                {
                    server = new TcpClient(sourceAddress, rawPort);
                }
                catch (SocketException)
                {
                    EventLog.WriteEntry(sSource, "Unable to connect to server. Waiting 60 seconds before reconnecting.");
                    System.Threading.Thread.Sleep(60000);
                    continue;
                }

                NetworkStream ns = server.GetStream();

                while (_shouldWork)
                {
                    try
                    {
                        data = new byte[1024];//should be more than enough
                        recv = ns.Read(data, 0, data.Length);
                        stringData = Encoding.ASCII.GetString(data, 0, recv);
                        
                        Regex r = new Regex(@"\*(.+?)\;");
                        MatchCollection mc = r.Matches(stringData);

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

                        ns.Write(data, 0, 1);//without that there is now way to check connection status  other than waiting forever (might be problematic - forever is a long time to wait)
                    }
                    catch (System.IO.IOException)
                    {
                        EventLog.WriteEntry(sSource, "Connection lost. Waiting 60 seconds before reconnecting.");
                        ns.Close();
                        server.Close();
                        System.Threading.Thread.Sleep(60000);
                        break;
                    }
                }

                sbsClient.stopThread();
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
                    //Console.WriteLine(ex.ToString());
                    //no reason to log client disconnection
                    arr.Add(Item.Key);
                }
            }

            for (int i = 0; i < arr.Count; ++i)//remove disconnected clients
            {
                clientsList.Remove(arr[i]);
            }
        }
    }
}
