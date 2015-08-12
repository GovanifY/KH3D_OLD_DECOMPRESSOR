using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace LZOvl_decompressor__KH3D
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            string input;
            Console.WriteLine("GovanifY LZOvl decompressor. Varient used in KH3D PMO format.");
            string arg = String.Join("", args);

            if (File.Exists(arg))
            {
                input= arg;
                using (FileStream input2 = File.Open(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (FileStream output = File.Open(input + "decompressed", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        Decompressor.Decompress(input2, input2.Length, output);
                    }
                }

            }
            Console.ReadLine();
            }
    }

    }