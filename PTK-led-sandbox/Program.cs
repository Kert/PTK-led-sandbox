using System;

namespace PTK_led_sandbox
{
    class Program
    {
        static void Main(string[] args)
        {
            int displayChunk = 0;
            bool surveyCompleted = false;
            do
            {
                Console.WriteLine("Which LED display to set image for? 1 or 2 (top or bottom)");
                string answer = Console.ReadLine();
                if (answer == "1")
                {
                    displayChunk = 0;
                    surveyCompleted = true;
                }
                else if (answer == "2")
                {
                    displayChunk = 4;
                    surveyCompleted = true;
                }
            } while (!surveyCompleted);

            const int headerOffset = 118;
            const int LENGTH = 64;
            const int HEIGTH = 32 * 4;        

            byte[] bmpFile = System.IO.File.ReadAllBytes("led.bmp");
            byte[] imgData = new ArraySegment<byte>(bmpFile, headerOffset, bmpFile.Length - headerOffset).ToArray();

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
            byte[] initString = new byte[256 + 3];            
            int initStringIndex = 0;
            string fileOutputStr = "";
            int displayChunkBlock = 0;
            for (int i = 0; i < LENGTH * HEIGTH; i++)
            {
                if (!Convert.ToBoolean(i % MAX_CHUNK_SIZE))
                {
                    if (i > 0)
                    {
                        fileOutputStr += '\"' + Convert.ToBase64String(initString) + "\"," + Environment.NewLine;
                        Array.Clear(initString, 0, initString.Length);
                    }

                    if (displayChunkBlock > 3)
                    {
                        displayChunk++;
                        displayChunkBlock = 0;
                    }

                    initString[0] = 0x23;
                    initString[1] = (byte)displayChunk;
                    initString[2] = (byte)displayChunkBlock;
                    initStringIndex = 3;
                    displayChunkBlock++;
                }
                if (!Convert.ToBoolean(i % 2))
                {
                    initString[initStringIndex] = (byte)((convertedImg[i] << 4) & 0xF0);
                    initStringIndex++;
                }
                else
                {
                    initString[initStringIndex - 1] |= convertedImg[i];
                }
            }

            fileOutputStr += '\"' + Convert.ToBase64String(initString) + "\"," + Environment.NewLine;
            System.IO.File.WriteAllText("inits.txt", fileOutputStr);

            return;
        }
    }
}