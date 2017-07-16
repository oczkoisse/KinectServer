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
using System.Threading;

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
        private List<TcpClient> connectedAudioClients = new List<TcpClient>();

        private KinectSensor sensor = null;
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private AudioBeamFrameReader audioFrameReader = null;

        // To keep sync between MultiSouceFrame and AudioBeamFrame
        private long lastTimestamp;

        private void HandleNewClient(IAsyncResult res)
        {
            TcpClient c = null;
            try
            {
                c = server.EndAcceptTcpClient(res);
            }
            catch(ObjectDisposedException e)
            {
                
            }

            // Go back to accepting connections
            ContinueAcceptConnections();

            if (c == null)
                return;

            // Meanwhile read first four bytes of this client
            // 31 bits for receiving frame type or a combination thereof
            byte[] requestedFrames = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            int bytesRead = c.GetStream().Read(requestedFrames, 0, 4);
            Debug.Assert(bytesRead == 4);

            List<Readers.FrameType> activeFrames = GetActiveFrames(requestedFrames);

            if (activeFrames.Count >= 1)
            {
                if (activeFrames.Count == 1 && activeFrames[0] == FrameType.Audio)
                {
                    lock(connectedAudioClients)
                    {
                        connectedAudioClients.Add(c);
                        Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                        Trace.WriteLine((int)activeFrames[0]);
                    }
                }
                else if (!activeFrames.Contains(FrameType.Audio))
                {
                    lock (connectedClients)
                    {
                        connectedClients.Add(c, activeFrames);
                        Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                        foreach (var ft in activeFrames)
                            Trace.WriteLine((int)ft);
                    }
                }
            }
            else
            {
                Trace.WriteLine(String.Format("Rejecting client {0} because of an unrecognized stream type", c.Client.RemoteEndPoint.ToString()));
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
                Trace.WriteLine(String.Format("Problem in accepting a TCP connection request: {0}", e.Message));
            }
        }

        public MainWindow()
        {
            Interlocked.Exchange(ref lastTimestamp, Int64.MinValue);
            bool success = InitializeKinect();
            if (success)
            {
                server.Start();
                ContinueAcceptConnections();
                InitializeComponent();
            }
        }

        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame msf = e.FrameReference.AcquireFrame();

            long prevTimestamp = Interlocked.Read(ref lastTimestamp);
            long curTimestamp = DateTime.Now.Ticks;

            
            Dictionary<Readers.FrameType, Readers.Frame> cachedFrames = new Dictionary<Readers.FrameType, Readers.Frame>();
            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            // Will lock new connections until all previous clients are sent data
            lock (connectedClients)
            {
                // Cache all frames
                foreach (var client in connectedClients.Keys)
                {
                    foreach (var frameType in connectedClients[client])
                    {
                        if (!cachedFrames.ContainsKey(frameType))
                        {
                            KSIM.Readers.Frame frame = frameType.GetReader().Read(msf);
                            if (frame != null)
                            {
                                frame.Timestamp = curTimestamp;
                                cachedFrames[frameType] = frame;
                            }
                            else
                            {
                                // To ensure synchronization, do not send any frame if one of the subscribed ones is unavailable (null)
                                // But we still need to dispose of the frames already cached
                                goto DisposeFrames;
                            }
                        }
                    }
                }

                // Safely update last known timestamp
                // Optimistically update lastTimestamp to be ready to be written to clients
                if(connectedClients.Count > 0)
                    Interlocked.Exchange(ref lastTimestamp, curTimestamp);


                // We are ensured that all the subscribed frames have already been cached
                foreach (var client in connectedClients.Keys)
                {
                    // Send all subscribed frames to client together to avoid synchronization issues between subscribed streams
                    foreach(var frameType in connectedClients[client])
                    {
                        using (var ms = new MemoryStream())
                        {
                            cachedFrames[frameType].Serialize(ms);
                            byte[] dataToSend = ms.ToArray();
                            try
                            {
                                client.GetStream().Write(dataToSend, 0, dataToSend.Length);
                            }
                            catch (IOException)
                            {
                                Trace.WriteLine(String.Format("Client {0} disconnected", client.Client.RemoteEndPoint.ToString()));
                                clientsToBeDisconnected.Add(client);
                                // No need to send other frames subscribed by the client since it is already disconnected
                                break;
                            }
                        }
                    }
                }

                // If it turns out that all connected clients have in fact disconnected
                // then revert back to previous timestamp
                if (clientsToBeDisconnected.Count == connectedClients.Count)
                    Interlocked.Exchange(ref lastTimestamp, prevTimestamp);

                // Remove clients that are already disconnected
                foreach (var client in clientsToBeDisconnected)
                {
                    client.Close();
                    connectedClients.Remove(client);
                }
            }
            
            DisposeFrames:
            // Dispose frames quickly otherwise Kinect will hang
            foreach (var frame in cachedFrames.Values)
                frame.Dispose();
        }

        private void Reader_AudioFrameArrived(object sender, AudioBeamFrameArrivedEventArgs e)
        {
            var audioBeamList = e.FrameReference.AcquireBeamFrames();

            var f = (AudioFrame)KSIM.Readers.FrameType.Audio.GetReader().Read(audioBeamList);
            if (f == null)
                return;

            f.Timestamp = Interlocked.Read(ref lastTimestamp);
            
            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            using (var ms = new MemoryStream())
            {
                f.Serialize(ms);
                // Dispose quickly else Kinect will hang
                f.Dispose();
                byte[] dataToSend = ms.ToArray();
                lock(connectedAudioClients)
                {
                    foreach(var client in connectedAudioClients)
                    {
                        try
                        {
                            client.GetStream().Write(dataToSend, 0, dataToSend.Length);
                        }
                        catch (IOException)
                        {
                            Trace.WriteLine(String.Format("Client {0} disconnected", client.Client.RemoteEndPoint.ToString()));
                            clientsToBeDisconnected.Add(client);
                            // No need to send other frames subscribed by the client since it is already disconnected
                            break;
                        }
                    }

                    // Remove clients that are already disconnected
                    foreach (var client in clientsToBeDisconnected)
                    {
                        client.Close();
                        connectedAudioClients.Remove(client);
                    }
                }
            }
            
        }


        private bool InitializeKinect()
        {
            var sensor = KinectSensor.GetDefault();
            if (sensor != null)
            {
                var msfr = sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.Body);
                msfr.MultiSourceFrameArrived += Reader_MultiSourceFrameArrived;

                var beams = sensor.AudioSource.AudioBeams;
                if (beams != null && beams.Count > 0)
                {
                    // Beam angle is in Radians theroretically between -1.58 to 1.58 (+- 90 degrees)
                    // Practically, Kinect is limited to -0.87 to 0.87 (+- 90 degrees) with 5 degree increments
                    // Note that setting beam mode and beam angle will only work if the
                    // application window is in the foreground.
                    // Furthermore, setting these values is an asynchronous operation --
                    // it may take a short period of time for the beam to adjust.
                    beams[0].AudioBeamMode = AudioBeamMode.Manual;
                    beams[0].BeamAngle = 0.0f;

                    var afr = sensor.AudioSource.OpenReader(); 
                    afr.FrameArrived += Reader_AudioFrameArrived;

                    sensor.Open();

                    this.sensor = sensor;
                    this.multiSourceFrameReader = msfr;
                    this.audioFrameReader = afr;

                    return true;
                }
            }
            return false;
        }

        
        private void ResetServer()
        {
            server.Stop();
            server = new TcpListener(IPAddress.Any, PORT);
            server.Start();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            lock (connectedClients)
            {
                foreach (var client in connectedClients.Keys)
                    client.Close();
            }
            lock(connectedAudioClients)
            {
                foreach (var client in connectedAudioClients)
                    client.Close();
            }
            // Stop listening too
            server.Stop();

            // Release Kinect resources
            multiSourceFrameReader.Dispose();
            audioFrameReader.Dispose();
            sensor.Close();
        }
    }
}
