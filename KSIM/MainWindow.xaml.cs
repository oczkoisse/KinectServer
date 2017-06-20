using System;
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
            server.BeginAcceptTcpClient(HandleNewClient, null);
            // Meanwhile read first four bytes of this client
            // 31 bits for receiving frame type or a combination thereof
            byte[] requestedFrames = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            using (var ns = c.GetStream())
            {
                int bytesRead = ns.Read(requestedFrames, 0, 4);
                Debug.Assert(bytesRead == 4);
            }

            List<Readers.FrameType> activeFrames = new List<Readers.FrameType>();
            // BitArray assumes these 4 bytes are little endian
            // i.e. first byte corresponds to bits 0-7
            BitArray reqFramesAsBits = new BitArray(requestedFrames);

            // Determine which stream bits are active
            foreach (var ft in Enum.GetValues(typeof(Readers.FrameType)).Cast<Readers.FrameType>())
            {
                if (reqFramesAsBits.Get((int)ft))
                    activeFrames.Add(ft);
            }

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

        public MainWindow()
        {
            server.Start();
            try
            {
                server.BeginAcceptTcpClient(HandleNewClient, null);
            }
            catch (Exception e)
            {
                Console.WriteLine("Problem in accepting a TCP connection request:");
                Console.WriteLine(e.Message);
            }

            InitializeComponent();
        }

        private void ResetServer()
        {
            server.Stop();
            server = new TcpListener(IPAddress.Any, PORT);
            server.Start();
        }
    }
}
