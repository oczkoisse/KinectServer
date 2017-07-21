﻿using KSIM.Readers;
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
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;

namespace KSIM
{
    /// <summary>
    /// Class holding the interaction logic for MainWindow.xaml.
    /// In addition, this class also handles new incoming client stream requests and
    /// interfaces with the underlying Kinect hardware to facilitate these stream requests.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The port at which the application listens for incoming stream requests for Kinect clients
        /// </summary>
        private const int PORT = 8000;

        /// <summary>
        /// The server object to listen for incoming client requests
        /// </summary>
        private TcpListener server = new TcpListener(IPAddress.Any, PORT);
        /// <summary>
        /// A dictionary for holding stream types subscribed by each client. The subscribed streams should not contain Audio or Speech
        /// </summary>
        private Dictionary<TcpClient, List<Readers.FrameType>> connectedClients = new Dictionary<TcpClient, List<Readers.FrameType>>();

        /// <summary>
        /// A dictionary for holding clients subscribed to Audio stream. These clients cannot be subscribed to any other stream.
        /// </summary>
        private List<TcpClient> connectedAudioClients = new List<TcpClient>();

        /// <summary>
        /// A dictionary for holding clients subscribed to Speech stream. These clients cannot be subscribed to any other stream.
        /// </summary>
        private List<TcpClient> connectedSpeechClients = new List<TcpClient>();

        /// <summary>
        /// Reference to the Kinect sensor. Needed to Close() at the application exit.
        /// </summary>
        private KinectSensor sensor = null;

        /// <summary>
        /// Reference to the MultiSourceFrameReader reader got from the Kinect sensor. Needed to Dispose() at the application exit.
        /// </summary>
        private MultiSourceFrameReader multiSourceFrameReader = null;

        /// <summary>
        /// Reference to the AudioBeamFrameReader reader got from the Kinect sensor. Needed to Dispose() at the application exit.
        /// </summary>
        private AudioBeamFrameReader audioFrameReader = null;

        /// <summary>
        /// Conversion stream needed to convert the raw 32 bit floating point samples emitted by Kinect into PCM 16 bit data
        /// that can be recognized by the SpeechRecognitionEngine.
        /// Needed to speechActive = false at application exit
        /// </summary>
        private Pcm16Stream audioStream;

        /// <summary>
        /// Reference to the SpeechRecognitionEngine. Needed to stop async recogntion at the application exit.
        /// </summary>
        private SpeechRecognitionEngine speechEngine;

        // To keep sync between MultiSouceFrame, AudioBeamFrame and Speech stream
        // Note that we cannot ensure perfect sync between AudioBeamFrame and Speech stream
        private long lastTimestamp;

        /// <summary>
        /// Provides an atomic read/write for the last timestamp set in <see cref="Reader_MultiSourceFrameArrived(object, MultiSourceFrameArrivedEventArgs)"/>
        /// </summary>
        /// <value>
        /// The timestamp set in <see cref="Reader_MultiSourceFrameArrived(object, MultiSourceFrameArrivedEventArgs)"/> the last time that event was fired
        /// </value>
        /// <remarks>
        /// This still cannot ensure perfect sync between <see cref="Reader_AudioFrameArrived(object, AudioBeamFrameArrivedEventArgs)"/> 
        /// and <see cref="Reader_SpeechRecognized(object, SpeechRecognizedEventArgs)"/>, since those operate in their own event threads.
        /// </remarks>
        private long LastTimestamp
        {
            get { return Interlocked.Read(ref lastTimestamp); }
            set { Interlocked.Exchange(ref lastTimestamp, value); }
        }

        /// <summary>
        /// Handles incoming client connections. The current implementation verifies these requests by
        /// reading first 4 bytes sent by the client, which are are validated by <see cref="GetActiveFrames(byte[])"/> into a list of stream types.
        /// The client is accepted as a valid subscriber if and only if one of the following is true:
        /// <list type="bullet">
        /// <item> The client request only constitutes the Audio stream type</item>
        /// <item> The client request only constitutes the Speech stream type</item>
        /// <item> The client request contains one or more stream types other than Audio and Speech</item>
        /// </list> 
        /// </summary>
        /// <param name="res"></param>
        private void HandleNewClient(IAsyncResult res)
        {
            TcpClient c = null;
            try
            {
                c = server.EndAcceptTcpClient(res);
            }
            catch(ObjectDisposedException e)
            {
                Debug.Write("Server closed while still listening.");
            }
            
            // Server has been stopped so don't listen anymore
            if (c == null)
                return;

            // Else go back to accepting connections
            ContinueAcceptConnections();


            // Read first four bytes of this client
            byte[] requestedFrames = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            int bytesRead = c.GetStream().Read(requestedFrames, 0, 4);
            Debug.Assert(bytesRead == 4);

            // Get a list of valid stream types from these 4 bytes
            List<Readers.FrameType> activeFrames = GetActiveFrames(requestedFrames);

            // If there are one or more valid stream requests
            if (activeFrames.Count >= 1)
            {
                // If the request does not involve either Audio or Speech
                if (!activeFrames.Contains(FrameType.Audio) && !activeFrames.Contains(FrameType.Speech))
                {
                    // Add the client to list of connected clients other than speech and audio clients
                    lock (connectedClients)
                    {
                        connectedClients.Add(c, activeFrames);
                        Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                        foreach (var ft in activeFrames)
                            Trace.WriteLine((int)ft);
                    }
                }
                // If the request contains only 1 stream type, which consists either Audio or Speech
                else if (activeFrames.Count == 1)
                {
                    // If Audio, add to the list of connected clients for Audio
                    if (activeFrames[0] == FrameType.Audio)
                    {
                        lock (connectedAudioClients)
                        {
                            connectedAudioClients.Add(c);
                            Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                            Trace.WriteLine((int)activeFrames[0]);
                        }
                    }
                    // If Speech, add to the list of connected clients for Speech
                    // Acutally, don't need the following check
                    else if (activeFrames[0] == FrameType.Speech)
                    {
                        lock (connectedSpeechClients)
                        {
                            connectedSpeechClients.Add(c);
                            Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                            Trace.WriteLine((int)activeFrames[0]);
                        }
                    }
                }
                else
                {
                    // Reject as an invalid stream as the stream contains a combination of Audio/Speech with other stream types
                    Trace.WriteLine(String.Format("Rejecting client {0} because it is not possible to combine Audio/Speech stream with other streams.", c.Client.RemoteEndPoint.ToString()));
                    c.Close();
                }
            }
            else
            {
                // Reject as no valid stream was identified
                Trace.WriteLine(String.Format("Rejecting client {0} because of an unrecognized stream type(s)", c.Client.RemoteEndPoint.ToString()));
                c.Close();
            }
        }


        /// <summary>
        /// Decodes the input bytes into a list of frame types. Currently, bits 1-31 are the only ones considered, which correspond to frame types as follows:
        /// <list type="table">
        /// <listheader>
        /// <term>Bit position</term>
        /// <description>Frame type</description>
        /// </listheader>
        /// <item>
        /// <term>1</term>
        /// <description>Color</description>
        /// <term>2</term>
        /// <description>Speech</description>
        /// <term>3</term>
        /// <description>Audio</description>
        /// <term>4</term>
        /// <description>Depth</description>
        /// <term>5</term>
        /// <description>Closest Body</description>
        /// <term>6</term>
        /// <description>Left Hand Depth</description>
        /// <term>7</term>
        /// <description>Right Hand Depth</description>
        /// <term>8</term>
        /// <description>Head Depth</description>
        /// </item>
        /// </list>
        /// </summary>
        /// <param name="frameBytes"></param>
        /// <returns>A list of frame types as decoded from the input bytes</returns>
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


        /// <summary>
        /// Starts an asynchronous client accept operation
        /// </summary>
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

        /// <summary>
        /// The constructor for MainWindow class. Sets the default value for <see cref="LastTimestamp"/> as minimum possible for 64-bit signed integer.
        /// Initializes the Kinect sensor, along with different native frame readers. 
        /// If successfull, also starts a server for accepting client requests for different streams.
        /// Finally, setups the window controls
        /// </summary>
        public MainWindow()
        {
            LastTimestamp = Int64.MinValue;
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

            long prevTimestamp = LastTimestamp;
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
                // Optimistically update LastTimestamp to be ready to be written to clients
                if(connectedClients.Count > 0)
                    LastTimestamp = curTimestamp;


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

            f.Timestamp = LastTimestamp;
            
            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            using (var ms = new MemoryStream())
            {
                f.Serialize(ms);
                // Dispose quickly else Kinect will hang
                f.Dispose();
                // Cache
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

        private void Reader_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            var result = e.Result;

            var f = (SpeechFrame)KSIM.Readers.FrameType.Speech.GetReader().Read(result);
            if (f == null)
                return;
            
            f.Timestamp = LastTimestamp;

            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            using (var ms = new MemoryStream())
            {
                f.Serialize(ms);
                f.Dispose();
                // Cache
                byte[] dataToSend = ms.ToArray();
                lock (connectedSpeechClients)
                {
                    foreach (var client in connectedSpeechClients)
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
                        connectedSpeechClients.Remove(client);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the metadata for the speech recognizer (acoustic model) most suitable to
        /// process audio from Kinect device.
        /// </summary>
        /// <returns>
        /// RecognizerInfo if found, <code>null</code> otherwise.
        /// </returns>
        private static RecognizerInfo TryGetKinectRecognizer()
        {
            IEnumerable<RecognizerInfo> recognizers;

            // This is required to catch the case when an expected recognizer is not installed.
            // By default - the x86 Speech Runtime is always expected. 
            try
            {
                recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }

            foreach (RecognizerInfo recognizer in recognizers)
            {
                string value;
                recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
                if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return recognizer;
                }
            }

            return null;
        }

        private bool InitializeKinect()
        {
            var sensor = KinectSensor.GetDefault();
            if (sensor != null)
            {
                var msfr = sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.Body);

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
                    
                    RecognizerInfo ri = TryGetKinectRecognizer();
                    if (null != ri)
                    {
                        this.audioStream = new Pcm16Stream(beams[0].OpenInputStream());
                        // let the underlying stream know speech is going active
                        this.audioStream.SpeechActive = true;

                        this.speechEngine = new SpeechRecognitionEngine(ri.Id);

                        var utterances = new Choices();
                        utterances.Add(new SemanticResultValue("left", "LEFT"));
                        utterances.Add(new SemanticResultValue("to the left", "LEFT"));
                        utterances.Add(new SemanticResultValue("to my left", "LEFT"));
                        utterances.Add(new SemanticResultValue("to your right", "LEFT"));

                        utterances.Add(new SemanticResultValue("right", "RIGHT"));
                        utterances.Add(new SemanticResultValue("to the right", "RIGHT"));
                        utterances.Add(new SemanticResultValue("to my right", "RIGHT"));
                        utterances.Add(new SemanticResultValue("to your left", "RIGHT"));

                        utterances.Add(new SemanticResultValue("yes", "YES"));
                        utterances.Add(new SemanticResultValue("yeah", "YES"));

                        utterances.Add(new SemanticResultValue("no", "NO"));

                        utterances.Add(new SemanticResultValue("red", "RED"));
                        utterances.Add(new SemanticResultValue("the red one", "RED"));
                        utterances.Add(new SemanticResultValue("blue", "BLUE"));
                        utterances.Add(new SemanticResultValue("the blue one", "BLUE"));
                        utterances.Add(new SemanticResultValue("green", "GREEN"));
                        utterances.Add(new SemanticResultValue("the green one", "GREEN"));

                        var gb = new GrammarBuilder { Culture = ri.Culture };
                        gb.Append(utterances);

                        this.speechEngine.LoadGrammar(new Grammar(gb));
                        
                        this.speechEngine.SetInputToAudioStream(
                            this.audioStream, new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                        this.speechEngine.RecognizeAsync(RecognizeMode.Multiple);
                    }

                    sensor.Open();

                    this.sensor = sensor;

                    this.multiSourceFrameReader = msfr;
                    multiSourceFrameReader.MultiSourceFrameArrived += Reader_MultiSourceFrameArrived;

                    this.audioFrameReader = afr;
                    audioFrameReader.FrameArrived += Reader_AudioFrameArrived;

                    speechEngine.SpeechRecognized += Reader_SpeechRecognized;

                    return true;
                }
            }
            return false;
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
            lock (connectedSpeechClients)
            {
                foreach (var client in connectedSpeechClients)
                    client.Close();
            }
            // Stop listening too
            server.Stop();

            // Release Kinect resources
            if (multiSourceFrameReader != null)
            {
                multiSourceFrameReader.MultiSourceFrameArrived -= Reader_MultiSourceFrameArrived;
                multiSourceFrameReader.Dispose();
            }
            if (audioFrameReader != null)
            {
                audioFrameReader.FrameArrived -= Reader_AudioFrameArrived;
                audioFrameReader.Dispose();
            }

            if (audioStream != null)
            {
                audioStream.SpeechActive = false;
            }

            if (speechEngine != null)
            {
                speechEngine.SpeechRecognized -= Reader_SpeechRecognized;
                speechEngine.RecognizeAsyncStop();
            }

            if (sensor != null)
                sensor.Close();
        }
    }
}
