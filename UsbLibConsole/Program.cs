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

        /*
         UsbLibConsole.exe CY20P-1TWCBootLoaderv0.3.3.bin H true CY2XBootloader.rsa
         para1: bootloader
         para2: USB Disk
         para3: erase all flash
         para4: rsakey : if exist will update the mcu.
        */
        static int Main(string[] args)
        {
            if (args != null)
            {
                if(args.Length >= 3)
                {
                    string uboot = args[0];
                    string udisk = args[1];
                    bool eraseall = true;
                    string rsakeyfile = null;
                    byte[] rsakeyn = null;
                    if (args[2] == "false")
                    {
                        eraseall = false;
                    }else if(args[2] == "true")
                    {
                        eraseall = true;
                    }else
                    {
                        Console.WriteLine(string.Format("不支持的参数:{0},{1}",args[2],args[2].Length));
                        return -1;
                    }

                    if (!File.Exists(uboot))
                    {
                        Console.WriteLine("bootloader [{0}]不存在!", uboot);
                        return -10;
                    }

                    if (args.Length >= 4)
                    {
                        rsakeyfile = args[3]; 
                        if(!File.Exists(rsakeyfile))
                        {
                            Console.WriteLine("RSAKEY[{0}]不存在!", rsakeyfile);
                            return -11;
                        }
                        rsakeyn = readrsakeydata(rsakeyfile);
                        if(rsakeyn.Length != 256)
                        {
                            Console.WriteLine("rsakey数据长度[{0}]不匹配!", rsakeyn.Length);
                            return -12;
                        }
                    }
                    Console.WriteLine("开始USB下载....");
                    USBDownLoad usbdl = new USBDownLoad();
                    return usbdl.USBDownloadFile(uboot,udisk, eraseall, rsakeyn);
                }
            }
            return -1;
        }
    }
}
