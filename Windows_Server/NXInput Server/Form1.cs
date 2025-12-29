using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace NXInput_Server
{
    public partial class Form1 : Form
    {

        private static ViGEmClient vigemClient;
        public static IXbox360Controller controller;
        private static Thread networkThread;
        private static UdpClient udpSocket;
        private static bool running = false;
        private static bool switchConnected = false;
        private static IPEndPoint clientEndPoint;

        private bool _serverRunning;


        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                label2.Text = GetLocalIPv4();
            }
            catch (Exception ex)
            {
                label2.Text = "IP not found";
                richTextBox1.AppendText("Error getting IP: " + ex.Message + Environment.NewLine);
            }
        }

        private string GetLocalIPv4()
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                      .AddressList
                      .First(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                      .ToString();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!_serverRunning)
                StartServer();
            else
                StopServer();
        }



        private void StartServer()
        {
            if (_serverRunning) return;

            try
            {
                button1.Enabled = false;

                vigemClient = new ViGEmClient();
                controller = vigemClient.CreateXbox360Controller();
                controller.Connect();
                Log("Virtual controller created.");

                running = true;
                switchConnected = false;

                networkThread = new Thread(networking);
                networkThread.Start();

                _serverRunning = true;
                button1.Text = "Stop Server";
            }
            catch (Exception ex)
            {
                Log("Error starting server: " + ex.Message);
                StopServer();
            }
            finally
            {
                button1.Enabled = true;
            }
        }

        private void StopServer()
        {
            if (!_serverRunning) return;

            try
            {
                running = false;
                switchConnected = false;

                udpSocket?.Close();
                networkThread?.Join(1000);

                controller?.Disconnect();
                vigemClient?.Dispose();
                controller = null;
                vigemClient = null;

                Log("Server stopped.");
            }
            catch (Exception ex)
            {
                Log("Error stopping server: " + ex.Message);
            }
            finally
            {
                _serverRunning = false;
                button1.Text = "Start Server";
                button1.Enabled = true;
            }
        }

        private void networking()
        {
            byte[] data = new byte[1024];
            var ipep = new IPEndPoint(IPAddress.Any, 8192);

            try
            {
                udpSocket = new UdpClient(ipep);
                var sender = new IPEndPoint(IPAddress.Any, 0);

                Log("Waiting for Switch discovery...");

                while (!switchConnected && running)
                {
                    try
                    {
                        data = udpSocket.Receive(ref sender);
                        string msg = Encoding.ASCII.GetString(data).Trim();

                        if (msg.Contains("xbox_switch"))
                        {
                            udpSocket.Send(Encoding.ASCII.GetBytes("xbox\0"), Encoding.ASCII.GetBytes("xbox\0").Length, sender);
                            clientEndPoint = (IPEndPoint)sender;
                            switchConnected = true;
                            Log($"Connected to: {clientEndPoint.Address}");
                            break;
                        }
                    }
                    catch {}
                }

                if (!switchConnected)
                {
                    Log("Switch discovery failed");
                    return;
                }

                Log("Running...");

                while (running)
                {
                    try
                    {
                        data = udpSocket.Receive(ref sender);

                        if (data.Length < 2) continue;

                        byte code = data[0];
                        byte v1 = data[1];

                        if (code <= 0x0B)
                        {
                            var btn = MapCodeToXboxButton(code);
                            controller.SetButtonState(btn, v1 != 0);
                        }
                        else if (code <= 0x0F)
                        {
                            var btn = MapCodeToXboxButton(code);
                            controller.SetButtonState(btn, v1 != 0);
                        }
                        else if (code == 0x10) // ZL
                        {
                            controller.SetSliderValue(Xbox360Slider.LeftTrigger, v1 == 0 ? (byte)0x00 : (byte)0xFF);
                        }
                        else if (code == 0x11) // ZR
                        {
                            controller.SetSliderValue(Xbox360Slider.RightTrigger, v1 == 0 ? (byte)0x00 : (byte)0xFF);
                        }
                        else if (code == 0x12) // Left stick
                        {
                            short lx = (short)((data[1] << 8) | data[2]);
                            short ly = (short)((data[3] << 8) | data[4]);
                            controller.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
                            controller.SetAxisValue(Xbox360Axis.LeftThumbY, ly);
                        }
                        else if (code == 0x13) // Right stick
                        {
                            short rx = (short)((data[1] << 8) | data[2]);
                            short ry = (short)((data[3] << 8) | data[4]);
                            controller.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                            controller.SetAxisValue(Xbox360Axis.RightThumbY, ry);
                        }
                    }
                    catch (SocketException) { break; }
                    catch { /* ignore bad packets */ }
                }
            }
            catch (Exception ex)
            {
                Log("Networking error: " + ex.Message);
            }
        }




        private static Xbox360Button MapCodeToXboxButton(byte code)
        {
            switch (code)
            {
                case 0x01: return Xbox360Button.Up;
                case 0x02: return Xbox360Button.Down;
                case 0x03: return Xbox360Button.Left;
                case 0x04: return Xbox360Button.Right;
                case 0x05: return Xbox360Button.Back;
                case 0x06: return Xbox360Button.Start;
                case 0x07: return Xbox360Button.LeftThumb;
                case 0x08: return Xbox360Button.RightThumb;
                case 0x09: return Xbox360Button.LeftShoulder;
                case 0x0A: return Xbox360Button.RightShoulder;
                case 0x0C: return Xbox360Button.B;
                case 0x0D: return Xbox360Button.A;
                case 0x0E: return Xbox360Button.Y;
                case 0x0F: return Xbox360Button.X;
                default: return null;
            };
        }

        private void SetButtonState(Xbox360Button btn, bool pressed)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetButtonState(btn, pressed)));
                return;
            }
            controller.SetButtonState(btn, pressed);
        }

        private void displayButtons()
        {
            while (running)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(displayButtons));
                    Thread.Sleep(50);
                    continue;
                }
                Log("Controller active...");
                Thread.Sleep(100);
            }
        }

        private void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Log(text)));
                return;
            }
            richTextBox1.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            richTextBox1.ScrollToCaret();
        }

    }
}
