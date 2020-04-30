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
                    for (int ch = 0; ch < 8; ch++)
                    {
                        Console.Write("{0:X}", recdata[16 + ch]);
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
        public bool Write(byte[] data, Int32 address)
        {
            UInt32 offset = 0;
            UInt32 addr = (UInt32)address;
            UInt32 blksize = 512;
            
            {
                while (offset < data.Length)
                {
                    var buf = this.Write10.Sptw.GetDataBuffer();
                    if (offset + blksize < data.Length)
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
                        Array.Copy(zero, 0, buf, data.Length - offset,  blksize - (data.Length - offset) );
                    }
                    this.Write10.SetBounds(addr, 1);
                    PrintBuffer(this.Write10.Sptw.sptBuffered.Spt.Cdb, 0,10);
                    
                    if (!this.Execute(ScsiCommandCode.Write10))
                    {
                        Console.WriteLine("Error Ioctl: 0x{0:X8}", this.usb.GetError());
                        return false;
                    }
                    addr += blksize/512;
                    offset += blksize;

                    Console.WriteLine("write 0x{0:X} bytes to 0x{1:X}\n", offset, addr);
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
}
