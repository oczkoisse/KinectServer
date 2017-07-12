using KSIM.Readers;
using Microsoft.Kinect;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace KSIM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int PORT = 8000;

        private TcpListener server = new TcpListener(IPAddress.Any, PORT);

        private Dictionary<TcpClient, List<Readers.FrameType>> connectedClients = new Dictionary<TcpClient, List<Readers.FrameType>>();

        private void HandleNewClient(IAsyncResult res)
        {
            TcpClient c = server.EndAcceptTcpClient(res);
            // Go back to accepting connections
            ContinueAcceptConnections();
            // Meanwhile read first four bytes of this client
            // 31 bits for receiving frame type or a combination thereof
            byte[] requestedFrames = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            int bytesRead = c.GetStream().Read(requestedFrames, 0, 4);
            Debug.Assert(bytesRead == 4);

            List<Readers.FrameType> activeFrames = GetActiveFrames(requestedFrames);

            if (activeFrames.Count != 0)
            {
                lock (connectedClients)
                {
                    connectedClients.Add(c, activeFrames);
                }
            }
            else
            {
                c.Close();
            }
        }

        private List<Readers.FrameType> GetActiveFrames(byte[] frameBytes)
        {
            List<Readers.FrameType> activeFrames = new List<Readers.FrameType>();
            // BitArray assumes these 4 bytes are little endian
            // i.e. first byte corresponds to bits 0-7
            BitArray reqFramesAsBits = new BitArray(frameBytes);

            // Determine which stream bits are active
            foreach (Readers.FrameType ft in Enum.GetValues(typeof(Readers.FrameType)).Cast<Readers.FrameType>())
            {
                if (reqFramesAsBits.Get((int)ft))
                    activeFrames.Add(ft);
            }

            return activeFrames;
        }

        private void ContinueAcceptConnections()
        {
            try
            {
                server.BeginAcceptTcpClient(HandleNewClient, null);
            }
            catch (Exception e)
            {
                Console.WriteLine("Problem in accepting a TCP connection request:");
                Console.WriteLine(e.Message);
            }
        }

        public MainWindow()
        {
            server.Start();
            InitializeKinect();
            ContinueAcceptConnections();
            InitializeComponent();
        }

        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            Dictionary<Readers.FrameType, byte[]> cachedFrames = new Dictionary<Readers.FrameType, byte[]>();

            //Console.WriteLine("Getting frame...");
            MultiSourceFrame msf = e.FrameReference.AcquireFrame();
            //Console.WriteLine("Got the frame");
            long curTimestamp = DateTime.Now.Ticks;

            List<Readers.Frame> framesToBeDisposed = new List<Readers.Frame>();
            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            // Will lock new connections until all previous clients are sent data
            //Console.WriteLine("Trying to get a lock on connected clients list");
            lock (connectedClients)
            {
                //Console.WriteLine("Got the lock");
                foreach (var client in connectedClients.Keys)
                {
                    foreach (var frameType in connectedClients[client])
                    {
                        if (!cachedFrames.ContainsKey(frameType))
                        {
                            using (var ms = new MemoryStream())
                            {
                                KSIM.Readers.Frame frame = frameType.GetReader().read(msf);
                                // TO DO: To ensure synchronization, ensure that either send all frames or none if any of the subscribed frames is null
                                if (frame != null)
                                {
                                    frame.Timestamp = curTimestamp;
                                    frame.Serialize(ms);
                                    cachedFrames[frameType] = ms.ToArray();
                                    try
                                    {
                                        client.GetStream().Write(cachedFrames[frameType], 0, cachedFrames[frameType].Length);
                                    }
                                    catch(IOException ex)
                                    {
                                        Console.WriteLine("Client {0} disconnected", client.Client.RemoteEndPoint.ToString());
                                        clientsToBeDisconnected.Add(client);
                                        // No need to send other frames subscribed by the client since it is already disconnected
                                        break;
                                    }
                                    framesToBeDisposed.Add(frame);
                                }
                            }
                        }
                    }
                }
            }
            
            // Dispose frames quickly otherwise Kinect will hang
            foreach (var frame in framesToBeDisposed)
                frame.Dispose();

            // Remove clients that are already disconnected
            foreach (var client in clientsToBeDisconnected)
            {
                client.Close();
                connectedClients.Remove(client);
            }
        }


        private void InitializeKinect()
        {
            var sensor = KinectSensor.GetDefault();
            var msfr = sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.Body);
            msfr.MultiSourceFrameArrived += Reader_MultiSourceFrameArrived;
            sensor.Open();
        }

        private void ResetServer()
        {
            server.Stop();
            server = new TcpListener(IPAddress.Any, PORT);
            server.Start();
        }
    }
}
