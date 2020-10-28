using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UsbLib
{
    using UsbLib.Usb;
    using UsbLib.Scsi;
    using UsbLib.Scsi.Commands;
    using System.IO;

    /// <summary>
    /// Scsi device class on base Usb Bulk protocol
    /// </summary>
    public class UsbScsiDevice : ScsiDevice
    {
        UsbDriver usb = new UsbDriver();

        public bool Connect(string drive) => this.usb.Connect(drive);

        public void Disconnect() => this.usb.Disconnect();

        private byte[] MH1903Header = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00 };
        private const int MH1903HeaderLen = 16;
        private byte[] ACKPacket = new byte[] { 0x02, 0x80, 0x03, 0, 0x28, 0, 0, 0xB8, 0x84 };

        /// <summary>
        /// Execute scsi command by scsi code
        /// </summary>
        /// <param name="code">scsi command code</param>
        /// <returns>result operation</returns>
        public bool Execute(ScsiCommandCode code) => this.usb.Ioctl(this.Commands[code].Sptw);

        public bool CheckByteArrayEquals(byte[] b1, int b1pos, byte[] b2, int b2pos, int checklen)
        {
            for (int i = 0; i < checklen; i++)
            {
                if (b1[b1pos + i] != b2[b2pos + i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Read data from device
        /// </summary>
        /// <param name="lba">logical block address</param>
        /// <param name="sectors">count read sectors</param>
        /// <returns>read data</returns>
        public byte[] Read(UInt32 lba, UInt32 sectors)
        {
            byte[] data = new byte[sectors * 512];
            
            UInt32 offset = 0;
            
            while (sectors > 0)
            {
                UInt32 transferSectorLength = (sectors >= 64) ? 64 : sectors;
                UInt32 transferBytes = transferSectorLength * 512;

                this.Read10.SetBounds(lba, transferSectorLength);
                this.Execute(ScsiCommandCode.Read10);

                var buf = this.Read10.Sptw.GetDataBuffer();
                Array.Copy(buf, 0, data, offset, transferBytes);

#if false
                this.PrintData(lba, transferSectorLength, buf);
#endif

                lba += transferSectorLength;
                sectors -= transferSectorLength;
                offset += transferBytes;
            }

            return data;
        }

        /*read response */
        public byte[] Read() {
            byte []response = new byte[] { };
            if (!this.Execute(ScsiCommandCode.Read10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return response;
            }
            var recdata = this.Read10.Sptw.GetDataBuffer();
            int reslen = BitConverter.ToUInt16(recdata, 16 + 2);
            Console.WriteLine("read response length {0}", reslen);

            if (!CheckByteArrayEquals(recdata, 0, MH1903Header, 0, MH1903Header.Length)){
                Console.WriteLine("Error MH1903 Header: 0x{0:X8}", this.usb.GetError());
                return response;
            }

            response = new byte[reslen + 6];
            Array.Copy(recdata, 16, response, 0, reslen + 4 + 2);
            return response;
        }

        /*check the ack packet*/
        private bool checkACK(byte[]response) {
            return CheckByteArrayEquals(response, 0, ACKPacket, 0, ACKPacket.Length);
        }

        //first command read device info
        public byte[] ReadDeviceInfo()
        {
            return Read();
        }

        private const byte CMD_USBSTART = 0x30;
        /*send 0x30 cmd*/
        public bool StartUSBConnect()
        {
            byte[] cmdpacket = packetCommand(CMD_USBSTART,new byte[] { });
            byte[] response = ExecuteCommand(cmdpacket);
            return checkACK(response);
        }

        /// <summary>
        /// Write data in device
        /// </summary>
        /// <param name="lba">logical block address</param>
        /// <param name="sectors">count read sectors</param>
        /// <param name="data">writing data (alignment by 512 bytes)</param>
        public void Write(UInt32 lba, UInt32 sectors, byte[] data)
        {
            UInt32 offset = 0;
            var buf = this.Write10.Sptw.GetDataBuffer();

            while(sectors > 0)
            {
                UInt32 transferSectorLength = (sectors >= 64) ? 64 : sectors;
                UInt32 transferBytes = transferSectorLength * 512;

                Array.Copy(data, offset, buf, 0, transferBytes);
                this.Write10.SetBounds(lba, transferSectorLength);
                if (!this.Execute(ScsiCommandCode.Write10))
                    Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());

                lba += transferSectorLength;
                sectors -= transferSectorLength;
                offset += transferBytes;
            }
        }

        //计算CRC16
        private byte[] CRC16_C(byte[] data, int datalen)
        {
            byte CRC16Lo;
            byte CRC16Hi;           //CRC寄存器 
            byte CL; byte CH;       //多项式码 ccitt的多项式是x16+x12+x5+1,多项式码是0x1021,但由于ccitt是默认先传LSB而不是MSB，故这里应该将多项式码按bit反转得到0x8408
            byte SaveHi; byte SaveLo;
            byte[] tmpData;
            int Flag;
            CRC16Lo = 0xFF;
            CRC16Hi = 0xFF;
            CL = 0x08;
            CH = 0x84;
            tmpData = data;
            for (int i = 0; i < datalen; i++)
            {
                CRC16Lo = (byte)(CRC16Lo ^ tmpData[i]); //每一个数据与CRC寄存器进行异或 
                for (Flag = 0; Flag <= 7; Flag++)
                {
                    SaveHi = CRC16Hi;
                    SaveLo = CRC16Lo;
                    CRC16Hi = (byte)(CRC16Hi >> 1);      //高位右移一位 
                    CRC16Lo = (byte)(CRC16Lo >> 1);      //低位右移一位 
                    if ((SaveHi & 0x01) == 0x01) //如果高位字节最后一位为1 
                    {
                        CRC16Lo = (byte)(CRC16Lo | 0x80);   //则低位字节右移后前面补1 
                    }             //否则自动补0 
                    if ((SaveLo & 0x01) == 0x01) //如果LSB为1，则与多项式码进行异或 
                    {
                        CRC16Hi = (byte)(CRC16Hi ^ CH);
                        CRC16Lo = (byte)(CRC16Lo ^ CL);
                    }
                }
            }
            byte[] ReturnData = new byte[2];
            ReturnData[0] = CRC16Hi;       //CRC高位 
            ReturnData[1] = CRC16Lo;       //CRC低位 
            return ReturnData;
        }

        private const byte SYNC = 0x02;

        /*packet cmd and cmddata and calc the crc16*/
        private byte[] packetCommand(byte cmd,byte[]cmddata)
        {
            ushort datalen = (ushort)cmddata.Length;
            byte[] cmdpacket = new byte[datalen + 6];
            cmdpacket[0] = SYNC;
            cmdpacket[1] = cmd;
            //data length
            Array.Copy(BitConverter.GetBytes(datalen), 0, cmdpacket, 2, 2);
            //data
            if(datalen > 0)
            {
                Array.Copy(cmddata, 0, cmdpacket, 4, datalen);
            }

            //calc crc16
            byte[] crc16 = CRC16_C(cmdpacket,4+datalen);
            Array.Copy(crc16, 0, cmdpacket, 4+datalen, 2);
            return cmdpacket;
        }

        /*send 2a with all zero data and get response data*/
        public bool ExecuteNullCommand()
        {
            byte[] zerodata = new byte[256]; //{ 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};
            byte[] buf = this.Write10.Sptw.GetDataBuffer();

            this.Write10.SetBounds(0x040000, 1);

            Array.Copy(MH1903Header, 0, zerodata, 0, MH1903HeaderLen);
            Array.Copy(zerodata, 0, buf, 0, zerodata.Length);
            //1.send cmd and data
            if (!this.Execute(ScsiCommandCode.Write10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return false;
            }

            //2.read data
            if (!this.Execute(ScsiCommandCode.Read10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return false;
            }
            byte[] recdata = this.Read10.Sptw.GetDataBuffer();

            if (!CheckByteArrayEquals(recdata, 0, zerodata, 0, zerodata.Length))
            {
                Console.WriteLine("Error: reclen:{0}", recdata.Length);
                return false;
            }
            return true;
        }

        /*execute cmd and get response*/
        public byte[] ExecuteCommand(byte[] cmdpacket) {
            byte[] response = new byte[] { };
            //0.send null command for clean cmd area.
            if (!ExecuteNullCommand())
            {
                Console.WriteLine("Error : ExecuteNullCommand");
                return response;
            }

            //1.send command
            var buf = this.Write10.Sptw.GetDataBuffer();

            Array.Copy(cmdpacket, 0, buf, 0, cmdpacket.Length);
            if (!this.Execute(ScsiCommandCode.Write10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return response;
            }

            ////2.read response   read timeout is 10*10
            ushort reslen = 0;
            byte[] recdata = null;
            for (int i = 0; i < 10; i++)
            {
                if (!this.Execute(ScsiCommandCode.Read10))
                {
                    Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                    return response;
                }
                recdata = this.Read10.Sptw.GetDataBuffer();
                reslen = BitConverter.ToUInt16(recdata, MH1903HeaderLen + 2);
                Console.WriteLine("read response length {0}", reslen);
                if (reslen == 0)
                    continue;
                if (!CheckByteArrayEquals(recdata, MH1903HeaderLen, ACKPacket, 0, ACKPacket.Length))
                {
                    for (int ch = 0; ch < 8; ch++)
                    {
                        Console.Write("{0:X}", recdata[MH1903HeaderLen + ch]);
                    }
                }
                else
                {
                    Console.WriteLine("ExecuteCommand Got ACK OK");
                    break;
                }
                System.Threading.Thread.Sleep(10);
            }
            
            //check response,SYNC CMD + LEN0 + LEN1 + DATA CRC0 CRC1
            if(reslen > 0)
            {
                response = new byte[reslen + 6];
                Array.Copy(recdata, MH1903HeaderLen,response,0,reslen + 6);
            }
            return response;
        }

        /* write cmd and read ack*/
        public bool Write(byte[] data, UInt32 datalen)
        {
            /*if (!ExecuteNullCommand()) {
                Console.WriteLine("Error: ExecuteNullCommand\n");
                return false;
            }*/
            var buf = this.Write10.Sptw.GetDataBuffer();

            Array.Copy(data, 0, buf, 0, datalen);
            if (!this.Execute(ScsiCommandCode.Write10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return false;
            }
            //read timeout is 10*10
            for (int i = 0; i < 3; i++)
            {
                if (!this.Execute(ScsiCommandCode.Read10))
                {
                    Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                    return false;
                }
                var recdata = this.Read10.Sptw.GetDataBuffer();
                int reslen = BitConverter.ToUInt16(recdata, 16 + 2);
                Console.WriteLine("read response length {0}",reslen);
                if (!CheckByteArrayEquals(recdata, 16, new byte[] { 0x02, 0x80, 0x03, 0 ,0x28,0,0,0xB8,0x84}, 0, 9))
                {
                    for (int ch = 0; ch < reslen + 4; ch++)
                    {
                        Console.Write("{0:X2} ", recdata[16 + ch]);
                        if (ch > 9) break;
                    }
                    Console.Write("\n");

                    if (CheckByteArrayEquals(recdata, 16, new byte[] { 0x02, 0x80, 0x03, 0, 0x29}, 0, 5))
                    {
                        Console.Write("Get NACK\n");
                        return false;
                    }
                }
                else {
                    Console.WriteLine("WriteCMD Got ACK OK");
                    return true;
                }
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        public bool Write(byte[] data, UInt32 datalen, UInt32 timeout)
        {
            if (!ExecuteNullCommand())
            {
                Console.WriteLine("Error : ExecuteNullCommand");
                //return false;
            }

            var buf = this.Write10.Sptw.GetDataBuffer();

            this.Write10.SetBounds(0x040000, 1);

            Array.Copy(data, 0, buf, 0, datalen);
            if (!this.Execute(ScsiCommandCode.Write10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return false;
            }
            //read timeout is 10*10
            for (int i = 0; i < timeout/10; i++)
            {
                if (!this.Execute(ScsiCommandCode.Read10))
                {
                    Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                    return false;
                }
                var recdata = this.Read10.Sptw.GetDataBuffer();
                int reslen = BitConverter.ToUInt16(recdata, 16 + 2);
                //Console.WriteLine("read response length {0}", reslen);
                if (!CheckByteArrayEquals(recdata, 16, new byte[] { 0x02, 0x80, 0x03, 0, 0x28, 0, 0, 0xB8, 0x84 }, 0, 9))
                {
                    
                    for (int ch = 0; ch < 8; ch++)
                    {
                        Console.Write("{0:X}", recdata[16 + ch]);
                    }
                    
                    if (recdata[0] == 0x02)
                    {
                        if (recdata[1] == 0x80 || recdata[1] == 0x81 || recdata[1] == 0x82 || recdata[1] == 0x83)
                        {
                            Console.Write("\n get response\n");
                        }
                    }
                    Console.Write(" ");
                }
                else
                {
                    Console.WriteLine("WriteCMD Got ACK OK,take {0}ms\n",i*10);
                    return true;
                }
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        /*send cmd and get response*/
        public bool Write(byte[] data, UInt32 datalen, out byte[] response)
        {
            var buf = this.Write10.Sptw.GetDataBuffer();
            response = new byte[] { };

            Array.Copy(data, 0, buf, 0, datalen);
            if (!this.Execute(ScsiCommandCode.Write10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return false;
            }
            if (!this.Execute(ScsiCommandCode.Read10))
            {
                Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                return false;
            }
            var recdata = this.Read10.Sptw.GetDataBuffer();
            int reslen = BitConverter.ToUInt16(recdata,16 + 2);
            Console.WriteLine("read response length {0}", reslen);

            response = new byte[reslen + 6];
            Array.Copy(recdata, 16, response, 0,reslen + 4 + 2);
            return true;
        }
        //write data to 0x08000000 ~ 0x08020000  //128K
        public bool Write(byte[] data, int dlen,Int32 address)
        {
            UInt32 offset = 0;
            UInt32 addr = (UInt32)address;
            UInt32 blksize = 512;
            
            {
                while (offset < dlen)
                {
                    var buf = this.Write10.Sptw.GetDataBuffer();
                    if (offset + blksize < dlen)
                    {
                        Array.Copy(data, offset, buf, 0, blksize);
                    }
                    else
                    {
                        /*for(int i = 0;i<blksize;i++)
                        {
                            data[i] = 0;
                        }*/
                        byte[] zero = new byte[blksize];
                        Array.Copy(data, offset, buf, 0, dlen - offset);
                        Array.Copy(zero, 0, buf, dlen - offset,  blksize - (dlen - offset) );
                    }
                    this.Write10.SetBounds(addr, 1);
                    //PrintBuffer(this.Write10.Sptw.sptBuffered.Spt.Cdb, 0,10);
                    
                    if (!this.Execute(ScsiCommandCode.Write10))
                    {
                        Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                        return false;
                    }

                    //Console.WriteLine("write oft:{0} bytes to 0x{1:X}\n", offset, addr);
                    addr += blksize/512;
                    offset += blksize;
                }
            }
            return true;
        }

        private void PrintData(UInt32 lba, UInt32 sectors, byte[] data)
        {
            int columns = 16;
            int rows = (int)sectors * 512 / columns;
            for (int row = 0; row < rows; row++)
            {
                Console.Write(string.Format("{0}{1:X8} | ", Environment.NewLine, lba * 512 + row * columns));
                for (int col = 0; col < columns; col++)
                {
                    Console.Write(string.Format("{0:X2} ", data[row * columns + col]));
                }

                Console.Write("| ");

                for (int ch = 0; ch < columns; ch++)
                {
                    char code = (char)data[row * columns + ch];
                    bool isTextNumber = (code > 0x20) && (code < 0x80);
                    Console.Write(isTextNumber ? code : '.');
                }
            }
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
    }

    public class USBDownLoad
    {
        static UsbScsiDevice usb;
        private static string currentStausmsg = "测试";
        private static long s_filesize = 0; //the bootloader size
        public string getStatus()
        {
            return currentStausmsg;
        }

        static void setStatus(string msg)
        {
            currentStausmsg = msg;
            Console.WriteLine(msg);
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

        public int USBDownloadFile(String filename, String usbdisk)
        {

            try
            {
                usb = new UsbScsiDevice();
                if (false)
                {
                    byte[] data = new byte[1024];
                    usb.Write(data, (Int32)0x08000000);
                    return -11;
                }
                setStatus("连接MCU");
                Console.WriteLine($"Connect device: {usb.Connect(usbdisk)}");

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
                    Console.WriteLine($"Bad command: Inquiry");
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
                byte[] fwheader = usb.Read();
                if (fwheader.Length == 0)
                {
                    return -1;
                }
                Console.WriteLine("Get Info Success:\n");
                PrintBuffer(fwheader, 0, fwheader.Length);

                //send 00
                byte[] array00 = new byte[] { 0x4d, 0x48, 0x31, 0x39, 0x30, 0x33, 0x20, 0x52, 0x4f, 0x4d, 0x20, 0x42, 0x4f, 0x4f, 0x54, 0x00 };
                byte[] response0;
                if (!usb.Write(array00, 16, out response0))
                {
                    return -2;
                }
                Console.WriteLine("Get Response00 length = {0}\n", response0.Length);
                PrintBuffer(response0, 0, response0.Length);

                setStatus("开始握手");
                //MH1903 Step 2,write 30
                if (handleshake() != 0)
                {
                    Console.WriteLine("handleshake error\n");
                    return -3;
                }
                Console.WriteLine("handleshake ok\n");

                //read flashID
                setStatus("读取FlashID");
                UInt32 flashID = 0;
                if (readFlashID(out flashID) != 0)
                {
                    Console.WriteLine("read flashID error\n");
                    return -4;
                }
                Console.WriteLine("flashID:{0:X}\n", flashID);

                //check flash id is right or not.
/*
                //write flash otp paras
                setStatus("写入Flash参数至OTP");
                if (writeFlashParas(flashID) != 0)
                {
                    Console.WriteLine("writeFlashParas error\n");
                    return -5;
                }
                Console.WriteLine("writeFlashParas ok\n");
*/

                //start step 3
                if (writefirmwarehead(filename) != 0)
                {
                    Console.WriteLine("writefirmwarehead error\n");
                    return -6;
                }
                Console.WriteLine("writefirmwarehead OK\n");

                //step 4 erase flash
                Console.WriteLine("erase flash\n");
                if (eraseflash(false) != 0)
                {
                    Console.WriteLine("erase flash error\n");
                    return -7;
                }
                Console.WriteLine("erase flash OK\n");

                //step4 download files
                setStatus("开始下载");
                Console.WriteLine("downloadFile\n");
                int ret = downloadFile(filename);
                if ( ret != 0)
                {
                    Console.WriteLine("downloadFile error\n");
                    setStatus(String.Format("下载失败,code={0}",ret));
                    return -8;
                }
                Console.WriteLine("downloadFile OK\n");
                setStatus("下载完毕!");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e.Message}");
                usb.Disconnect();
                return -9;
            }
            
            return 0;
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

        private static int executecmd(byte cmd, byte[] cmddata)
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
            Console.WriteLine("write CMD{0:X} OK\n", cmd);
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
            if (!usb.Write(usbpkt, (UInt32)usbpkt.Length, timeout))
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
            cmdpkt[2] = (byte)(datalen & 0xff);
            cmdpkt[3] = (byte)((datalen >> 8) & 0xff);

            cmdpkt[4] = (byte)(crc16 & 0xff);
            cmdpkt[5] = (byte)((crc16 >> 8) & 0xff);

            Array.Copy(arrayhead, 0, usbpkt, 0, arrayhead.Length);
            Array.Copy(cmdpkt, 0, usbpkt, arrayhead.Length, cmdpkt.Length);

            string msg = PrintByteArray(usbpkt);
            Console.WriteLine(msg);
            if (!usb.Write(usbpkt, (UInt32)usbpkt.Length, 1000))
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
            if (!usb.Write(usbpkt, (UInt32)usbpkt.Length, out flashid))
            {
                Console.WriteLine("write usbpkt error");
                return -1;
            }

            flashID = (UInt32)(flashid[4] | (flashid[5] << 8) + (flashid[6] << 16));
            Console.WriteLine("Get Response length = {0} 0X{1:X}\n", flashid.Length, flashID);
            PrintBuffer(flashid, 0, flashid.Length);
            return 0;
        }

        private static int writeFlashParas(UInt32 flashID)
        {
            UInt32[] flashID_support = new UInt32[] {
                0x684017,
                0x5e6014
            };
            UInt32[,] flashID_support_paras = new UInt32[2, 9]{
                { 0,0x309F,0x27031,0x404277,0xD132,0x9020,0xC7,0xAB,0xb2EB},
                { 0,0x309F,0x27031,0x404277,0xD132,0x9020,0xC7,0xAB,0xb2EB}
            };
            int idx = -1;
            for (int i = 0; i < flashID_support.Length; i++)
            {
                if (flashID == flashID_support[i])
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                Console.WriteLine("Error:unsupport flash id\n");
                return -1;
            }
            Console.WriteLine("idx = {0} ", idx);
            UInt32[] paras = new UInt32[9];
            byte[] cmddata = new byte[36];
            for (int i = 0; i < 9; i++)
            {
                paras[i] = flashID_support_paras[idx, i];
                //Console.WriteLine("{0:X} ", paras[i]);
            }

            for (int i = 0; i < paras.Length; i++)
            {
                Array.Copy(BitConverter.GetBytes(paras[i]), 0, cmddata, i * 4, 4);
            }

            PrintBuffer(cmddata, 0, cmddata.Length);

            if (executecmd((byte)0x18, cmddata) != 0)
            {
                Console.WriteLine("Error: wrtie flash paras to flash");
                //return -2;
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
            s_filesize = filesize;
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

            if (executecmd((byte)0x20, fmheadarray) != 0)
            {
                return -1;
            }
            return 0;
        }

        private static int eraseflash(UInt32 startaddress, UInt32 sectors, UInt32 timeout)
        {
            UInt32 erasestartaddr = startaddress - 0x1000000;
            byte[] buf = new byte[12];
            Array.Copy(Int32ToBytes(erasestartaddr), 0, buf, 0, 4);
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

        private static int eraseflash(bool eraseall)
        {
            if(eraseall == true)
            {
                Console.WriteLine("erase all");
                if (eraseflash((UInt32)0x1000000, (UInt32)0xFFFFFFFF, 20000) != 0)
                {
                    Console.WriteLine("erase all flash error\n");
                    return -1;
                }

                //clean data area
                if (!usb.ExecuteNullCommand())
                {
                    Console.WriteLine("Error:ExecuteNullCommand!\n");
                    return -3;
                }
                return 0;
            }

            int secs = (int)(s_filesize/0x1000);
            if((s_filesize % 0x1000) != 0)
            {
                secs += 1;
            }
            Console.WriteLine("erase flash from 0x1001000 to {0:X8}" , 0x1001000 + secs*0x1000);

            //erase first block
            if (eraseflash((UInt32)0x1000000, (UInt32)1, 260) != 0)
            {
                Console.WriteLine("erase flash 0x1000000 error\n");
                return -1;
            }
            //clean data area
            if (!usb.ExecuteNullCommand())
            {
                Console.WriteLine("Error:ExecuteNullCommand!\n");
                return -3;
            }

            //erase app aera.
            if (eraseflash((UInt32)0x1001000, (UInt32)secs, 20000) != 0)
            {
                Console.WriteLine("erase flash error\n");
                return -2;
            }

            //clean data area
            if (!usb.ExecuteNullCommand())
            {
                Console.WriteLine("Error:ExecuteNullCommand!\n");
                return -3;
            }
            return 0;
        }

        public int injectRSApublickey(byte[]rsakeyn)
        {
            Console.WriteLine("injectRSApublickey");
            byte[] rsapkg = new byte[324];
            int oft = 0;

            //format rsapkg to all 0
            for (int i = 0; i < rsapkg.Length; i++)
            {
                rsapkg[i] = 0;
            }

            //SN
            byte[] sn = new byte[16]{ 0x10,0x80,0x80,0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            Random ra = new Random(unchecked((int)DateTime.Now.Ticks));
            for (int i=0;i<8;i++)
            {
                sn[i + 4] = (byte)(ra.Next(0, 256)&0xff);
            }
            Array.Copy(sn, 0, rsapkg, 0, sn.Length);
            oft += 16;

            //Timeout 100
            rsapkg[oft] = 0x64;
            rsapkg[oft+1] = 0x00;
            oft += 2;

            //Res 0x0000
            oft += 2;

            //KeyIdx 1
            rsapkg[oft] = 0x01;
            oft += 4;

            //E 0x010001
            rsapkg[oft] = 0x00;
            rsapkg[oft + 1] = 0x00;
            rsapkg[oft + 2] = 0x00;
            rsapkg[oft + 3] = 0x00;
            rsapkg[oft + 4] = 0x00;
            rsapkg[oft + 5] = 0x01;
            rsapkg[oft + 6] = 0x00;
            rsapkg[oft + 7] = 0x01;
            oft += 8;

            //N 
            Array.Copy(rsakeyn,0,rsapkg,oft,256);
            oft += 256;

            /*IsEncrypt  
             * none 0x5555 : enable flash enc
             * 0xAAAA: use input key else use random aes key.
             * 
            */
            oft += 4;

            //aesKey ,all 0
            //aesIV ,all 0

            //execute inject cmd
            PrintBuffer(rsapkg, 0, rsapkg.Length);
            return 0;

            if (executecmd((byte)0x12, rsapkg) != 0)
            {
                Console.WriteLine("Error: inject RSA key");
                return -1;
            }
            Console.WriteLine("inject RSA key success.");
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
            if (executecmd21(size, crc16, 3000) != 0)
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
                int oft = 0, rlen = 0;
                int ret = -1;
                int onelen = 0x8000;
                UInt32 addr = 0x08000000;
                UInt32 dladdr = 0x1001000;
                byte[] data = new byte[onelen + 4];
                byte[] alldata = new byte[data.Length + 4];
                ushort crc16;
                long filesize = GetFileSize(filename);

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

                        Array.Copy(new byte[] { 2, 0x21, 0x0, 0x0 }, 0, alldata, 0, 4);
                        Array.Copy(data, 0, alldata, 4, rlen + 4);
                        alldata[2] = (byte)((rlen + 4) & 0xff);
                        alldata[3] = (byte)(((rlen + 4) >> 8) & 0xff);
                        crc16 = CRC16(alldata, rlen + 8);
                        //crc16 = CRC16(alldata, alldata.Length);
                        //PrintBuffer(alldata, 4, 16);
                        Console.WriteLine("crc16: {0:X} {1:X}\n", crc16, rlen);
                        //send 21 cmd
                        if (writefirmwarecmd((ushort)(rlen + 4), crc16) != 0)
                        {
                            Console.WriteLine("Error: writefirmwarecmd\n");
                            return -3;
                        }

                        //update oft
                        oft += rlen;
                        if (oft == fileStream.Length)
                        {
                            Console.WriteLine("EOT\n");
                            setStatus("下载进度:100%");
                            ret = 0;
                            break;
                        }
                        else if (oft > fileStream.Length)
                        {
                            Console.WriteLine("error:oft {0} > {1}\n", oft, fileStream.Length);
                            ret = -3;
                            break;
                        }
                        else {
                            setStatus(String.Format("下载进度:{0}%",oft*100/ filesize));
                        }
                    }
                    else
                    {
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

        public static int GetCRC32(byte[] bytes, int oft, int length)
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

        public static ushort CRC16(byte[] data, int oft, int length)
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
        private static byte[] packetdata(byte cmdid, byte[] data)
        {
            byte[] frame = new byte[data.Length + 6];
            short dlen = (short)data.Length;
            frame[0] = 0x02;
            frame[1] = cmdid;
            frame[2] = (byte)(dlen & 0xff);
            frame[3] = (byte)(dlen >> 8 & 0xff);
            Array.Copy(data, 0, frame, 4, dlen);

            ushort crc16 = CRC16(frame, dlen + 4);
            frame[dlen + 4] = (byte)(crc16 & 0xff);
            frame[dlen + 5] = (byte)(crc16 >> 8 & 0xff);
            return frame;
        }
    }
}
