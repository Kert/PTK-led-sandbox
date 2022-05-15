using System;
using HidSharp;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Accord.Imaging;
using Accord.Imaging.Filters;
using Accord.Video.FFMPEG;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PTK_led_sandbox
{
    internal class Program
    {
        enum PTK_LED
        {
            TOP = 0,
            BOTTOM = 4
        };

        static async Task Main(string[] args)
        {
            // find ptk-1240
            HidDevice tablet = Array.Find(DeviceList.Local.GetHidDevices().ToArray(),
                delegate (HidDevice d)
                {
                    return d.ProductID == 187 && d.VendorID == 1386 && d.MaxFeatureReportLength > 0;
                }
            );

            if (tablet == null)
            {
                Console.WriteLine("Failed to find PTK-1240");
                return;
            }

            HidStream hidStream;
            if (tablet.TryOpen(out hidStream))
            {
                Console.WriteLine("Opened device.");
                // init tablet just in case
                hidStream.SetFeature(Convert.FromBase64String("AgI="));

                PTK_LED led = PTK_LED.TOP;
                bool surveyCompleted = false;
                do
                {
                    Console.WriteLine("Which LED display to draw to? 1 or 2 (top or bottom)");
                    string answer = Console.ReadLine();
                    if (answer == "1")
                    {
                        led = PTK_LED.TOP;
                        surveyCompleted = true;
                    }
                    else if (answer == "2")
                    {
                        led = PTK_LED.BOTTOM;
                        surveyCompleted = true;
                    }
                } while (!surveyCompleted);

                bool flip = false;
                surveyCompleted = false;
                do
                {
                    Console.WriteLine("Flip displayed data vertically? y or n");
                    string answer = Console.ReadLine();
                    if (answer == "y")
                    {
                        flip = true;
                        surveyCompleted = true;
                    }
                    else if (answer == "n")
                    {
                        flip = false;
                        surveyCompleted = true;
                    }
                } while (!surveyCompleted);

                surveyCompleted = false;
                do
                {
                    Console.WriteLine("Clock (1), twitch chat (2), play media (3)?");
                    string answer = Console.ReadLine();
                    if (answer == "1")
                    {
                        Clock(led, flip, hidStream);
                        surveyCompleted = true;
                    }
                    else if (answer == "2")
                    {
                        await Twitch(led, flip, hidStream);
                        surveyCompleted = true;
                    }
                    else if (answer == "3")
                    {
                        PlayMedia("anime-walking-gif-14.gif", led, flip, hidStream);
                        surveyCompleted = true;
                    }
                } while (!surveyCompleted);
            }
            else
            {
                Console.WriteLine("Failed to open device.");
            }
        }

        static void Clock(PTK_LED led, bool flip, HidStream hidStream)
        {
            const int UPDATE_RATE_MS = 1000;
            Bitmap bitmap = CreateEmptyBitmap(Color.Black);
            Graphics g = Graphics.FromImage(bitmap);
            // disable smoothing
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            Rectangle rect = new Rectangle(0, 50, 64, 128);
            while (true)
            {
                string time = DateTime.Now.ToString("HH:mm");
                g.Clear(Color.Black);
                StringFormat stringFormat = new StringFormat();
                stringFormat.Alignment = StringAlignment.Center;
                g.DrawString(time, new Font("Tahoma", 16), Brushes.White, rect, stringFormat);
                g.Flush();
                SendBitmapToLED(bitmap, led, flip, hidStream);
                System.Threading.Thread.Sleep(UPDATE_RATE_MS);
            }
        }

        static async Task Twitch(PTK_LED led, bool flip, HidStream hidStream)
        {
            var tcpClient = new TcpClient();
            string ip = "irc.chat.twitch.tv";
            int port = 6667;
            Random rnd = new Random();
            string botUsername = "justinfan" + rnd.Next(1000, 1000000);
            string channelName = "#kert_c";
            await tcpClient.ConnectAsync(ip, port);
            StreamReader streamReader = new StreamReader(tcpClient.GetStream());
            StreamWriter streamWriter = new StreamWriter(tcpClient.GetStream()) { NewLine = "\r\n", AutoFlush = true };
            await streamWriter.WriteLineAsync($"NICK {botUsername}");
            await streamWriter.WriteLineAsync($"JOIN {channelName}");

            Queue<string> msgQueue = new Queue<string>();

            Bitmap bitmap = CreateEmptyBitmap(Color.Black);
            Graphics g = Graphics.FromImage(bitmap);
            // disable smoothing
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            var myCustomFont = CustomFont.font;
            while (true)
            {
                string line = await streamReader.ReadLineAsync();
                Console.WriteLine(line);

                string[] split = line.Split(' ');
                //PING :tmi.twitch.tv
                //Respond with PONG :tmi.twitch.tv
                if (line.StartsWith("PING"))
                    await streamWriter.WriteLineAsync($"PONG {split[1]}");

                if (split.Length > 1 && split[1] == "PRIVMSG")
                {
                    //:mytwitchchannel!mytwitchchannel@mytwitchchannel.tmi.twitch.tv 
                    // ^^^^^^^^
                    //Grab this name here
                    int exclamationPointPosition = split[0].IndexOf("!");
                    string username = split[0].Substring(1, exclamationPointPosition - 1);
                    //Skip the first character, the first colon, then find the next colon
                    int secondColonPosition = line.IndexOf(':', 1);//the 1 here is what skips the first character
                    string message = line.Substring(secondColonPosition + 1);//Everything past the second colon
                    message = $"{username[0]}:{message}";

                    msgQueue.Enqueue(message);
                    if (msgQueue.Count() > 21)
                        msgQueue.Dequeue();

                    int offset = 0;
                    g.Clear(Color.Black);
                    foreach (string msg in msgQueue)
                    {
                        g.DrawString(msg, myCustomFont, Brushes.White, new Rectangle(0, offset, 200, 12));
                        offset += 6;
                    }
                    g.Flush();
                    SendBitmapToLED(bitmap, led, flip, hidStream);
                }
            }
        }

        static void PlayMedia(string fileName, PTK_LED led, bool flip, HidStream hidStream)
        {
            var vFReader = new VideoFileReader();
            vFReader.Open(fileName);
            while (true)
            {
                Bitmap frame = vFReader.ReadVideoFrame();
                if (frame == null)
                {
                    frame = vFReader.ReadVideoFrame(0);
                    //break;
                }
                Color trColor = Color.White;
                frame.MakeTransparent(trColor);

                //frame.Save("input.bmp", ImageFormat.Bmp);                        

                Bitmap targetBmp = frame.Clone(PixelFormat.Format32bppArgb);
                BaseResizeFilter filter = new ResizeBilinear(64, 128);
                targetBmp = filter.Apply(targetBmp);

                SendBitmapToLED(targetBmp, led, flip, hidStream);
            }
            vFReader.Close();
        }
        static Bitmap CreateEmptyBitmap(Color color)
        {
            Bitmap bitmap = new Bitmap(64, 128, PixelFormat.Format32bppArgb);
            var bData = bitmap.LockBits(ImageLockMode.WriteOnly);
            Drawing.FillRectangle(bData, new Rectangle(0, 0, 64, 128), color);
            bitmap.UnlockBits(bData);
            return bitmap;
        }

        static void SendBitmapToLED(Bitmap bitmap, PTK_LED led, bool flip, HidStream hidStream)
        {
            byte[] pixelDatahbpp = BitmapToHalfBitGrayscale(bitmap);
            byte[,] features = Convert_bmp((int)led, pixelDatahbpp, flip);
            for (int i = 0; i < features.GetLength(0); i++)
            {
                byte[] row = Enumerable.Range(0, features.GetUpperBound(1) + 1)
                  .Select(j => features[i, j])
                  .ToArray();

                //Console.WriteLine(Convert.ToBase64String(row));                            
                hidStream.SetFeature(row);
            }
        }

        static byte[] BitmapToHalfBitGrayscale(Bitmap bitmap)
        {
            // this also changes the format to 8bpp grayscale
            Grayscale gfilter = Grayscale.CommonAlgorithms.BT709;
            bitmap = gfilter.Apply(bitmap);

            var bData = bitmap.LockBits(ImageLockMode.ReadOnly);
            var length = bData.Stride * bData.Height;
            byte[] pixelData = new byte[length];
            Marshal.Copy(bData.Scan0, pixelData, 0, length);
            bitmap.UnlockBits(bData);

            // flip because wrong order for converter
            for (int p = 0; p < pixelData.Length; p += bitmap.Width)
                Array.Reverse(pixelData, p, bitmap.Width);
            Array.Reverse(pixelData);

            // pass this to the inits converter
            byte[] pixelDatahbpp = new byte[4096];
            int c = 0;
            for (int i = 0; i < (pixelData.Length - 1); i += 2)
            {
                byte h = (byte)((pixelData[i + 1] >> 4) & 0x0F);
                byte l = (byte)((pixelData[i]) & 0xF0);

                pixelDatahbpp[c] |= h;
                pixelDatahbpp[c] |= l;
                c++;
            }
            return pixelDatahbpp;
        }

        static byte[,] Convert_bmp(int displayChunk, byte[] imgData, bool flip = false)
        {
            const int LENGTH = 64;
            const int HEIGTH = 32 * 4;

            // flipping stuff because in bmp file it's stored in reverse 
            // flip every half of a byte
            if (!flip)
            {
                for (int i = 0; i < imgData.Length; i++)
                {
                    byte chr = imgData[i];
                    byte h = (byte)((chr >> 4) & 0x0F);
                    byte l = (byte)((chr & 0x0F) << 4);
                    imgData[i] = 0;
                    imgData[i] |= h;
                    imgData[i] |= l;
                }
                Array.Reverse(imgData);
            }

            byte[] convertedImg = new byte[LENGTH * HEIGTH];

            int x = 0;
            int y = 0;
            bool firstline = true;
            int c = 1;
            for (int i = 0; i < imgData.Length; i++)
            {
                byte chr = imgData[i];
                byte h = (byte)((chr >> 4) & 0x0F);
                byte l = (byte)(chr & 0x0F);

                int k1 = c;
                int k2 = c + 2;
                convertedImg[k1] = h;
                convertedImg[k2] = l;


                //Console.WriteLine("{0} {1}", k1, k2);
                c += 4;
                x += 2;

                if (x >= LENGTH)
                {
                    y++;
                    x = 0;
                    if (firstline)
                    {
                        firstline = false;
                        c -= LENGTH * 2 + 1;
                        //Console.WriteLine("c = {0}\n", c);
                    }
                    else
                    {
                        firstline = true;
                        c += 1;
                        //Console.WriteLine("c = {0}\n", c);
                    }
                }
            }


            const int MAX_CHUNK_SIZE = 512;

            byte[,] features = new byte[16, 256 + 3];

            int displayChunkBlock = 0;
            int featureIndex = 0;
            int currentByte = 0;
            for (int i = 0; i < LENGTH * HEIGTH; i++)
            {
                if (!Convert.ToBoolean(i % MAX_CHUNK_SIZE))
                {
                    if (i > 0)
                    {
                        featureIndex++;
                    }

                    if (displayChunkBlock > 3)
                    {
                        displayChunk++;
                        displayChunkBlock = 0;
                    }

                    features[featureIndex, 0] = 0x23;
                    features[featureIndex, 1] = (byte)displayChunk;
                    features[featureIndex, 2] = (byte)displayChunkBlock;
                    currentByte = 3;
                    displayChunkBlock++;
                }
                if (!Convert.ToBoolean(i % 2))
                {
                    features[featureIndex, currentByte] = (byte)((convertedImg[i] << 4) & 0xF0);
                    currentByte++;
                }
                else
                {
                    features[featureIndex, currentByte - 1] |= convertedImg[i];
                }
            }
            return features;
        }
    }
    static class CustomFont
    {
        private static readonly PrivateFontCollection fontCollection = new PrivateFontCollection();
        public static readonly Font font;

        static CustomFont()
        {
            fontCollection.AddFontFile("TinyCyrillic.ttf");
            font = new Font((FontFamily)fontCollection.Families[0], 4);
        }
    }
}
