using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UsbLibConsole
{
    using System.Runtime.InteropServices;
    using UsbLib;
    using UsbLib.Usb;
    using UsbLib.Scsi.Commands;
    using System.IO;

    class Program
    {
        public Program()
        {
            
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public static byte[] readrsakeydata(string rsakey)
        {
            const string HeadRsaN = "pub_key n:";
            byte[] rsapubkeyn = null;
            StreamReader rsafile = File.OpenText(rsakey);
            string txtdata;
            while((txtdata = rsafile.ReadLine()) != null)
            {
                if(txtdata.Contains(HeadRsaN))
                {
                    
                    string rsakeynstr = txtdata.Substring(HeadRsaN.Length);
                    
                    rsapubkeyn = StringToByteArray(rsakeynstr);
                    break;
                }
            }
            rsafile.Close();
            return rsapubkeyn;
        }

        static int Main(string[] args)
        {
            if (args != null)
            {
                if(args.Length >= 2)
                {
                    string uboot = args[0];
                    string udisk = args[1];
                    string rsakeyfile = null;
                    byte[] rsakeyn = null;
                    if (!File.Exists(uboot))
                    {
                        Console.WriteLine("bootloader [{0}] not exist!", uboot);
                        return -10;
                    }

                    if (args.Length >= 3)
                    {
                        rsakeyfile = args[2]; 
                        if(!File.Exists(rsakeyfile))
                        {
                            Console.WriteLine("RSAKEY[{0}]不存在!", rsakeyfile);
                            return -11;
                        }
                        rsakeyn = readrsakeydata(rsakeyfile);
                        if(rsakeyn.Length != 256)
                        {
                            Console.WriteLine("rsakey data length[{0}] not match.!", rsakeyn.Length);
                            return -12;
                        }
                    }
                    Console.WriteLine("开始USB下载....");
                    USBDownLoad usbdl = new USBDownLoad();
                    return usbdl.USBDownloadFile(uboot,udisk, rsakeyn);
                }
            }
            return -13;
        }
    }
}
