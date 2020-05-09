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
        static UsbScsiDevice usb;
        public Program()
        {
            
        }

        static void PrintBuffer(byte[] buf, int start, int count)
        {
            for (int i = start; i < count; i++)
            {
                if ((i % 16) == 0)
                    Console.WriteLine("");

                Console.Write(string.Format("{0:X2} ", buf[i]));
            }
                
                    
        }

        private static void UsbDriverTest(String filename)
        {
            
            try
            {
                usb = new UsbScsiDevice();
                if(false){
                    byte[] data = new byte[1024];
                    usb.Write(data,(Int32)0x08000000);
                    return;
                }
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
#if true
                byte[] fwheader = usb.Read();
                if (fwheader.Length == 0) {
                    return;
                }
                Console.WriteLine("Get Info Success:\n");
                PrintBuffer(fwheader, 0, fwheader.Length);

                //send 00
                byte[] array00 = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00};
                byte[] response0;
                if (!usb.Write(array00, 16, out response0)) {
                    return;
                }
                Console.WriteLine("Get Response00 length = {0}\n",response0.Length);
                PrintBuffer(response0, 0, response0.Length);

                //MH1903 Step 2,write 30
                if (handleshake() != 0)
                {
                    Console.WriteLine("handleshake error\n");
                    return;
                }
                Console.WriteLine("handleshake ok\n");

                //read flashID
                UInt32 flashID = 0;
                if(readFlashID(out flashID) != 0)
                {
                    Console.WriteLine("read flashID error\n");
                    return;
                }
                Console.WriteLine("flashID:{0:X}\n",flashID);

                //check flash id is right or not.

                //write flash otp paras
                if (writeFlashParas(flashID) != 0)
                {
                    Console.WriteLine("writeFlashParas error\n");
                    return;
                }
                Console.WriteLine("writeFlashParas ok\n");
                return;

                //start step 3
                Console.WriteLine("erase all flash\n");
                if (writefirmwarehead(filename) != 0)
                {
                    Console.WriteLine("writefirmwarehead error\n");
                    return;
                }
                Console.WriteLine("writefirmwarehead OK\n");

                //step 4 erase flash
                Console.WriteLine("erase all flash\n");
                if (eraseflash() != 0)
                {
                    Console.WriteLine("erase all flash error\n");
                    return;
                }
                Console.WriteLine("erase all flash OK\n");

                //step4 download files
                Console.WriteLine("downloadFile\n");
                if (downloadFile(filename) != 0)
                {
                    Console.WriteLine("downloadFile error\n");
                    return;
                }
                Console.WriteLine("downloadFile OK\n");

#endif

            }
            catch(Exception e)
            {
                Console.WriteLine($"Exception: {e.Message}");
                usb.Disconnect();
            }
        }

        private static byte[] Int32ToBytes(uint number)
        {
            //lsb
            byte[] bs = new byte[4];
            byte a = (byte)(number >> 24);
            byte b = (byte)((number & 0xff0000) >> 16);
            byte c = (byte)((number & 0xff00) >> 8);
            byte d = (byte)(number & 0xff);
            bs[0] = d;
            bs[1] = c;
            bs[2] = b;
            bs[3] = a;
            return bs;
        }

        private static int executecmd(byte cmd, byte[]cmddata)
        {
            
            byte[] arrayhead = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00 };
            byte[] cmdpkt = packetdata(cmd, cmddata);
            byte[] usbpkt = new byte[arrayhead.Length + cmdpkt.Length];
            Array.Copy(arrayhead, 0, usbpkt, 0, arrayhead.Length);
            Array.Copy(cmdpkt, 0, usbpkt, arrayhead.Length, cmdpkt.Length);

            string msg = PrintByteArray(usbpkt);
            Console.WriteLine(msg);
            if (!usb.Write(usbpkt, (UInt32)usbpkt.Length))
            {
                Console.WriteLine("write usbpkt error");
                return -1;
            }
            Console.WriteLine("write CMD{0:X} OK\n",cmd);
            return 0;
        }

        private static int executecmd(byte cmd, byte[] cmddata, UInt32 timeout)
        {

            byte[] arrayhead = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00 };
            byte[] cmdpkt = packetdata(cmd, cmddata);
            byte[] usbpkt = new byte[arrayhead.Length + cmdpkt.Length];
            Array.Copy(arrayhead, 0, usbpkt, 0, arrayhead.Length);
            Array.Copy(cmdpkt, 0, usbpkt, arrayhead.Length, cmdpkt.Length);

            string msg = PrintByteArray(usbpkt);
            Console.WriteLine(msg);
            if ( !usb.Write(usbpkt, (UInt32)usbpkt.Length, timeout) )
            {
                Console.WriteLine("write usbpkt error");
                return -1;
            }
            Console.WriteLine("write CMD:{0:X} OK\n", cmd);
            return 0;
        }

        private static int executecmd21(ushort datalen, ushort crc16, UInt32 timeout)
        {

            byte[] arrayhead = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00 };
            byte[] cmdpkt = new byte[6];//packetdata((byte)0x21, cmddata);
            byte[] usbpkt = new byte[arrayhead.Length + cmdpkt.Length];

            cmdpkt[0] = 0x02;
            cmdpkt[1] = 0x21;
            cmdpkt[2] = (byte)(datalen&0xff);
            cmdpkt[3] = (byte)((datalen>>8) & 0xff);

            cmdpkt[4] = (byte)(crc16 & 0xff);
            cmdpkt[5] = (byte)((crc16 >> 8) & 0xff);

            Array.Copy(arrayhead, 0, usbpkt, 0, arrayhead.Length);
            Array.Copy(cmdpkt, 0, usbpkt, arrayhead.Length, cmdpkt.Length);

            string msg = PrintByteArray(usbpkt);
            Console.WriteLine(msg);
            if (!usb.Write(usbpkt, (UInt32)usbpkt.Length,1000))
            {
                Console.WriteLine("write usbpkt error");
                return -1;
            }
            Console.WriteLine("write CMD: 21 OK\n");
            return 0;
        }

        private static int handleshake()
        {
            byte[] data = new byte[0];
            if (executecmd((byte)0x30, data) != 0)
            {
                return -1;
            }
            return 0;
        }

        private static int readFlashID(out UInt32 flashID)
        {
            byte[] arrayhead = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00 };
            byte[] cmdpkt = packetdata((byte)0x23, new byte[0]);
            byte[] usbpkt = new byte[arrayhead.Length + cmdpkt.Length];
            Array.Copy(arrayhead, 0, usbpkt, 0, arrayhead.Length);
            Array.Copy(cmdpkt, 0, usbpkt, arrayhead.Length, cmdpkt.Length);

            string msg = PrintByteArray(usbpkt);
            Console.WriteLine(msg);

            byte[] flashid;
            flashID = 0;
            if (!usb.Write(usbpkt, (UInt32)usbpkt.Length,out flashid))
            {
                Console.WriteLine("write usbpkt error");
                return -1;
            }
            
            flashID = (UInt32)(flashid[4] | (flashid[5]<<8) + (flashid[6]<<16));
            Console.WriteLine("Get Response length = {0} 0X{1:X}\n", flashid.Length,flashID);
            PrintBuffer(flashid, 0, flashid.Length);
            return 0;
        }

        private static int writeFlashParas(UInt32 flashID)
        {
            UInt32[] flashID_support = new UInt32[] {
                0x684017
            };
            UInt32[,] flashID_support_paras = new UInt32[2,9]{
                { 0,0x309F,0x27031,0x404277,0xD132,0x9020,0xC7,0xAB,0xb2EB},
                { 0,2,3,4,5,6,7,8,9}
            };
            int idx = -1;
            for(int i = 0;i< flashID_support.Length; i++)
            {
                if( flashID == flashID_support[i])
                {
                    idx = i;
                    break;
                }
            }

            if(idx < 0)
            {
                Console.WriteLine("Error:unsupport flash id\n");
                return -1;
            }
            Console.WriteLine("idx = {0} ", idx);
            UInt32[] paras = new UInt32[9];
            byte[] cmddata = new byte[36];
            for(int i = 0; i < 9; i++)
            {
                paras[i] = flashID_support_paras[idx,i];
                //Console.WriteLine("{0:X} ", paras[i]);
            }

            for(int i=0;i<paras.Length;i++)
            {
                Array.Copy(BitConverter.GetBytes(paras[i]),0,cmddata,i*4,4);
            }

            PrintBuffer(cmddata,0,cmddata.Length);
            
            if (executecmd((byte)0x18, cmddata) != 0)
            {
                Console.WriteLine("Error: wrtie flash paras to flash");
                return -2;
            }
            return 0;
        }

        private static int writefirmwarehead(string filename)
        {
            Console.WriteLine("Start step 3,send firmware head\n");
            byte[] fmheadarray = new byte[92];
            //add fixed head
            int oft = 0;

            //0x5555aaaa
            Array.Copy(new byte[] { 0xaa, 0xaa, 0x55, 0x55, 0, 0, 0, 0 }, 0, fmheadarray, oft, 8);
            oft += 8;

            //firmware start address ,fixed to 0x01001000
            Array.Copy(new byte[] { 0x00, 0x10, 0x00, 0x01 }, 0, fmheadarray, oft, 4);
            oft += 4;

            //firmware size
            long filesize = GetFileSize(filename);
            if (filesize == 0)
            {
                Console.WriteLine("error:file size is %d\n", filesize);
                return -2;
            }
            fmheadarray[oft] = (byte)(filesize & 0xff);
            fmheadarray[oft + 1] = (byte)(filesize >> 8 & 0xff);
            fmheadarray[oft + 2] = (byte)(filesize >> 16 & 0xff);
            fmheadarray[oft + 3] = (byte)(filesize >> 24 & 0xff);
            oft += 4;


            //firmware ver and sha256 option
            Array.Copy(new byte[] { 0xff, 0xff, 0x00, 0x00, 2, 0, 0, 0 }, 0, fmheadarray, oft, 8);
            oft += 8;

            //firmware sha256 valuse
            byte[] sha256 = SHA256File(filename);
            Array.Copy(sha256, 0, fmheadarray, oft, sha256.Length);
            oft += sha256.Length;

            oft += 32;

            //crc32 values
            int crc32 = GetCRC32(fmheadarray, 4, oft - 4);
            fmheadarray[oft] = (byte)(crc32 & 0xff);
            fmheadarray[oft + 1] = (byte)(crc32 >> 8 & 0xff);
            fmheadarray[oft + 2] = (byte)(crc32 >> 16 & 0xff);
            fmheadarray[oft + 3] = (byte)(crc32 >> 24 & 0xff);
            oft += 4;
            Console.WriteLine("Crc32={0} {1}\n", crc32, oft);

            if ( executecmd((byte)0x20, fmheadarray) != 0 )
            {
                return -1;
            }
            return 0;
        }

        private static int eraseflash(UInt32 startaddress,UInt32 sectors, UInt32 timeout)
        {
            UInt32 erasestartaddr = startaddress - 0x1000000;
            byte[] buf = new byte[12];
            Array.Copy(Int32ToBytes(erasestartaddr),0,buf,0,4);
            Array.Copy(Int32ToBytes(sectors), 0, buf, 4, 4);
            Array.Copy(Int32ToBytes(0x1000), 0, buf, 8, 4);

            if (executecmd((byte)0x22, buf, timeout) != 0)
            {
                Console.WriteLine("Erase flash error\n");
                return -1;
            }
            Console.WriteLine("Erase flash ok\n");
            return 0;
        }

        private static int eraseflash()
        {
            /*if (eraseflash((UInt32)0x1000000, (UInt32)1, 260) != 0)
            {
                Console.WriteLine("erase flash 1 error\n");
                return -1;
            }*/

            if (eraseflash((UInt32)0x1000000, (UInt32)0xFFFFFFFF, 20000) != 0)
            {
                Console.WriteLine("erase all flash error\n");
                return -2;
            }

            //clean data area
            if (!usb.ExecuteNullCommand()) {
                Console.WriteLine("Error:ExecuteNullCommand!\n");
                return -3;
            }
            return 0;
        }

        private static int writefirmwaredata(UInt32 addr, int size)
        {
            int oft = 0;
            return 0;
        }
        private static int writefirmwarecmd(UInt16 size, UInt16 crc16)
        {
            var sizebytes = BitConverter.GetBytes(size).Reverse<byte>().ToArray();
            var crc16bytes = BitConverter.GetBytes(crc16).Reverse<byte>().ToArray();
            byte[] data = new byte[4];
            Array.Copy(sizebytes, 0, data, 0, 2);
            Array.Copy(crc16bytes, 0, data, 2, 2);
            PrintBuffer(data, 0, data.Length);
            if (executecmd21(size,crc16, 3000) != 0)
            {
                return -1;
            }
            return 0;
        }

        private static int cleandata()
        {
            if (!usb.ExecuteNullCommand())
            {
                Console.WriteLine("Error : ExecuteNullCommand");
                return -1;
            }
            return 0;
        }

        private static int downloadFile(string filename)
        {
            try
            {
                System.IO.FileStream fileStream = System.IO.File.OpenRead(filename);
                int oft=0,rlen = 0;
                int ret = -1;
                int onelen = 0x8000;
                UInt32 addr = 0x08000000;
                UInt32 dladdr = 0x1001000;
                byte[] data = new byte[onelen + 4];
                byte[] alldata = new byte[data.Length + 4];
                ushort crc16;

                while (true)
                {
                    rlen = fileStream.Read(data, 4, onelen);
                    if (rlen > 0)
                    {
                        //fixed the download address 0x1001000
                        Array.Copy(BitConverter.GetBytes(dladdr + oft), 0, data, 0, 4);
                        if (usb.Write(data, rlen + 4, (Int32)0x80000) != true)
                        {
                            Console.WriteLine("Error: writefirmwardata\n");
                            ret = -2;
                            break;
                        }

                        Array.Copy(new byte[] { 2,0x21,0x0,0x0}, 0, alldata, 0, 4);
                        Array.Copy(data, 0, alldata, 4, rlen + 4);
                        alldata[2] = (byte)((rlen+4) & 0xff);
                        alldata[3] = (byte)(((rlen + 4)>>8) & 0xff);
                        crc16 = CRC16(alldata, rlen + 8);
                        //crc16 = CRC16(alldata, alldata.Length);
                        //PrintBuffer(alldata, 4, 16);
                        Console.WriteLine("crc16: {0:X} {1:X}\n",crc16,rlen);
                        //send 21 cmd
                        if (writefirmwarecmd((ushort)(rlen + 4),crc16) != 0)
                        {
                            Console.WriteLine("Error: writefirmwarecmd\n");
                            return -3;
                        }

                        //update oft
                        oft += rlen;
                        if (oft == fileStream.Length)
                        {
                            Console.WriteLine("EOT\n");
                            ret = 0;
                            break;
                        }
                        else if (oft > fileStream.Length) {
                            Console.WriteLine("error:oft {0} > {1}\n",oft, fileStream.Length);
                            ret = -3;
                            break;
                        }
                    }
                    else {
                        ret = -4;
                        break;
                    }
                }

                fileStream.Close();
                return ret;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e.Message}");
            }
            return -1;
        }

        private static string PrintByteArray(byte[] array)
        {
            StringBuilder sb = new StringBuilder();
            int i;
            for (i = 0; i < array.Length; i++)
            {
                sb.Append(String.Format("{0:X2}", array[i]));

            }
            return sb.ToString();
        }

        public static long GetFileSize(string sFullName)
        {
            long lSize = 0;
            if (File.Exists(sFullName))
                lSize = new FileInfo(sFullName).Length;
            return lSize;
        }

        /// <summary>
        /// 计算文件的 SHA256 值
        /// </summary>
        /// <param name="fileStream">文件流</param>
        /// <returns>System.String.</returns>
        public static byte[] SHA256File(string filename)
        {
            System.Security.Cryptography.SHA256 mySHA256 = System.Security.Cryptography.SHA256Managed.Create();
            System.IO.FileStream fileStream = System.IO.File.OpenRead(filename);
            byte[] hashValue;

            // Create a fileStream for the file.
            //FileStream fileStream = fInfo.Open(FileMode.Open);
            // Be sure it's positioned to the beginning of the stream.
            fileStream.Position = 0;
            // Compute the hash of the fileStream.
            hashValue = mySHA256.ComputeHash(fileStream);

            // Close the file.
            fileStream.Close();
            return hashValue;
        }

        static UInt32[] crcTable = {
            0x0, 0x77073096, 0xee0e612c, 0x990951ba, 0x76dc419, 0x706af48f, 0xe963a535, 0x9e6495a3,
            0xedb8832, 0x79dcb8a4, 0xe0d5e91e, 0x97d2d988, 0x9b64c2b, 0x7eb17cbd, 0xe7b82d07, 0x90bf1d91,
            0x1db71064, 0x6ab020f2, 0xf3b97148, 0x84be41de, 0x1adad47d, 0x6ddde4eb, 0xf4d4b551, 0x83d385c7,
            0x136c9856, 0x646ba8c0, 0xfd62f97a, 0x8a65c9ec, 0x14015c4f, 0x63066cd9, 0xfa0f3d63, 0x8d080df5,
            0x3b6e20c8, 0x4c69105e, 0xd56041e4, 0xa2677172, 0x3c03e4d1, 0x4b04d447, 0xd20d85fd, 0xa50ab56b,
            0x35b5a8fa, 0x42b2986c, 0xdbbbc9d6, 0xacbcf940, 0x32d86ce3, 0x45df5c75, 0xdcd60dcf, 0xabd13d59,
            0x26d930ac, 0x51de003a, 0xc8d75180, 0xbfd06116, 0x21b4f4b5, 0x56b3c423, 0xcfba9599, 0xb8bda50f,
            0x2802b89e, 0x5f058808, 0xc60cd9b2, 0xb10be924, 0x2f6f7c87, 0x58684c11, 0xc1611dab, 0xb6662d3d,
            0x76dc4190, 0x1db7106, 0x98d220bc, 0xefd5102a, 0x71b18589, 0x6b6b51f, 0x9fbfe4a5, 0xe8b8d433,
            0x7807c9a2, 0xf00f934, 0x9609a88e, 0xe10e9818, 0x7f6a0dbb, 0x86d3d2d, 0x91646c97, 0xe6635c01,
            0x6b6b51f4, 0x1c6c6162, 0x856530d8, 0xf262004e, 0x6c0695ed, 0x1b01a57b, 0x8208f4c1, 0xf50fc457,
            0x65b0d9c6, 0x12b7e950, 0x8bbeb8ea, 0xfcb9887c, 0x62dd1ddf, 0x15da2d49, 0x8cd37cf3, 0xfbd44c65,
            0x4db26158, 0x3ab551ce, 0xa3bc0074, 0xd4bb30e2, 0x4adfa541, 0x3dd895d7, 0xa4d1c46d, 0xd3d6f4fb,
            0x4369e96a, 0x346ed9fc, 0xad678846, 0xda60b8d0, 0x44042d73, 0x33031de5, 0xaa0a4c5f, 0xdd0d7cc9,
            0x5005713c, 0x270241aa, 0xbe0b1010, 0xc90c2086, 0x5768b525, 0x206f85b3, 0xb966d409, 0xce61e49f,
            0x5edef90e, 0x29d9c998, 0xb0d09822, 0xc7d7a8b4, 0x59b33d17, 0x2eb40d81, 0xb7bd5c3b, 0xc0ba6cad,
            0xedb88320, 0x9abfb3b6, 0x3b6e20c, 0x74b1d29a, 0xead54739, 0x9dd277af, 0x4db2615, 0x73dc1683,
            0xe3630b12, 0x94643b84, 0xd6d6a3e, 0x7a6a5aa8, 0xe40ecf0b, 0x9309ff9d, 0xa00ae27, 0x7d079eb1,
            0xf00f9344, 0x8708a3d2, 0x1e01f268, 0x6906c2fe, 0xf762575d, 0x806567cb, 0x196c3671, 0x6e6b06e7,
            0xfed41b76, 0x89d32be0, 0x10da7a5a, 0x67dd4acc, 0xf9b9df6f, 0x8ebeeff9, 0x17b7be43, 0x60b08ed5,
            0xd6d6a3e8, 0xa1d1937e, 0x38d8c2c4, 0x4fdff252, 0xd1bb67f1, 0xa6bc5767, 0x3fb506dd, 0x48b2364b,
            0xd80d2bda, 0xaf0a1b4c, 0x36034af6, 0x41047a60, 0xdf60efc3, 0xa867df55, 0x316e8eef, 0x4669be79,
            0xcb61b38c, 0xbc66831a, 0x256fd2a0, 0x5268e236, 0xcc0c7795, 0xbb0b4703, 0x220216b9, 0x5505262f,
            0xc5ba3bbe, 0xb2bd0b28, 0x2bb45a92, 0x5cb36a04, 0xc2d7ffa7, 0xb5d0cf31, 0x2cd99e8b, 0x5bdeae1d,
            0x9b64c2b0, 0xec63f226, 0x756aa39c, 0x26d930a, 0x9c0906a9, 0xeb0e363f, 0x72076785, 0x5005713,
            0x95bf4a82, 0xe2b87a14, 0x7bb12bae, 0xcb61b38, 0x92d28e9b, 0xe5d5be0d, 0x7cdcefb7, 0xbdbdf21,
            0x86d3d2d4, 0xf1d4e242, 0x68ddb3f8, 0x1fda836e, 0x81be16cd, 0xf6b9265b, 0x6fb077e1, 0x18b74777,
            0x88085ae6, 0xff0f6a70, 0x66063bca, 0x11010b5c, 0x8f659eff, 0xf862ae69, 0x616bffd3, 0x166ccf45,
            0xa00ae278, 0xd70dd2ee, 0x4e048354, 0x3903b3c2, 0xa7672661, 0xd06016f7, 0x4969474d, 0x3e6e77db,
            0xaed16a4a, 0xd9d65adc, 0x40df0b66, 0x37d83bf0, 0xa9bcae53, 0xdebb9ec5, 0x47b2cf7f, 0x30b5ffe9,
            0xbdbdf21c, 0xcabac28a, 0x53b39330, 0x24b4a3a6, 0xbad03605, 0xcdd70693, 0x54de5729, 0x23d967bf,
            0xb3667a2e, 0xc4614ab8, 0x5d681b02, 0x2a6f2b94, 0xb40bbe37, 0xc30c8ea1, 0x5a05df1b, 0x2d02ef8d,
            };

        public static int GetCRC32(byte[] bytes,int oft,int length)
        {       
            UInt32 crc = 0xFFFFFFFF;
            for (int i = oft; i < oft + length; i++)
            {
                crc = ((crc >> 8) & 0x00FFFFFF) ^ crcTable[(crc ^ bytes[i]) & 0xFF];
            }
            UInt32 temp = crc ^ 0xFFFFFFFF;
            int t = (int)temp;
            return (t);
        }

        /// CRC校验
        /// </summary>
        /// <param name="data">校验数据</param>
        /// <returns>高低8位</returns>
        public static ushort CRC16(byte[] data, int length)
        {
            ushort wCRC = 0xffff;
            for (int i = 0; i < length; ++i)
            {
                wCRC = (ushort)(wCRC ^ (data[i] << 8));

                for (int j = 0; j < 8; j++)
                {
                    if ((wCRC & 0x8000) != 0)
                        wCRC = (ushort)((wCRC << 1) ^ 0x1021);
                    else
                        wCRC <<= 1;
                }
            }
            return wCRC;
        }

        public static ushort CRC16(byte[] data, int oft,int length)
        {
            ushort wCRC = 0xffff;
            for (int i = oft; i < length + oft; ++i)
            {
                wCRC = (ushort)(wCRC ^ (data[i] << 8));

                for (int j = 0; j < 8; j++)
                {
                    if ((wCRC & 0x8000) != 0)
                        wCRC = (ushort)((wCRC << 1) ^ 0x1021);
                    else
                        wCRC <<= 1;
                }
            }
            return wCRC;
        }

        //append stx lenhth and calc crc16
        private static byte[] packetdata(byte cmdid,byte[] data)
        {
            byte[] frame = new byte[ data.Length + 6];
            short dlen = (short)data.Length;
            frame[0] = 0x02;
            frame[1] = cmdid;
            frame[2] = (byte)(dlen&0xff);
            frame[3] = (byte)(dlen>>8 & 0xff);
            Array.Copy(data, 0, frame, 4, dlen);

            ushort crc16 = CRC16(frame,dlen + 4);
            frame[dlen + 4] = (byte)(crc16 & 0xff);
            frame[dlen + 5] = (byte)(crc16>>8 & 0xff);
            return frame;
        }

        static void Main(string[] args)
        {
            UsbDriverTest("CY21BootLoaderv0.2.1.bin");
            //UsbDriverTest("test.bin");
            //downloadFile("test.bin");
            Console.ReadLine();
        }
    }
}
