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

    class Program
    {
        static void PrintBuffer(byte[] buf, int start, int count)
        {
            for (int i = start; i < count; i++)
            {
                if ((i % 16) == 0)
                    Console.WriteLine("");

                Console.Write(string.Format("{0:X2} ", buf[i]));
            }
                
                    
        }

        static void UsbDriverTest()
        {
            UsbScsiDevice usb = new UsbScsiDevice();

            try
            {
                Console.WriteLine($"Connect device: {usb.Connect("H")}");

                Console.WriteLine("start read Inquiry");
                if (usb.Execute(ScsiCommandCode.Inquiry))
                {
                    byte[] po = usb.Inquiry.Sptw.GetDataBuffer();

                    Console.WriteLine("");
                    Console.Write("Inquiry: ");
                    for (int i = 0; i < 32; i++)
                        Console.Write(string.Format("{0:X} ", po[i]));
                }
                else
                    Console.WriteLine($"Bad command: {Marshal.GetLastWin32Error()}");
                /*
                if (usb.Execute(ScsiCommandCode.ReadCapacity))
                {
                    ReadCapacity rc10 = usb.ReadCapacity;
                    UInt32 cntSector = rc10.CountSectors();
                    UInt32 sizeSector = rc10.SizeSector();
                    Console.WriteLine($"C: {cntSector}, S: {sizeSector}");

                    UInt32 mb = (UInt32)(rc10.Capacity() / (1024 * 1024));
                    Console.WriteLine($"Sectors: {cntSector} [{mb}] MB");
                    Console.Write("ReadCapacity10: ");
                    PrintBuffer(rc10.Sptw.GetDataBuffer(0, 8), 0, 8);
                }
                else
                    Console.WriteLine($"Bad command: {Marshal.GetLastWin32Error()}");
                */
#if false
                usb.Read10.SetBounds(0, 64);
                usb.Read10.Sptw.SetCdb(new byte[]
                {
                    (byte) ScsiCommandCode.Read10, 0,
                    0, 0, 0, 64,
                    0,
                    0, 64,
                    0
                });
                usb.Execute(ScsiCommandCode.Read10);
                var data = usb.Read10.Sptw.GetDataBuffer();
                Console.WriteLine("");
                Console.WriteLine("Data: ");
                PrintBuffer(data, 0, 128);
#else
#if true
                byte[] fwheader = usb.Read();
                if (fwheader.Length == 0) {
                    return;
                }
                Console.WriteLine("Get Info Success:\n");
                PrintBuffer(fwheader, 0, fwheader.Length);

                //send 00
                byte[] array00 = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00};
                //byte[] response0 = new byte[] { };
                byte[] response0;
                if (!usb.Write(array00, 16, out response0)) {
                    return;
                }
                Console.WriteLine("Get Response00 length = {0}\n",response0.Length);
                PrintBuffer(response0, 0, response0.Length);

                //MH1903 Step 2,write 30

                byte[] array = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00, 0x02, 0x30, 0x00, 0x00, 0x0d, 0xac, 0, 0, 0, 0, 0, 0, 0, 0 ,0,0};

                if (!usb.Write(array,(UInt32)array.Length))
                {
                    Console.WriteLine("write step 2 error");
                    return;
                }
                Console.WriteLine("write step 2 OK");

                return;
#endif




                // offset in flash (use for GRUB)
                UInt32 startByteAddress = 0x7E00;
                UInt32 readLba = startByteAddress / 512;

                void UsbToFile(string file, UInt32 lba, UInt32 sectors)
                {
                    byte[] readData = usb.Read(lba, sectors);
                    System.IO.File.WriteAllBytes(file, readData);
                }

                UsbToFile("test.bin", readLba, 256);

                Console.WriteLine("press any key to continue...");
                Console.ReadKey();

                // Write 0x1A00
                UInt32 writeBytesAddress = startByteAddress + 0x1A00;
                UInt32 writeLba = writeBytesAddress / 512;

                byte[] writeData = new byte[512];
                for(int i = 0; i < 512; i++)
                {
                    writeData[i] = (byte)(i + 16);
                }

                Console.WriteLine("press any key to continue...");
                Console.ReadKey();
                usb.Write(writeLba, 1, writeData);

                // Read reply
                UsbToFile("test1.bin", readLba, 256);
#endif

            }
            catch(Exception e)
            {
                Console.WriteLine($"Exception: {e.Message}");
                usb.Disconnect();
            }
        }

        static void Main(string[] args)
        {
            UsbDriverTest();

            Console.ReadLine();
        }
    }
}
