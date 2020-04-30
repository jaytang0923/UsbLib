using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UsbLib.Scsi.Commands
{
    public class Write10 : ScsiCommand
    {
        public void SetBounds(UInt32 lba, UInt32 sectors)
        {
            var lbaBytes = BitConverter.GetBytes(lba).Reverse<byte>().ToArray();
            ushort blocksize = 256;
            var sectorsBytes = BitConverter.GetBytes(blocksize).Reverse<byte>().ToArray();

            this.Sptw.SetCdb(lbaBytes, 0, 2, 4);
            this.Sptw.SetCdb(sectorsBytes, 0, 8, 1);

            this.Sptw.SetDataLength((uint)(512));
        }

        public Write10() :
            base(new ScsiPassThroughWrapper(new byte[] { (byte)ScsiCommandCode.Write10, 0, 0, 4, 0, 0, 0, 0 , 1, 0},
                DataDirection.SCSI_IOCTL_OUT, 0x200))
        {

        }
    }
}
