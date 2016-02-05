using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdsbTranslator
{
    /* Storage class for information about particular aircraft */
    class Aircraft
    {
        public UInt32 addr;//ICAO address
        public UInt32 lastSeen;//Unix Time Stamp (s) of last successfully decoded message
        public char[] flight;//8 chars flight number - probably not needed here

         /* Partial data - you have to decode full information
         * How-to: http://www.lll.lu/~edward/edward/adsb/DecodingADSBposition.html
         * We decode full information only when time between frames is =< 10 seconds
         */
        public int oddFrameCprLatitude;
        public int oddFrameCprLongitude;
        public int evenFrameCprLatitude;
        public int evenFrameCprLongitude;
        public UInt32 oddFrameCprTime;//Unix Time Stamp (s) of last successfully decoded message with odd frame
        public UInt32 evenFrameCprTime;//Unix Time Stamp (s) of last successfully decoded message with even frame

        public Aircraft()
        {
            flight = new char[9];
        }
    }


}
