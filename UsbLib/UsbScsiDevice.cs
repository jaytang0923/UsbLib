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

        /* write cmd and read ack*/
        public bool Write(byte[] data, UInt32 datalen)
        {
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
    }
}
