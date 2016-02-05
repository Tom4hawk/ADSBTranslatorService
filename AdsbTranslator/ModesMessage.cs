using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdsbTranslator
{
    /* Storage for data from one message */
    class DecodedMessage
    {
        /* Generic fields */
        public byte[] message; // binary message
        public int msgSize; // in bits
        public int downlinkFormat;
        public bool validCrc;
        public UInt32 messageCrc;
        public int correctedBit; // -1 = nothing corrected but something is wrong
        public int icaoAddrPartOne, icaoAddrPartTwo, icaoAddrPartThree; // ICAO Address hex 1, 2 and 3

        /* DF 11 */
        public int responderCapabilities;

        /* DF 17 - extended squitter(es) */
        public int esMessageType;
        public int esMessageSubType;
        public int track; //calculated not decoded
        public int parityFlag; // 1 = Odd, 0 = Even CPR message
        public int rawLatitude; // Non decoded latitude - single frame
        public int rawLongitude; // Non decoded longitude - single frame
        public char[] flightNumber; //8 chars flight number
        public int ewDirection; // 0 = East, 1 = West
        public int ewVelocity; // E/W velocity
        public int nsDirection; // 0 = North, 1 = South
        public int nsVelocity; // N/S velocity
        public int verticalRateSign;
        public int verticalRate;
        public int velocity; // Computed from ewVelocity and nsVelocity

        /* DF4, DF5, DF20, DF21 */
        public int flightStatus;
        public int squawkIdentity; // Squawk(13 bits identity)

        /* - */
        public int altitude;
        public int unit;

        public double latitude;
        public double longitude;
        
        public DecodedMessage()
        {
            message = new byte[14]; //Long message size in bytes
            flightNumber = new char[9];
        }
    }
}
