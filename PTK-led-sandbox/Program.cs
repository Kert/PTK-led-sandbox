using System;
using HidSharp;
using System.Linq;
using System.Threading.Tasks;
using Accord.Imaging;
using Accord.Imaging.Filters;
using Accord.Video.FFMPEG;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PTK_led_sandbox
{
    internal class Program
    {
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
                using (var vFReader = new VideoFileReader())
                {
                    var writer = new VideoFileWriter();
                    vFReader.Open("anime-walking-gif-14.gif");
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
                        // this also changes the format to 8bpp grayscale
                        Grayscale gfilter = Grayscale.CommonAlgorithms.BT709;
                        targetBmp = gfilter.Apply(targetBmp);

                        var bData = targetBmp.LockBits(ImageLockMode.ReadOnly);
                        var length = bData.Stride * bData.Height;
                        byte[] pixelData = new byte[length];
                        Marshal.Copy(bData.Scan0, pixelData, 0, length);
                        targetBmp.UnlockBits(bData);

                        // flip because wrong order for converter
                        for (int p = 0; p < pixelData.Length; p += targetBmp.Width)
                            Array.Reverse(pixelData, p, targetBmp.Width);
                        Array.Reverse(pixelData);

                        // pass this to the inits converter
                        byte[] pixelData4bpp = new byte[4096];
                        int c = 0;
                        for (int i = 0; i < (pixelData.Length - 1); i += 2)
                        {
                            byte h = (byte)((pixelData[i + 1] >> 4) & 0x0F);
                            byte l = (byte)((pixelData[i]) & 0xF0);

                            pixelData4bpp[c] |= h;
                            pixelData4bpp[c] |= l;
                            c++;
                        }

                        byte[,] features = convert_bmp(0, pixelData4bpp);
                        for (int i = 0; i < features.GetLength(0); i++)
                        {
                            byte[] row = Enumerable.Range(0, features.GetUpperBound(1) + 1)
                              .Select(j => features[i, j])
                              .ToArray();

                            //Console.WriteLine(Convert.ToBase64String(row));

                            hidStream.SetFeature(row);
                        }
                    }
                    vFReader.Close();
                }
            }
            else
            {
                Console.WriteLine("Failed to open device.");
            }
        }

        static byte[,] convert_bmp(int displayChunk, byte[] imgData)
        {
            const int LENGTH = 64;
            const int HEIGTH = 32 * 4;

            // flipping stuff because in bmp file it's stored in reverse 
            // flip every half of a byte
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
}
