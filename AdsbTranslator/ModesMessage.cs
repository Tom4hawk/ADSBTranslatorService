using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdsbTranslator
{
    class ModesMessage
    {
        /* Useful consts */
        const int _longMessageBytesSize = 14;//bytes (112 bits)
        const int _shortMessageBytesSize = 7;//bytes (56 bits)
        const int _longMessageBitsSize = _longMessageBytesSize * 8;
        const int _shortMessageBitsSize = _shortMessageBytesSize * 8;

        /* Generic fields */
        public byte[] msg; /* Binary message. */
        public int msgbits; /* Number of bits in message */
        public int msgtype; /* Downlink format # */
        public bool crcok; /* True if CRC was valid */
        public UInt32 crc; /* Message CRC */
        public int errorbit; /* Bit corrected. -1 if no bit corrected. */
        public int aa1, aa2, aa3; /* ICAO Address bytes 1 2 and 3 */

        /* DF 11 */
        public int ca; /* Responder capabilities. */

        /* DF 17 */
        public int metype; /* Extended squitter message type. */
        public int mesub; /* Extended squitter message subtype. */
        public int heading_is_valid;
        public int heading;
        public int aircraft_type;
        public int fflag; /* 1 = Odd, 0 = Even CPR message. */
        public int tflag; /* UTC synchronized? */
        public int raw_latitude; /* Non decoded latitude */
        public int raw_longitude; /* Non decoded longitude */
        public char[] flight;// /* 8 chars flight number. */
        public int ew_dir; /* 0 = East, 1 = West. */
        public int ew_velocity; /* E/W velocity. */
        public int ns_dir; /* 0 = North, 1 = South. */
        public int ns_velocity; /* N/S velocity. */
        public int vert_rate_source; /* Vertical rate source. */
        public int vert_rate_sign; /* Vertical rate sign. */
        public int vert_rate; /* Vertical rate. */
        public int velocity; /* Computed from EW and NS velocity. */

        /* DF4, DF5, DF20, DF21 */
        public int fs; /* Flight status for DF4,5,20,21 */
        public int dr; /* Request extraction of downlink request. */
        public int um; /* Request extraction of downlink request. */
        public int identity; /* 13 bits identity (Squawk). */

        /* Fields used by multiple message types. */
        public int altitude;
        public int unit;

        public double lat;
        public double lon;
        
        public ModesMessage()
        {
            msg = new byte[_longMessageBytesSize];
            flight = new char[9];
        }
    }
}
