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

        static void Main(string[] args)
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
                        return;
                    }

                    if (args.Length >= 3)
                    {
                        rsakeyfile = args[2]; 
                        if(!File.Exists(rsakeyfile))
                        {
                            Console.WriteLine("RSAKEY[{0}]不存在!", rsakeyfile);
                            return;
                        }
                        rsakeyn = readrsakeydata(rsakeyfile);
                        if(rsakeyn.Length != 256)
                        {
                            Console.WriteLine("rsakey data length[{0}] not match.!", rsakeyn.Length);
                            return;
                        }
                    }
                    Console.WriteLine("开始USB下载....");
                    USBDownLoad usbdl = new USBDownLoad();
                    usbdl.USBDownloadFile(uboot,udisk, rsakeyn);
                }
            }
            //UsbDriverTest("CY21BootLoaderv0.2.1.bin");
            //UsbDriverTest("test.bin");
            //downloadFile("test.bin");
            //USBDownLoad usbdl = new USBDownLoad();
            //usbdl.USBDownloadFile("CY20BootLoaderv0.2.6.bin", "H");
            //usbdl.USBDownloadFile("CY20P-1TWCBootLoaderv0.3.3.sig", "H");
            //string strrsa = "CA9CB139002106582CD4593215C8CED6C9DDEDC2F2825A00EDA7D69C128B12C31DC1F79DBBFFB49BF8ED0335BCE12A96590BBC164A5782B6E8EE268F0028962AD0598FAD5D83A9967EEAD6B2A36A3E4E0A186F73A007C689092E8E5A0BC112EE8F8AB0145CB6628C025DF5509FF92E848886F5E9B7AB3C01C971213A702EF56097A3D5792A3DBC4421A1199CF237FB924FB8179BBEFF4249A74060F54E841A3E3A48CB7CEF4B8774A5CC43163FE907252D425877F0208B91F8C4D6AA31986F882621B76D4AE8E50D52BDD5781D9A5C9A0F16429A1DFB7F759E1DA459AD357E9503DC83D99AC75AFE30357A42CF03E0455C039FF7FB23B233F5ADCECEC12F1A65";
            //byte[] rsakey = StringToByteArray(strrsa);

            //usbdl.injectRSApublickey(rsakey);
            //Console.ReadLine();
        }
    }
}
